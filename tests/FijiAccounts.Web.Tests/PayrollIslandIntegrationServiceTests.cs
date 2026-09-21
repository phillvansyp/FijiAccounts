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

    private static PayrollIslandPayRunPayload DetailedRun() => PayRun() with
    {
        Employees = Enumerable.Range(0, 10).Select(i => new PayrollEmployeeDetail($"run-1:EMP{i}", $"EMP{i:D4}",
            $"Employee Person{i}", new(2026, 8, 28), 1000m, 100m, 80m, 100m, 20m, 800m)).ToArray()
    };

    [Fact]
    public async Task Employee_enrichment_preserves_posted_journal_and_report_opens_payroll()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var client = new FakePayrollIslandClient(Page(PayRun()));
        var service = Service(test, client);
        await ConnectAsync(test, service);
        await service.SyncAsync(test.UserId, test.Organisation.Id);
        var original = await test.Db.PayrollIslandPayRunImports.SingleAsync();
        var journal = await service.PostPayRunAsync(test.UserId, test.Organisation.Id, original.Id);
        client.Page = Page(DetailedRun() with { Revision = 2 });
        await service.SyncAsync(test.UserId, test.Organisation.Id);
        var current = await test.Db.PayrollIslandPayRunImports.SingleAsync(x => x.Status == PayrollIslandImportStatus.Posted);
        Assert.Equal(journal.Id, current.PostedJournalId);
        Assert.Equal(10, PayrollEmployeeDetail.Read(current).Count);
        Assert.Single(await test.Db.PostedJournals.ToListAsync());
        var report = await new ReportTransactionService(test.Db, test.Access).GetAsync(test.UserId, test.Organisation.Id,
            "6000", new(2026, 8, 1), new(2026, 8, 31));
        Assert.All(report.Transactions, x => Assert.EndsWith($"/payroll/{current.Id}", x.SourceUrl));
        client.Page = Page(DetailedRun() with { Revision = 2, Employees = DetailedRun().Employees!.Reverse().ToArray() });
        Assert.Equal(1, (await service.SyncAsync(test.UserId, test.Organisation.Id)).Skipped);
    }

    [Fact]
    public async Task Invalid_employee_breakdown_is_rejected_before_import()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var run = DetailedRun();
        var invalid = run with { Employees = run.Employees!.Select((x, i) => i == 0 ? x with { NetPay = 801m } : x).ToArray() };
        var service = Service(test, new FakePayrollIslandClient(Page(invalid)));
        await ConnectAsync(test, service);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SyncAsync(test.UserId, test.Organisation.Id));
        Assert.Empty(await test.Db.PayrollIslandPayRunImports.ToListAsync());
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    public async Task Automatic_matching_is_unique_idempotent_and_preserves_expenses(int copies, bool existingPayment)
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = Service(test, new FakePayrollIslandClient(Page(DetailedRun())));
        await ConnectAsync(test, service);
        await service.SyncAsync(test.UserId, test.Organisation.Id);
        var import = await test.Db.PayrollIslandPayRunImports.SingleAsync();
        await service.PostPayRunAsync(test.UserId, test.Organisation.Id, import.Id);
        if (existingPayment)
            await test.Posting.PostAsync(test.UserId, new(test.Organisation.Id, new(2026, 8, 28), "WAGES", "Employee Person0",
                [new(test.Account("2200").Id, "Employee Person0", 800m, 0), new(test.Account("1000").Id, "Employee Person0", 0, 800m)]));
        for (var i = 0; i < copies; i++)
            await test.Reconciliation.AddStatementLineAsync(test.UserId, new(test.Organisation.Id, test.Account("1000").Id,
                new(2026, 8, 28), "Employee Person0", null, -800m));
        var matching = new PayrollBankMatchingService(test.Db, test.Access, test.Posting, test.Reconciliation);
        Assert.Equal(copies == 1 ? 1 : 0, await matching.MatchAsync(test.UserId, test.Organisation.Id));
        Assert.Equal(0, await matching.MatchAsync(test.UserId, test.Organisation.Id));
        Assert.Equal(11000m, await test.AccountBalanceAsync("6000"));
        Assert.Equal(copies == 1 ? -800m : 0m, await test.AccountBalanceAsync("1000"));
        Assert.Equal(copies == 1 ? 2 : 1, await test.Db.PostedJournals.CountAsync());
    }

    [Theory]
    [InlineData("Employee Person0", -800, 28, true)]
    [InlineData("WAGES EMP0000", -800, 28, true)]
    [InlineData("Employee Person1", -800, 28, false)]
    [InlineData("Employee Person0", -799, 28, false)]
    [InlineData("Employee Person0", 800, 28, false)]
    [InlineData("Employee Person0", -800, 27, false)]
    [InlineData("Employee Person01", -800, 28, false)]
    public void Automatic_matching_requires_employee_exact_amount_and_date(string name, decimal amount, int day, bool expected)
    {
        var statement = new BankStatementLine { Description = name, Amount = amount, TransactionDate = new(2026, 8, day) };
        Assert.Equal(expected, PayrollBankMatchingService.Matches(DetailedRun().Employees![0], statement));
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
