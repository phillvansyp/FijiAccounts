using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SupplierCreditNoteAccountingTests
{
    [Fact]
    public async Task CreateAsync_WhenVatReceivableControlAccountIsInactive_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill = await CreateBillAsync(test);

        var vatReceivable = test.Account("1150");
        vatReceivable.IsActive = false;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var service =
            new SupplierCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new SupplierCreditNoteRequest(
                            OrganisationId: test.Organisation.Id,
                            SupplierBillId: bill.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reason: "Inactive VAT receivable control test",
                            Amount: 50m,
                            ReturnTrackedItems: false)));

        Assert.Contains(
            "1150",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_WhenVatReceivableControlAccountHasWrongType_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill = await CreateBillAsync(test);

        var vatReceivable = test.Account("1150");
        vatReceivable.Type = AccountType.Liability;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var service =
            new SupplierCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new SupplierCreditNoteRequest(
                            OrganisationId: test.Organisation.Id,
                            SupplierBillId: bill.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reason: "Invalid VAT receivable control type test",
                            Amount: 50m,
                            ReturnTrackedItems: false)));

        Assert.Contains(
            "1150",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_WhenAccountsPayableControlAccountIsInactive_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill = await CreateBillAsync(test);

        var accountsPayable = test.Account("2000");
        accountsPayable.IsActive = false;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var service =
            new SupplierCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new SupplierCreditNoteRequest(
                            OrganisationId: test.Organisation.Id,
                            SupplierBillId: bill.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reason: "Inactive AP control test",
                            Amount: 50m,
                            ReturnTrackedItems: false)));

        Assert.Contains(
            "2000",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_WhenAccountsPayableControlAccountHasWrongType_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill = await CreateBillAsync(test);

        var accountsPayable = test.Account("2000");
        accountsPayable.Type = AccountType.Asset;
        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var service =
            new SupplierCreditNoteService(
                test.Db,
                test.Access,
                test.Posting);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new SupplierCreditNoteRequest(
                            OrganisationId: test.Organisation.Id,
                            SupplierBillId: bill.Id,
                            Date: new DateOnly(2026, 8, 20),
                            Reason: "Invalid AP control type test",
                            Amount: 50m,
                            ReturnTrackedItems: false)));

        Assert.Contains(
            "2000",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());
    }

    private static Task<FijiAccounts.Web.Data.SupplierBill> CreateBillAsync(
        AccountingTestDatabase test) =>
        test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                OrganisationId: test.Organisation.Id,
                SupplierId: test.Supplier.Id,
                SupplierReference: $"SUP-CREDIT-CONTROL-{Guid.NewGuid():N}",
                BillDate: new DateOnly(2026, 8, 18),
                DueDate: new DateOnly(2026, 9, 17),
                Lines:
                [
                    new SupplierBillLineRequest(
                        Description: "Supplier credit control test",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        ExpenseAccountId: test.Account("6500").Id)
                ]));
}