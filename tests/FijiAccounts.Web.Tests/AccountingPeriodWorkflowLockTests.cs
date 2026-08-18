using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class AccountingPeriodWorkflowLockTests
{
    [Fact]
    public async Task SalesInvoice_InLockedPeriod_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        await LockAugust2026Async(test);

        var before =
            await test.Db.PostedJournals.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    test.SalesInvoices.CreateAndPostAsync(
                        test.UserId,
                        new SalesInvoiceRequest(
                            OrganisationId: test.Organisation.Id,
                            CustomerId: test.Customer.Id,
                            IssueDate: new DateOnly(2026, 8, 18),
                            DueDate: new DateOnly(2026, 9, 17),
                            Lines:
                            [
                                new SalesInvoiceLineRequest(
                                    Description: "Locked invoice",
                                    Quantity: 1m,
                                    UnitPrice: 100m,
                                    VatTreatment: VatTreatment.Standard,
                                    RevenueAccountId:
                                        test.Account("4000").Id)
                            ])));

        Assert.Equal(
            "The accounting period is locked.",
            ex.Message);

        Assert.Equal(
            before,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task SupplierBill_InLockedPeriod_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        await LockAugust2026Async(test);

        var before =
            await test.Db.PostedJournals.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    test.Purchasing.PostBillAsync(
                        test.UserId,
                        new SupplierBillRequest(
                            OrganisationId: test.Organisation.Id,
                            SupplierId: test.Supplier.Id,
                            SupplierReference: "LOCK-BILL-001",
                            BillDate: new DateOnly(2026, 8, 18),
                            DueDate: new DateOnly(2026, 9, 17),
                            Lines:
                            [
                                new SupplierBillLineRequest(
                                    Description: "Locked bill",
                                    Quantity: 1m,
                                    UnitPrice: 100m,
                                    VatTreatment: VatTreatment.Standard,
                                    ExpenseAccountId:
                                        test.Account("6500").Id)
                            ])));

        Assert.Equal(
            "The accounting period is locked.",
            ex.Message);

        Assert.Equal(
            before,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task BankCoding_InLockedPeriod_IsRejectedAndStatementRemainsUnreconciled()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        bank.BankAccountKind =
            BankAccountKind.DebitCard;

        await test.Db.SaveChangesAsync();

        var statement =
            await test.Reconciliation.AddStatementLineAsync(
                test.UserId,
                new StatementLineRequest(
                    OrganisationId: test.Organisation.Id,
                    BankAccountId: bank.Id,
                    Date: new DateOnly(2026, 8, 18),
                    Description: "Locked bank transaction",
                    Reference: "LOCK-BANK-001",
                    Amount: -112.50m));

        await LockAugust2026Async(test);

        var before =
            await test.Db.PostedJournals.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    test.BankCoding.PostAndReconcileAsync(
                        test.UserId,
                        new BankTransactionCodingRequest(
                            OrganisationId: test.Organisation.Id,
                            StatementLineId: statement.Id,
                            TargetAccountCode: "6500",
                            Description: "Locked bank transaction",
                            VatTreatment: VatTreatment.Standard)));

        Assert.Equal(
            "The accounting period is locked.",
            ex.Message);

        Assert.Equal(
            before,
            await test.Db.PostedJournals.CountAsync());

        var reloaded =
            await test.Db.BankStatementLines
                .AsNoTracking()
                .SingleAsync(x => x.Id == statement.Id);

        Assert.Null(reloaded.ReconciledAt);
        Assert.Null(reloaded.MatchedPostedJournalLineId);
        Assert.Null(reloaded.ReconciledByUserId);
    }

    [Fact]
    public async Task FixedAssetAcquisition_InLockedPeriod_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var fixedAssets =
            new FixedAssetService(
                test.Db,
                test.Access,
                test.Posting);

        var accumulatedDepreciation =
            new LedgerAccount
            {
                OrganisationId = test.Organisation.Id,
                Code = "1510",
                Name = "Accumulated Depreciation",
                Type = AccountType.Asset,
                IsSystemAccount = false,
                IsActive = true
            };

        test.Db.LedgerAccounts.Add(
            accumulatedDepreciation);

        await test.Db.SaveChangesAsync();

        await LockAugust2026Async(test);

        var before =
            await test.Db.PostedJournals.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    fixedAssets.CreateAsync(
                        test.UserId,
                        new FixedAssetRequest(
                            OrganisationId:
                                test.Organisation.Id,
                            Name:
                                "Locked Office Computer",
                            AcquisitionDate:
                                new DateOnly(2026, 8, 18),
                            Cost:
                                2400m,
                            ResidualValue:
                                400m,
                            UsefulLifeMonths:
                                36,
                            AssetAccountId:
                                test.Account("1500").Id,
                            DepreciationExpenseAccountId:
                                test.Account("6900").Id,
                            AccumulatedDepreciationAccountId:
                                accumulatedDepreciation.Id,
                            AcquisitionBankAccountId:
                                test.Account("1000").Id)));

        Assert.Equal(
            "The accounting period is locked.",
            ex.Message);

        Assert.Equal(
            before,
            await test.Db.PostedJournals.CountAsync());
    }

    private static async Task LockAugust2026Async(
        AccountingTestDatabase test)
    {
        test.Db.AccountingPeriods.Add(
            new AccountingPeriod
            {
                OrganisationId =
                    test.Organisation.Id,
                Name =
                    "August 2026",
                StartsOn =
                    new DateOnly(2026, 8, 1),
                EndsOn =
                    new DateOnly(2026, 8, 31),
                IsLocked =
                    true,
                LockedAt =
                    DateTimeOffset.UtcNow,
                LockedByUserId =
                    test.UserId
            });

        await test.Db.SaveChangesAsync();
    }
}