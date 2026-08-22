using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class FinancialRiskServiceTests
{
    [Fact]
    public async Task GetAsync_BucketsOutstandingBalancesByDaysOverdue()
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
                Reference = "RISK-TEST",
                PostedAt = DateTimeOffset.UtcNow,
                PostedByUserId = test.UserId
            };

        test.Db.PostedJournals.Add(journal);

        test.Db.SalesInvoices.AddRange(
            Invoice(test, 1, today.AddDays(1), 100m),
            Invoice(test, 2, today.AddDays(-1), 200m, paid: 50m),
            Invoice(test, 3, today.AddDays(-31), 300m, credited: 50m),
            Invoice(test, 4, today.AddDays(-61), 400m),
            Invoice(test, 5, today.AddDays(-91), 500m),
            Invoice(test, 6, today.AddDays(-10), 600m, status: InvoiceStatus.Draft),
            Invoice(test, 7, today.AddDays(-10), 700m, status: InvoiceStatus.Voided),
            Invoice(test, 8, today.AddDays(-10), 800m, paid: 800m));

        test.Db.SupplierBills.AddRange(
            Bill(test, journal.Id, 1, today, 80m),
            Bill(test, journal.Id, 2, today.AddDays(-15), 120m, paid: 20m),
            Bill(test, journal.Id, 3, today.AddDays(-45), 200m, credited: 50m),
            Bill(test, journal.Id, 4, today.AddDays(-75), 300m),
            Bill(test, journal.Id, 5, today.AddDays(-100), 400m),
            Bill(test, journal.Id, 6, today.AddDays(-10), 500m, status: BillStatus.Voided));

        await test.Db.SaveChangesAsync();

        var summary =
            await new FinancialRiskService(test.Db)
                .GetAsync(test.Organisation.Id);

        AssertSummary(
            summary.Receivables,
            current: 100m,
            days1To30: 150m,
            days31To60: 250m,
            days61To90: 400m,
            over90: 500m);

        AssertSummary(
            summary.Payables,
            current: 80m,
            days1To30: 100m,
            days31To60: 150m,
            days61To90: 300m,
            over90: 400m);
    }

    [Fact]
    public void RiskLevel_ReflectsOldestOutstandingBucket()
    {
        Assert.Equal(
            FinancialRiskLevel.Current,
            new AgingRiskSummary(100m, 0, 0, 0, 0).RiskLevel);

        Assert.Equal(
            FinancialRiskLevel.Watch,
            new AgingRiskSummary(100m, 1m, 0, 0, 0).RiskLevel);

        Assert.Equal(
            FinancialRiskLevel.High,
            new AgingRiskSummary(100m, 0, 0, 0, 1m).RiskLevel);
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
            InvoiceNumber = $"RISK-{Guid.NewGuid():N}",
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
        decimal credited = 0,
        BillStatus status = BillStatus.Posted) =>
        new()
        {
            OrganisationId = test.Organisation.Id,
            SupplierId = test.Supplier.Id,
            SequenceNumber = sequenceNumber,
            BillNumber = $"RISK-{Guid.NewGuid():N}",
            SupplierReference = $"RISK-{Guid.NewGuid():N}",
            BillDate = dueDate,
            DueDate = dueDate,
            Status = status,
            Subtotal = total,
            Total = total,
            AmountPaid = paid,
            AmountCredited = credited,
            PostedJournalId = journalId,
            CreatedByUserId = test.UserId
        };

    private static void AssertSummary(
        AgingRiskSummary actual,
        decimal current,
        decimal days1To30,
        decimal days31To60,
        decimal days61To90,
        decimal over90)
    {
        Assert.Equal(current, actual.Current);
        Assert.Equal(days1To30, actual.Days1To30);
        Assert.Equal(days31To60, actual.Days31To60);
        Assert.Equal(days61To90, actual.Days61To90);
        Assert.Equal(over90, actual.Over90);
        Assert.Equal(
            current + days1To30 + days31To60 + days61To90 + over90,
            actual.Total);
        Assert.Equal(
            days1To30 + days31To60 + days61To90 + over90,
            actual.Overdue);
    }
}
