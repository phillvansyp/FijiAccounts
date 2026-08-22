using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class CashflowForecastServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsCumulativeOutstandingAmountsAtEachHorizon()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var today =
            DateOnly.FromDateTime(DateTime.Today);

        var journal =
            new PostedJournal
            {
                OrganisationId = test.Organisation.Id,
                SequenceNumber = 1,
                EntryDate = today,
                Reference = "CASHFLOW-TEST",
                PostedAt = DateTimeOffset.UtcNow,
                PostedByUserId = test.UserId
            };

        test.Db.PostedJournals.Add(journal);

        test.Db.SalesInvoices.AddRange(
            Invoice(test, 1, today, 100m),
            Invoice(test, 2, today.AddDays(7), 200m, paid: 50m, credited: 25m),
            Invoice(test, 3, today.AddDays(30), 300m),
            Invoice(test, 4, today.AddDays(60), 400m, status: InvoiceStatus.Voided),
            Invoice(test, 5, today.AddDays(90), 500m),
            Invoice(test, 6, today.AddDays(91), 600m),
            Invoice(test, 7, today.AddDays(-1), 700m),
            Invoice(test, 8, today, 800m, status: InvoiceStatus.Draft));

        test.Db.SupplierBills.AddRange(
            Bill(test, journal.Id, 1, today, 40m),
            Bill(test, journal.Id, 2, today.AddDays(7), 50m, paid: 10m),
            Bill(test, journal.Id, 3, today.AddDays(30), 60m, credited: 10m),
            Bill(test, journal.Id, 4, today.AddDays(60), 70m),
            Bill(test, journal.Id, 5, today.AddDays(90), 80m),
            Bill(test, journal.Id, 6, today.AddDays(91), 90m));

        await test.Db.SaveChangesAsync();

        var forecast =
            await new CashflowForecastService(test.Db)
                .GetAsync(test.Organisation.Id);

        AssertPeriod(forecast.Today, 100m, 40m);
        AssertPeriod(forecast.Next7Days, 225m, 80m);
        AssertPeriod(forecast.Next30Days, 525m, 130m);
        AssertPeriod(forecast.Next60Days, 525m, 200m);
        AssertPeriod(forecast.Next90Days, 1025m, 280m);
    }

    private static SalesInvoice Invoice(
        AccountingTestDatabase test,
        long sequenceNumber,
        DateOnly dueDate,
        decimal total,
        decimal paid = 0,
        decimal credited = 0,
        InvoiceStatus status = InvoiceStatus.Posted) =>
        new()
        {
            OrganisationId = test.Organisation.Id,
            CustomerId = test.Customer.Id,
            SequenceNumber = sequenceNumber,
            InvoiceNumber = $"CF-{Guid.NewGuid():N}",
            IssueDate = dueDate,
            DueDate = dueDate,
            Status = status,
            Subtotal = total,
            Total = total,
            AmountPaid = paid,
            AmountCredited = credited,
            CreatedByUserId = test.UserId
        };

    private static SupplierBill Bill(
        AccountingTestDatabase test,
        Guid journalId,
        long sequenceNumber,
        DateOnly dueDate,
        decimal total,
        decimal paid = 0,
        decimal credited = 0) =>
        new()
        {
            OrganisationId = test.Organisation.Id,
            SupplierId = test.Supplier.Id,
            SequenceNumber = sequenceNumber,
            BillNumber = $"CF-{Guid.NewGuid():N}",
            SupplierReference = $"CF-{Guid.NewGuid():N}",
            BillDate = dueDate,
            DueDate = dueDate,
            Status = BillStatus.Posted,
            Subtotal = total,
            Total = total,
            AmountPaid = paid,
            AmountCredited = credited,
            PostedJournalId = journalId,
            CreatedByUserId = test.UserId
        };

    private static void AssertPeriod(
        CashflowPeriodSummary actual,
        decimal receipts,
        decimal payments)
    {
        Assert.Equal(receipts, actual.ExpectedReceipts);
        Assert.Equal(payments, actual.ExpectedPayments);
        Assert.Equal(receipts - payments, actual.NetMovement);
    }
}
