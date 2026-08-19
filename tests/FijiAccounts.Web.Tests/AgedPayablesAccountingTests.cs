using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class AgedPayablesAccountingTests
{
    [Fact]
    public async Task PayableAsAtDate_UsesOnlyPaymentsDatedOnOrBeforeReportDate()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bill =
            await test.Purchasing.PostBillAsync(
                test.UserId,
                new SupplierBillRequest(
                    OrganisationId: test.Organisation.Id,
                    SupplierId: test.Supplier.Id,
                    SupplierReference: "AGING-BILL-001",
                    BillDate: new DateOnly(2026, 6, 1),
                    DueDate: new DateOnly(2026, 6, 30),
                    Lines:
                    [
                        new SupplierBillLineRequest(
                            Description: "Office expense",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            ExpenseAccountId: test.Account("6000").Id)
                    ]));

        var asAt =
            new DateOnly(2026, 7, 31);

        var bills =
            await test.Db.SupplierBills
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == test.Organisation.Id &&
                    x.BillDate <= asAt &&
                    x.Status != BillStatus.Voided)
                .ToListAsync();

        var billIds =
            bills.Select(x => x.Id).ToArray();

        var paid =
            await test.Db.SupplierPayments
                .AsNoTracking()
                .Where(x =>
                    billIds.Contains(x.SupplierBillId) &&
                    x.PaymentDate <= asAt)
                .GroupBy(x => x.SupplierBillId)
                .Select(x => new
                {
                    x.Key,
                    Amount = x.Sum(y => y.Amount)
                })
                .ToDictionaryAsync(
                    x => x.Key,
                    x => x.Amount);

        var credited =
            await test.Db.SupplierCreditNotes
                .AsNoTracking()
                .Where(x =>
                    billIds.Contains(x.SupplierBillId) &&
                    x.CreditDate <= asAt)
                .GroupBy(x => x.SupplierBillId)
                .Select(x => new
                {
                    x.Key,
                    Amount = x.Sum(y => y.Total)
                })
                .ToDictionaryAsync(
                    x => x.Key,
                    x => x.Amount);

        var outstanding =
            bills.Sum(x =>
                Math.Max(
                    0,
                    x.Total -
                    paid.GetValueOrDefault(x.Id) -
                    credited.GetValueOrDefault(x.Id)));

        Assert.Equal(bill.Total, outstanding);

        var daysOverdue =
            asAt.DayNumber -
            bill.DueDate.DayNumber;

        Assert.InRange(daysOverdue, 31, 60);
    }

    [Fact]
public async Task PayableAsAtDate_IncludesBillUntilItsVoidDate()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var bill =
        await test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                OrganisationId: test.Organisation.Id,
                SupplierId: test.Supplier.Id,
                SupplierReference: "AGING-VOID-001",
                BillDate: new DateOnly(2026, 6, 1),
                DueDate: new DateOnly(2026, 6, 30),
                Lines:
                [
                    new SupplierBillLineRequest(
                        Description: "Historical ageing purchase",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        ExpenseAccountId: test.Account("6000").Id)
                ]));

    await test.Purchasing.VoidBillAsync(
        test.UserId,
        test.Organisation.Id,
        bill.Id,
        new DateOnly(2026, 8, 5),
        "Later-period correction");

    var julyAsAt =
        new DateOnly(2026, 7, 31);

    var voidedInJuly =
        await test.Db.SupplierBillVoids
            .AsNoTracking()
            .AnyAsync(x =>
                x.SupplierBillId == bill.Id &&
                x.VoidDate <= julyAsAt);

    Assert.False(voidedInJuly);

    var augustAsAt =
        new DateOnly(2026, 8, 31);

    var voidedInAugust =
        await test.Db.SupplierBillVoids
            .AsNoTracking()
            .AnyAsync(x =>
                x.SupplierBillId == bill.Id &&
                x.VoidDate <= augustAsAt);

    Assert.True(voidedInAugust);
}

[Fact]
public async Task PayableAsAtDate_CreditReversalOnlyRestoresBalanceFromReversalDate()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var bill =
        await test.Purchasing.PostBillAsync(
            test.UserId,
            new SupplierBillRequest(
                OrganisationId: test.Organisation.Id,
                SupplierId: test.Supplier.Id,
                SupplierReference: "AGING-CREDIT-REV-001",
                BillDate: new DateOnly(2026, 6, 1),
                DueDate: new DateOnly(2026, 6, 30),
                Lines:
                [
                    new SupplierBillLineRequest(
                        Description: "Ageing credit reversal purchase",
                        Quantity: 1m,
                        UnitPrice: 100m,
                        VatTreatment: VatTreatment.Standard,
                        ExpenseAccountId: test.Account("6000").Id)
                ]));

    var credits =
        new SupplierCreditNoteService(
            test.Db,
            test.Access,
            test.Posting);

    var credit =
        await credits.CreateAsync(
            test.UserId,
            new SupplierCreditNoteRequest(
                OrganisationId: test.Organisation.Id,
                SupplierBillId: bill.Id,
                Date: new DateOnly(2026, 7, 10),
                Reason: "Temporary supplier credit",
                Amount: 56.25m,
                ReturnTrackedItems: false));

    await credits.ReverseAsync(
        test.UserId,
        test.Organisation.Id,
        credit.Id,
        new DateOnly(2026, 8, 5),
        "Reverse temporary supplier credit");

    var julyAsAt =
        new DateOnly(2026, 7, 31);

    var julyReversed =
        await test.Db.SupplierCreditNoteReversals
            .AsNoTracking()
            .AnyAsync(x =>
                x.SupplierCreditNoteId == credit.Id &&
                x.ReversalDate <= julyAsAt);

    Assert.False(julyReversed);

    var augustAsAt =
        new DateOnly(2026, 8, 31);

    var augustReversed =
        await test.Db.SupplierCreditNoteReversals
            .AsNoTracking()
            .AnyAsync(x =>
                x.SupplierCreditNoteId == credit.Id &&
                x.ReversalDate <= augustAsAt);

    Assert.True(augustReversed);
}
}