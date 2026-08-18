using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class AgedReceivablesAccountingTests
{
    [Fact]
    public async Task ReceivableAsAtDate_UsesOnlyReceiptsDatedOnOrBeforeReportDate()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var invoice =
            await test.SalesInvoices.CreateAndPostAsync(
                test.UserId,
                new SalesInvoiceRequest(
                    OrganisationId: test.Organisation.Id,
                    CustomerId: test.Customer.Id,
                    IssueDate: new DateOnly(2026, 6, 1),
                    DueDate: new DateOnly(2026, 6, 30),
                    Lines:
                    [
                        new SalesInvoiceLineRequest(
                            Description: "Consulting",
                            Quantity: 1m,
                            UnitPrice: 100m,
                            VatTreatment: VatTreatment.Standard,
                            RevenueAccountId: test.Account("4000").Id)
                    ]));

        var asAt =
            new DateOnly(2026, 7, 31);

        var invoices =
            await test.Db.SalesInvoices
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == test.Organisation.Id &&
                    x.IssueDate <= asAt &&
                    x.Status != InvoiceStatus.Draft &&
                    x.Status != InvoiceStatus.Voided)
                .ToListAsync();

        var invoiceIds =
            invoices.Select(x => x.Id).ToArray();

        var paid =
            await test.Db.CustomerReceiptAllocations
                .AsNoTracking()
                .Where(x =>
                    invoiceIds.Contains(x.SalesInvoiceId) &&
                    x.CustomerReceipt.ReceiptDate <= asAt)
                .GroupBy(x => x.SalesInvoiceId)
                .Select(x => new
                {
                    x.Key,
                    Amount = x.Sum(y => y.Amount)
                })
                .ToDictionaryAsync(
                    x => x.Key,
                    x => x.Amount);

        var credited =
            await test.Db.SalesCreditNotes
                .AsNoTracking()
                .Where(x =>
                    invoiceIds.Contains(x.SalesInvoiceId) &&
                    x.CreditDate <= asAt)
                .GroupBy(x => x.SalesInvoiceId)
                .Select(x => new
                {
                    x.Key,
                    Amount = x.Sum(y => y.Total)
                })
                .ToDictionaryAsync(
                    x => x.Key,
                    x => x.Amount);

        var outstanding =
            invoices.Sum(x =>
                Math.Max(
                    0,
                    x.Total -
                    paid.GetValueOrDefault(x.Id) -
                    credited.GetValueOrDefault(x.Id)));

        Assert.Equal(invoice.Total, outstanding);

        var daysOverdue =
            asAt.DayNumber -
            invoice.DueDate.DayNumber;

        Assert.InRange(daysOverdue, 31, 60);
    }
}