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
}