using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class CashRunwayServiceTests
{
    [Fact]
    public async Task GetAsync_FindsFirstProjectedShortfallWithinNinetyDays()
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
                Reference = "RUNWAY-TEST",
                PostedAt = DateTimeOffset.UtcNow,
                PostedByUserId = test.UserId,
                Lines =
                [
                    new PostedJournalLine
                    {
                        LedgerAccountId = test.Account("1000").Id,
                        Description = "Opening cash",
                        Debit = 100m
                    }
                ]
            };

        test.Db.PostedJournals.Add(journal);

        test.Db.SalesInvoices.AddRange(
            Invoice(test, 1, today.AddDays(-1), 20m),
            Invoice(test, 2, today.AddDays(10), 50m),
            Invoice(test, 3, today.AddDays(30), 100m),
            Invoice(test, 4, today.AddDays(91), 1000m),
            Invoice(test, 5, today.AddDays(5), 500m, status: InvoiceStatus.Draft));

        test.Db.SupplierBills.AddRange(
            Bill(test, journal.Id, 1, today.AddDays(-1), 30m),
            Bill(test, journal.Id, 2, today.AddDays(20), 200m),
            Bill(test, journal.Id, 3, today.AddDays(5), 500m, status: BillStatus.Voided));

        await test.Db.SaveChangesAsync();

        var forecast =
            await new CashRunwayService(test.Db)
                .GetAsync(test.Organisation.Id);

        Assert.Equal(today, forecast.AsAt);
        Assert.Equal(100m, forecast.OpeningCash);
        Assert.Equal(40m, forecast.ProjectedBalance30Days);
        Assert.Equal(40m, forecast.ProjectedBalance90Days);
        Assert.Equal(-60m, forecast.LowestProjectedBalance);
        Assert.Equal(today.AddDays(20), forecast.FirstShortfallDate);
        Assert.Equal(20, forecast.DaysUntilShortfall);
    }

    private static SalesInvoice Invoice(
        AccountingTestDatabase test,
        long sequenceNumber,
        DateOnly dueDate,
        decimal total,
        InvoiceStatus status = InvoiceStatus.Posted) =>
        new()
        {
            OrganisationId = test.Organisation.Id,
            CustomerId = test.Customer.Id,
            SequenceNumber = sequenceNumber,
            InvoiceNumber = $"RUNWAY-{Guid.NewGuid():N}",
            IssueDate = dueDate,
            DueDate = dueDate,
            Status = status,
            Subtotal = total,
            Total = total,
            CreatedByUserId = test.UserId
        };

    private static SupplierBill Bill(
        AccountingTestDatabase test,
        Guid journalId,
        long sequenceNumber,
        DateOnly dueDate,
        decimal total,
        BillStatus status = BillStatus.Posted) =>
        new()
        {
            OrganisationId = test.Organisation.Id,
            SupplierId = test.Supplier.Id,
            SequenceNumber = sequenceNumber,
            BillNumber = $"RUNWAY-{Guid.NewGuid():N}",
            SupplierReference = $"RUNWAY-{Guid.NewGuid():N}",
            BillDate = dueDate,
            DueDate = dueDate,
            Status = status,
            Subtotal = total,
            Total = total,
            PostedJournalId = journalId,
            CreatedByUserId = test.UserId
        };
}
