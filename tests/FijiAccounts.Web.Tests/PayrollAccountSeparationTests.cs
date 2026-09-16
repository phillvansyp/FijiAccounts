using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class PayrollAccountSeparationTests
{
    [Fact]
    public async Task SeparatesExistingPayrollAndPaymentsAndFutureImportsWithoutChangingBankOrProfit()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var (connection, payroll) = await Seed(t);
        var service = new PayrollAccountSeparationService(t.Db, t.Access, t.Posting);
        var plan = Plan(t, connection, payroll);
        var bank = t.Account("1000");
        var payment = await t.Posting.PostAsync(t.UserId, new(t.Organisation.Id, payroll.EntryDate, "FNPF-PAY", "FNPF payment",
            [new(t.Account("2200").Id, "FNPF", 180m, 0m), new(bank.Id, "Bank", 0m, 180m)]));
        var paymentLine = payment.Lines.Single(l => l.LedgerAccountId != bank.Id);
        plan.Lines.Add(new(paymentLine.Id, paymentLine.LedgerAccountId, 180m, 0m, "Fnpf"));
        var beforeCount = await t.Db.PostedJournals.CountAsync();
        await service.RunAsync(plan, false);
        Assert.Equal(beforeCount, await t.Db.PostedJournals.CountAsync());
        Assert.False(await t.Db.LedgerAccounts.AnyAsync(a => a.Code == "6010"));
        await service.RunAsync(plan, true);
        Assert.Equal(1000m, await t.AccountBalanceAsync("6000"));
        Assert.Equal(100m, await t.AccountBalanceAsync("6010"));
        Assert.Equal(0m, await t.AccountBalanceAsync("2200"));
        Assert.Equal(0m, await t.AccountBalanceAsync("2220"));
        Assert.Equal(-100m, await t.AccountBalanceAsync("2210"));
        Assert.Equal(-820m, await t.AccountBalanceAsync("2230"));
        Assert.Equal(-180m, await t.AccountBalanceAsync("1000"));
        Assert.Equal(180m, (await t.LoadJournalAsync(payment.Id)).Lines.Single(l => l.LedgerAccountId == bank.Id).Credit);
        var count = await t.Db.PostedJournals.CountAsync();
        Assert.Contains("Already applied", await service.RunAsync(plan, true));
        Assert.Equal(count, await t.Db.PostedJournals.CountAsync());

        // A ready import posts through the regular integration using the new mappings.
        var import = new PayrollIslandPayRunImport { OrganisationId = t.Organisation.Id, ConnectionId = connection.Id,
            ExternalPayRunId = "next-run", Revision = 1, PayRunNumber = "NEXT", PaymentDate = payroll.EntryDate,
            PeriodStart = payroll.EntryDate, PeriodEnd = payroll.EntryDate, Currency = "FJD", EmployeeCount = 1,
            GrossEarnings = 1000m, EmployerFnpf = 100m, EmployeeFnpf = 80m, EmployeePaye = 100m, NetPay = 820m,
            PayloadSha256 = "test", ImportedByUserId = t.UserId };
        t.Db.PayrollIslandPayRunImports.Add(import);
        await t.Db.SaveChangesAsync();
        var integration = new PayrollIslandIntegrationService(t.Db, t.Access, t.Posting, new UnusedClient(), new EphemeralDataProtectionProvider());
        await integration.PostPayRunAsync(t.UserId, t.Organisation.Id, import.Id);
        Assert.Equal(2000m, await t.AccountBalanceAsync("6000"));
        Assert.Equal(200m, await t.AccountBalanceAsync("6010"));
        Assert.Equal(-180m, await t.AccountBalanceAsync("2220"));
        Assert.Equal(-200m, await t.AccountBalanceAsync("2210"));
        Assert.Equal(-180m, await t.AccountBalanceAsync("1000"));
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("changed")]
    [InlineData("bank")]
    public async Task InvalidPlanRollsBackAccountsMappingsAndJournals(string failure)
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var (connection, payroll) = await Seed(t);
        var plan = Plan(t, connection, payroll);
        if (failure == "locked")
        {
            t.Db.AccountingPeriods.Add(new AccountingPeriod { OrganisationId = t.Organisation.Id,
                Name = "Locked", StartsOn = payroll.EntryDate, EndsOn = payroll.EntryDate, IsLocked = true });
            await t.Db.SaveChangesAsync();
        }
        if (failure == "changed") plan.Lines[0] = plan.Lines[0] with { Debit = 999m };
        if (failure == "bank") plan.Accounts[0] = new("Wages", "1000", "Bank");
        var count = await t.Db.PostedJournals.CountAsync();
        var service = new PayrollAccountSeparationService(t.Db, t.Access, t.Posting);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(plan, true));
        Assert.Equal(count, await t.Db.PostedJournals.CountAsync());
        Assert.False(await t.Db.LedgerAccounts.AnyAsync(a => a.Code == "6010"));
        Assert.Equal(connection.FnpfPayableAccountId, (await t.Db.PayrollIslandConnections.AsNoTracking().SingleAsync()).FnpfPayableAccountId);
    }

    private static async Task<(PayrollIslandConnection, PostedJournal)> Seed(AccountingTestDatabase t)
    {
        var wages = t.Account("6000").Id;
        var payable = t.Account("2200").Id;
        var connection = new PayrollIslandConnection { OrganisationId = t.Organisation.Id,
            BaseUrl = "https://payroll.example.test", PayrollOrganisationId = "test", ProtectedAccessToken = "unused",
            WagesExpenseAccountId = wages, EmployerContributionsExpenseAccountId = wages,
            NetWagesPayableAccountId = payable, PayePayableAccountId = payable, FnpfPayableAccountId = payable,
            OtherDeductionsPayableAccountId = payable, CreatedByUserId = t.UserId, UpdatedByUserId = t.UserId };
        t.Db.PayrollIslandConnections.Add(connection);
        await t.Db.SaveChangesAsync();
        var journal = await t.Posting.PostAsync(t.UserId, new(t.Organisation.Id, new(2026, 8, 28), "PAYROLL-OLD", "Payroll",
            [new(wages, "Wages", 1000m, 0m), new(wages, "EmployerFnpf", 100m, 0m),
                new(payable, "NetWages", 0m, 820m), new(payable, "Paye", 0m, 100m), new(payable, "Fnpf", 0m, 180m)]));
        return (connection, journal);
    }

    private static PayrollAccountSeparationPlan Plan(AccountingTestDatabase t, PayrollIslandConnection c, PostedJournal j) =>
        new("split-test", t.Organisation.Id, t.UserId, c.Id,
            [new("Wages", "6000", "Wages and Salaries"), new("EmployerFnpf", "6010", "Employer FNPF Contributions"),
                new("Paye", "2210", "PAYE Payable"), new("Fnpf", "2220", "FNPF Payable"),
                new("NetWages", "2230", "Net Wages Payable"), new("OtherDeductions", "2240", "Other Payroll Deductions Payable")],
            j.Lines.Select(l => new PayrollLineMove(l.Id, l.LedgerAccountId, l.Debit, l.Credit, l.Description)).ToList());

    private sealed class UnusedClient : IPayrollIslandClient
    {
        public Task<PayrollIslandPayRunPage> GetFinalisedPayRunsAsync(string baseUrl, string payrollOrganisationId,
            string accessToken, string? afterCursor, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
