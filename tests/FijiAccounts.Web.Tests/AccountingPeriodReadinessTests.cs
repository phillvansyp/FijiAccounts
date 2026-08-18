using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class AccountingPeriodReadinessTests
{
    [Fact]
    public async Task EmptyPeriod_IsReady()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var period =
            await CreatePeriodAsync(test);

        var service =
            new AccountingPeriodService(
                test.Db,
                test.Access);

        var readiness =
            await service.GetReadinessAsync(
                test.UserId,
                test.Organisation.Id,
                period.Id);

        Assert.True(readiness.IsReady);
        Assert.Equal(0, readiness.WarningCount);
        Assert.Equal(0, readiness.UnreconciledBankStatementLines);
        Assert.Equal(0, readiness.IncompleteBankReconciliations);
        Assert.Equal(0, readiness.DraftSalesInvoices);
        Assert.Equal(0, readiness.DraftSupplierBills);
    }

    [Fact]
    public async Task UnreconciledStatementLine_InPeriod_IsReported()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var period =
            await CreatePeriodAsync(test);

        var bank = test.Account("1000");

        await test.Reconciliation.AddStatementLineAsync(
            test.UserId,
            new StatementLineRequest(
                OrganisationId: test.Organisation.Id,
                BankAccountId: bank.Id,
                Date: new DateOnly(2026, 7, 15),
                Description: "Unreconciled test",
                Reference: "READY-001",
                Amount: 100m));

        var service =
            new AccountingPeriodService(
                test.Db,
                test.Access);

        var readiness =
            await service.GetReadinessAsync(
                test.UserId,
                test.Organisation.Id,
                period.Id);

        Assert.False(readiness.IsReady);
        Assert.Equal(1, readiness.WarningCount);
        Assert.Equal(1, readiness.UnreconciledBankStatementLines);
    }

    [Fact]
    public async Task IncompleteReconciliation_OverlappingPeriod_IsReported()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var period =
            await CreatePeriodAsync(test);

        test.Db.BankReconciliationSessions.Add(
            new BankReconciliationSession
            {
                OrganisationId = test.Organisation.Id,
                BankAccountId = test.Account("1000").Id,
                StatementStartDate = new DateOnly(2026, 6, 25),
                StatementEndDate = new DateOnly(2026, 7, 5),
                OpeningStatementBalance = 0m,
                ClosingStatementBalance = 0m,
                LedgerBalance = 0m,
                Difference = 0m,
                IsCompleted = false,
                CreatedByUserId = test.UserId
            });

        await test.Db.SaveChangesAsync();

        var service =
            new AccountingPeriodService(
                test.Db,
                test.Access);

        var readiness =
            await service.GetReadinessAsync(
                test.UserId,
                test.Organisation.Id,
                period.Id);

        Assert.False(readiness.IsReady);
        Assert.Equal(1, readiness.IncompleteBankReconciliations);
    }

    [Fact]
    public async Task DraftSalesInvoice_InPeriod_IsReported()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var period =
            await CreatePeriodAsync(test);

        test.Db.SalesInvoices.Add(
            new SalesInvoice
            {
                OrganisationId = test.Organisation.Id,
                CustomerId = test.Customer.Id,
                InvoiceNumber = "DRAFT-001",
                IssueDate = new DateOnly(2026, 7, 10),
                DueDate = new DateOnly(2026, 8, 9),
                Status = InvoiceStatus.Draft,
                Subtotal = 0m,
                VatTotal = 0m,
                Total = 0m,
                AmountPaid = 0m,
                AmountCredited = 0m,
                CreatedByUserId = test.UserId
            });

        await test.Db.SaveChangesAsync();

        var service =
            new AccountingPeriodService(
                test.Db,
                test.Access);

        var readiness =
            await service.GetReadinessAsync(
                test.UserId,
                test.Organisation.Id,
                period.Id);

        Assert.False(readiness.IsReady);
        Assert.Equal(1, readiness.DraftSalesInvoices);
    }

    [Fact]
    public async Task DraftSupplierBill_InPeriod_IsReported()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var period =
            await CreatePeriodAsync(test);

        test.Db.SupplierBillDrafts.Add(
            new SupplierBillDraft
            {
                OrganisationId = test.Organisation.Id,
                SupplierId = test.Supplier.Id,
                SupplierReference = "DRAFT-BILL-001",
                BillDate = new DateOnly(2026, 7, 20),
                DueDate = new DateOnly(2026, 8, 19),
                Description = "Draft supplier bill",
                Quantity = 1m,
                UnitPrice = 100m,
                CreatedByUserId = test.UserId
            });

        await test.Db.SaveChangesAsync();

        var service =
            new AccountingPeriodService(
                test.Db,
                test.Access);

        var readiness =
            await service.GetReadinessAsync(
                test.UserId,
                test.Organisation.Id,
                period.Id);

        Assert.False(readiness.IsReady);
        Assert.Equal(1, readiness.DraftSupplierBills);
    }

    [Fact]
    public async Task ItemsOutsidePeriod_AreNotReported()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var period =
            await CreatePeriodAsync(test);

        var bank = test.Account("1000");

        await test.Reconciliation.AddStatementLineAsync(
            test.UserId,
            new StatementLineRequest(
                OrganisationId: test.Organisation.Id,
                BankAccountId: bank.Id,
                Date: new DateOnly(2026, 8, 1),
                Description: "Outside period",
                Reference: "READY-OUTSIDE",
                Amount: 100m));

        var service =
            new AccountingPeriodService(
                test.Db,
                test.Access);

        var readiness =
            await service.GetReadinessAsync(
                test.UserId,
                test.Organisation.Id,
                period.Id);

        Assert.True(readiness.IsReady);
        Assert.Equal(0, readiness.WarningCount);
    }

    private static async Task<AccountingPeriod> CreatePeriodAsync(
        AccountingTestDatabase test)
    {
        var period =
            new AccountingPeriod
            {
                OrganisationId = test.Organisation.Id,
                Name = "July 2026",
                StartsOn = new DateOnly(2026, 7, 1),
                EndsOn = new DateOnly(2026, 7, 31),
                IsLocked = false
            };

        test.Db.AccountingPeriods.Add(period);

        await test.Db.SaveChangesAsync();

        return period;
    }
}