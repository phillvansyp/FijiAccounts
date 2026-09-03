using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class PayrollIslandIntegrationServiceTests
{
    [Fact]
    public async Task SyncAsync_ImportsBalancedPayRunOnceAndProtectsToken()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var payRun = PayRun();
        var client = new FakePayrollIslandClient(Page(payRun));
        var service = Service(test, client);
        await ConnectAsync(test, service);

        var first = await service.SyncAsync(test.UserId, test.Organisation.Id);
        client.Page = Page(payRun with { Payments = payRun.Payments.Reverse().ToList() });
        var second = await service.SyncAsync(test.UserId, test.Organisation.Id);

        Assert.Equal(1, first.Imported);
        Assert.Equal(0, first.Skipped);
        Assert.Equal(0, second.Imported);
        Assert.Equal(1, second.Skipped);
        Assert.Equal("payroll-read-token-0123456789abcdef", client.LastToken);
        var connection = await test.Db.PayrollIslandConnections.AsNoTracking().SingleAsync();
        Assert.DoesNotContain("payroll-read-token-0123456789abcdef", connection.ProtectedAccessToken);
        Assert.Equal("cursor-1", connection.LastSyncCursor);
        var imported = await test.Db.PayrollIslandPayRunImports
            .AsNoTracking().Include(x => x.Payments).SingleAsync();
        Assert.Equal(PayrollIslandImportStatus.ReadyToPost, imported.Status);
        Assert.Equal(4, imported.Payments.Count);
        Assert.Single(await test.Db.AuditEvents
            .Where(x => x.EventType == "PayrollIslandPayRunImported")
            .ToListAsync());
    }

    [Fact]
    public async Task PostPayRunAsync_CreatesBalancedPayrollJournal()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var client = new FakePayrollIslandClient(Page(PayRun()));
        var service = Service(test, client);
        await ConnectAsync(test, service);
        await service.SyncAsync(test.UserId, test.Organisation.Id);
        var imported = await test.Db.PayrollIslandPayRunImports.SingleAsync();

        var journal = await service.PostPayRunAsync(
            test.UserId,
            test.Organisation.Id,
            imported.Id);

        var stored = await test.LoadJournalAsync(journal.Id);
        Assert.Equal(JournalPurpose.Payroll, stored.Purpose);
        Assert.Equal("FJD", stored.Currency);
        Assert.Equal(new DateOnly(2026, 8, 28), stored.EntryDate);
        Assert.Equal(11_000m, stored.Lines.Sum(x => x.Debit));
        Assert.Equal(11_000m, stored.Lines.Sum(x => x.Credit));
        Assert.Equal(11_000m, await test.AccountBalanceAsync("6000"));
        Assert.Equal(-11_000m, await test.AccountBalanceAsync("2200"));
        Assert.Equal(
            PayrollIslandImportStatus.Posted,
            (await test.Db.PayrollIslandPayRunImports.AsNoTracking().SingleAsync()).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostPayRunAsync(test.UserId, test.Organisation.Id, imported.Id));
    }

    [Fact]
    public async Task SyncAsync_PostedRevisionCreatesCorrectionReviewInsteadOfAnotherJournal()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var client = new FakePayrollIslandClient(Page(PayRun()));
        var service = Service(test, client);
        await ConnectAsync(test, service);
        await service.SyncAsync(test.UserId, test.Organisation.Id);
        var first = await test.Db.PayrollIslandPayRunImports.SingleAsync();
        await service.PostPayRunAsync(test.UserId, test.Organisation.Id, first.Id);
        client.Page = Page(PayRun(revision: 2, grossEarnings: 10_100m, netPay: 8_100m));

        await service.SyncAsync(test.UserId, test.Organisation.Id);

        var imports = await test.Db.PayrollIslandPayRunImports
            .AsNoTracking().OrderBy(x => x.Revision).ToListAsync();
        Assert.Equal(2, imports.Count);
        Assert.Equal(PayrollIslandImportStatus.Posted, imports[0].Status);
        Assert.Equal(PayrollIslandImportStatus.CorrectionRequired, imports[1].Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PostPayRunAsync(test.UserId, test.Organisation.Id, imports[1].Id));
        Assert.Single(await test.Db.PostedJournals.ToListAsync());
    }

    [Fact]
    public async Task SyncAsync_PaymentOnlyRevisionKeepsExistingJournal()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var client = new FakePayrollIslandClient(Page(PayRun()));
        var service = Service(test, client);
        await ConnectAsync(test, service);
        await service.SyncAsync(test.UserId, test.Organisation.Id);
        var first = await test.Db.PayrollIslandPayRunImports.SingleAsync();
        var journal = await service.PostPayRunAsync(
            test.UserId,
            test.Organisation.Id,
            first.Id);
        var updated = PayRun(revision: 2);
        updated = updated with
        {
            Payments = updated.Payments.Select(payment =>
                payment.Kind == "Paye"
                    ? payment with
                    {
                        Status = "Paid",
                        PaidDate = new DateOnly(2026, 9, 25)
                    }
                    : payment).ToList()
        };
        client.Page = Page(updated);

        await service.SyncAsync(test.UserId, test.Organisation.Id);

        var imports = await test.Db.PayrollIslandPayRunImports
            .AsNoTracking().OrderBy(x => x.Revision).ToListAsync();
        Assert.Equal(PayrollIslandImportStatus.Superseded, imports[0].Status);
        Assert.Equal(PayrollIslandImportStatus.Posted, imports[1].Status);
        Assert.Equal(journal.Id, imports[1].PostedJournalId);
        Assert.Single(await test.Db.PostedJournals.ToListAsync());
    }

    [Fact]
    public async Task SyncAsync_RejectsUnbalancedPayRunWithoutPartialImport()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var client = new FakePayrollIslandClient(Page(
            PayRun(),
            PayRun(externalId: "run-2", payRunNumber: "PR-002", grossEarnings: 9_999m)));
        var service = Service(test, client);
        await ConnectAsync(test, service);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SyncAsync(test.UserId, test.Organisation.Id));

        Assert.Contains("does not balance", exception.Message);
        Assert.Empty(await test.Db.PayrollIslandPayRunImports.ToListAsync());
    }

    [Fact]
    public async Task SaveConnectionAsync_RejectsNonHttpsAndWrongAccountTypes()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = Service(test, new FakePayrollIslandClient(Page()));
        var wages = test.Account("6000").Id;
        var liability = test.Account("2200").Id;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveConnectionAsync(
                test.UserId,
                test.Organisation.Id,
                Request("http://payroll.example.test", wages, liability)));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveConnectionAsync(
                test.UserId,
                test.Organisation.Id,
                Request("https://payroll.example.test", liability, liability)));
        Assert.Empty(await test.Db.PayrollIslandConnections.ToListAsync());
    }

    [Fact]
    public async Task ImportedPayrollSourceValuesAreAppendOnly()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = Service(test, new FakePayrollIslandClient(Page(PayRun())));
        await ConnectAsync(test, service);
        await service.SyncAsync(test.UserId, test.Organisation.Id);
        var imported = await test.Db.PayrollIslandPayRunImports.SingleAsync();
        imported.GrossEarnings += 1m;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.Db.SaveChangesAsync());

        Assert.Contains("payroll source records", exception.Message);
    }

    private static PayrollIslandIntegrationService Service(
        AccountingTestDatabase test,
        IPayrollIslandClient client) => new(
            test.Db,
            test.Access,
            test.Posting,
            client,
            new EphemeralDataProtectionProvider());

    private static Task<PayrollIslandConnection> ConnectAsync(
        AccountingTestDatabase test,
        PayrollIslandIntegrationService service)
    {
        var wages = test.Account("6000").Id;
        var liability = test.Account("2200").Id;
        return service.SaveConnectionAsync(
            test.UserId,
            test.Organisation.Id,
            Request("https://payroll.example.test", wages, liability));
    }

    private static PayrollIslandConnectionRequest Request(
        string baseUrl,
        Guid wagesAccountId,
        Guid liabilityAccountId) => new(
            baseUrl,
            "payroll-org-1",
            "payroll-read-token-0123456789abcdef",
            wagesAccountId,
            wagesAccountId,
            liabilityAccountId,
            liabilityAccountId,
            liabilityAccountId,
            liabilityAccountId);

    private static PayrollIslandPayRunPage Page(params PayrollIslandPayRunPayload[] payRuns) =>
        new(payRuns, "cursor-1");

    private static PayrollIslandPayRunPayload PayRun(
        string externalId = "run-1",
        int revision = 1,
        string payRunNumber = "PR-001",
        decimal grossEarnings = 10_000m,
        decimal netPay = 8_000m) => new(
            externalId,
            revision,
            payRunNumber,
            new DateOnly(2026, 8, 15),
            new DateOnly(2026, 8, 28),
            new DateOnly(2026, 8, 28),
            "FJD",
            10,
            grossEarnings,
            1_000m,
            800m,
            1_000m,
            200m,
            netPay,
            [
                new("payment-net-1", "NetWages", "Paid", new DateOnly(2026, 8, 28), new DateOnly(2026, 8, 28), netPay, "WAGES-PR-001"),
                new("payment-paye-1", "Paye", "Expected", new DateOnly(2026, 9, 30), null, 1_000m, "PAYE-PR-001"),
                new("payment-fnpf-1", "Fnpf", "Expected", new DateOnly(2026, 9, 30), null, 1_800m, "FNPF-PR-001"),
                new("payment-other-1", "OtherDeduction", "Expected", new DateOnly(2026, 9, 30), null, 200m, "OTHER-PR-001")
            ]);

    private sealed class FakePayrollIslandClient(PayrollIslandPayRunPage page)
        : IPayrollIslandClient
    {
        public PayrollIslandPayRunPage Page { get; set; } = page;
        public string? LastToken { get; private set; }

        public Task<PayrollIslandPayRunPage> GetFinalisedPayRunsAsync(
            string baseUrl,
            string payrollOrganisationId,
            string accessToken,
            string? afterCursor,
            CancellationToken cancellationToken = default)
        {
            LastToken = accessToken;
            return Task.FromResult(Page);
        }
    }
}
