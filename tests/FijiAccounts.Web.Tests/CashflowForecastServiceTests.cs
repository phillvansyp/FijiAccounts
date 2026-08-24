using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using FijiAccounts.Domain.Tax;

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
        Assert.Equal(12, forecast.Monthly.Count);
    }

    [Fact]
    public async Task GetAsync_IncludesRecurringTemplatesAndCommittedPurchasesWithBreakdown()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var asAt = new DateOnly(2026, 8, 24);
        var journal = new PostedJournal
        {
            OrganisationId = test.Organisation.Id,
            SequenceNumber = 1,
            EntryDate = asAt,
            Reference = "ADVANCED-CASHFLOW",
            PostedAt = DateTimeOffset.UtcNow,
            PostedByUserId = test.UserId
        };
        test.Db.PostedJournals.Add(journal);
        test.Db.SalesInvoices.Add(Invoice(test, 1, asAt.AddDays(1), 50m));
        test.Db.SupplierBills.Add(Bill(test, journal.Id, 1, asAt.AddDays(1), 20m));
        test.Db.RecurringSalesInvoices.Add(new RecurringSalesInvoice
        {
            OrganisationId = test.Organisation.Id,
            CustomerId = test.Customer.Id,
            Frequency = RecurringSalesInvoiceFrequency.Monthly,
            StartDate = new DateOnly(2026, 9, 1),
            NextInvoiceDate = new DateOnly(2026, 9, 1),
            DueDays = 7,
            Status = RecurringSalesInvoiceStatus.Active,
            IsActive = true,
            CreatedByUserId = test.UserId,
            Lines =
            [
                new RecurringSalesInvoiceLine
                {
                    Description = "Monthly support",
                    Quantity = 1m,
                    UnitPrice = 100m,
                    VatTreatment = VatTreatment.ZeroRated,
                    RevenueAccountId = test.Account("4000").Id
                }
            ]
        });
        test.Db.RecurringSupplierBills.Add(new RecurringSupplierBill
        {
            OrganisationId = test.Organisation.Id,
            SupplierId = test.Supplier.Id,
            SupplierReference = "MONTHLY-RENT",
            Frequency = RecurringSupplierBillFrequency.Monthly,
            StartDate = new DateOnly(2026, 9, 5),
            NextBillDate = new DateOnly(2026, 9, 5),
            DueDays = 0,
            Status = RecurringSupplierBillStatus.Active,
            IsActive = true,
            CreatedByUserId = test.UserId,
            Lines =
            [
                new RecurringSupplierBillLine
                {
                    Description = "Monthly service",
                    Quantity = 1m,
                    UnitPrice = 40m,
                    VatTreatment = VatTreatment.ZeroRated,
                    ExpenseAccountId = test.Account("6100").Id
                }
            ]
        });
        test.Db.PurchaseOrders.Add(new PurchaseOrder
        {
            OrganisationId = test.Organisation.Id,
            SupplierId = test.Supplier.Id,
            SequenceNumber = 1,
            PurchaseOrderNumber = "PO-CASHFLOW-001",
            OrderDate = asAt,
            ExpectedDate = new DateOnly(2026, 10, 10),
            Status = PurchaseOrderStatus.Approved,
            Total = 300m,
            CreatedByUserId = test.UserId
        });
        await test.Db.SaveChangesAsync();

        var forecast = await new CashflowForecastService(test.Db)
            .GetAsync(test.Organisation.Id, asAt);

        AssertPeriod(forecast.Next7Days, 50m, 20m);
        AssertPeriod(forecast.Next30Days, 150m, 60m);
        AssertPeriod(forecast.Next12Months, 1_150m, 760m);
        Assert.Equal(50m, forecast.Monthly[0].Sources.PostedReceipts);
        Assert.Equal(20m, forecast.Monthly[0].Sources.PostedPayments);
        Assert.Equal(100m, forecast.Monthly[1].Sources.RecurringReceipts);
        Assert.Equal(40m, forecast.Monthly[1].Sources.RecurringPayments);
        Assert.Equal(300m, forecast.Monthly[2].Sources.PlannedPurchasePayments);
    }

    [Fact]
    public async Task GetAsync_DoesNotDoubleCountPurchaseOrderLinkedToPostedBill()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var asAt = new DateOnly(2026, 8, 24);
        var journal = new PostedJournal
        {
            OrganisationId = test.Organisation.Id,
            SequenceNumber = 1,
            EntryDate = asAt,
            Reference = "NO-DOUBLE-COUNT",
            PostedAt = DateTimeOffset.UtcNow,
            PostedByUserId = test.UserId
        };
        test.Db.PostedJournals.Add(journal);
        var bill = Bill(test, journal.Id, 1, asAt.AddDays(10), 200m);
        test.Db.SupplierBills.Add(bill);
        test.Db.PurchaseOrders.Add(new PurchaseOrder
        {
            OrganisationId = test.Organisation.Id,
            SupplierId = test.Supplier.Id,
            SequenceNumber = 1,
            PurchaseOrderNumber = "PO-LINKED-001",
            OrderDate = asAt,
            ExpectedDate = asAt.AddDays(10),
            Status = PurchaseOrderStatus.Received,
            Total = 200m,
            SupplierBillId = bill.Id,
            CreatedByUserId = test.UserId
        });
        await test.Db.SaveChangesAsync();

        var forecast = await new CashflowForecastService(test.Db)
            .GetAsync(test.Organisation.Id, asAt);

        AssertPeriod(forecast.Next30Days, 0m, 200m);
        Assert.Equal(0m, forecast.Monthly[0].Sources.PlannedPurchasePayments);
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
