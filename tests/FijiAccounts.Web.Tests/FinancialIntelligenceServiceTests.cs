using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class FinancialIntelligenceServiceTests
{
    private static readonly DateOnly AsAt = new(2026, 8, 24);

    [Fact]
    public async Task GetAsync_DetectsMarginCostAndConcentrationRisks()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var secondCustomer = Party(test, "Second Customer", PartyType.Customer);
        var secondSupplier = Party(test, "Second Supplier", PartyType.Supplier);
        test.Db.BusinessParties.AddRange(secondCustomer, secondSupplier);
        AddMarginJournal(test, AsAt.AddDays(-35), 1_000m, 200m, 1);
        AddMarginJournal(test, AsAt.AddDays(-5), 1_000m, 400m, 2);
        var journal = AddMarginJournal(test, AsAt, 0m, 0m, 3);
        AddSupplierBill(test, journal.Id, test.Supplier, AsAt.AddDays(-35), 1, 10m, 100m);
        AddSupplierBill(test, journal.Id, test.Supplier, AsAt.AddDays(-5), 2, 10m, 150m);
        AddSupplierBill(test, journal.Id, secondSupplier, AsAt.AddDays(-5), 3, 1m, 100m);
        AddInvoice(test, test.Customer, 1, 800m);
        AddInvoice(test, secondCustomer, 2, 200m);
        await test.Db.SaveChangesAsync();

        var summary = await new FinancialIntelligenceService(test.Db, test.Access)
            .GetAsync(test.UserId, test.Organisation.Id, AsAt);

        Assert.Equal(1_000m, summary.CurrentRevenue);
        Assert.Equal(600m, summary.CurrentGrossProfit);
        Assert.Equal(60m, summary.CurrentGrossMarginPercent);
        Assert.Equal(80m, summary.PriorGrossMarginPercent);
        Assert.Contains(summary.Insights, x =>
            x.Category == FinancialInsightCategory.GrossMargin &&
            x.Severity == FinancialInsightSeverity.High);
        Assert.Contains(summary.Insights, x =>
            x.Category == FinancialInsightCategory.SupplierCost &&
            x.Explanation.Contains("50.0%"));
        Assert.Contains(summary.Insights, x =>
            x.Category == FinancialInsightCategory.CustomerConcentration &&
            x.Explanation.Contains("80.0%"));
    }

    [Fact]
    public async Task GetAsync_NoActivity_ReturnsEmptyExplainableSummary()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();

        var summary = await new FinancialIntelligenceService(test.Db, test.Access)
            .GetAsync(test.UserId, test.Organisation.Id, AsAt);

        Assert.Equal(0m, summary.CurrentRevenue);
        Assert.Null(summary.CurrentGrossMarginPercent);
        Assert.Empty(summary.Insights);
    }

    [Fact]
    public async Task GetAsync_RejectsCrossTenantAccess()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var other = new Organisation
        {
            LegalName = "Other Limited",
            CountryCode = "FJ",
            BaseCurrency = "FJD",
            Kind = OrganisationKind.Business
        };
        test.Db.Organisations.Add(other);
        await test.Db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new FinancialIntelligenceService(test.Db, test.Access)
                .GetAsync(test.UserId, other.Id, AsAt));
    }

    private static PostedJournal AddMarginJournal(
        AccountingTestDatabase test,
        DateOnly date,
        decimal revenue,
        decimal cost,
        long sequence)
    {
        var journal = new PostedJournal
        {
            OrganisationId = test.Organisation.Id,
            SequenceNumber = sequence,
            EntryDate = date,
            Reference = $"INTEL-{sequence}",
            PostedAt = DateTimeOffset.UtcNow,
            PostedByUserId = test.UserId,
            Lines =
            [
                new PostedJournalLine
                {
                    LedgerAccountId = test.Account("4000").Id,
                    Description = "Revenue",
                    Credit = revenue
                },
                new PostedJournalLine
                {
                    LedgerAccountId = test.Account("5000").Id,
                    Description = "Cost of sales",
                    Debit = cost
                }
            ]
        };
        test.Db.PostedJournals.Add(journal);
        return journal;
    }

    private static void AddSupplierBill(
        AccountingTestDatabase test,
        Guid journalId,
        BusinessParty supplier,
        DateOnly date,
        long sequence,
        decimal quantity,
        decimal unitPrice)
    {
        var net = quantity * unitPrice;
        test.Db.SupplierBills.Add(new SupplierBill
        {
            OrganisationId = test.Organisation.Id,
            SupplierId = supplier.Id,
            SequenceNumber = sequence,
            BillNumber = $"INTEL-BILL-{sequence}",
            SupplierReference = $"SUP-{sequence}",
            BillDate = date,
            DueDate = date.AddDays(30),
            Status = BillStatus.Posted,
            Subtotal = net,
            Total = net,
            PostedJournalId = journalId,
            CreatedByUserId = test.UserId,
            Lines =
            [
                new SupplierBillLine
                {
                    Description = "Diesel fuel",
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                    NetAmount = net,
                    GrossAmount = net,
                    ExpenseAccountId = test.Account("5000").Id
                }
            ]
        });
    }

    private static void AddInvoice(
        AccountingTestDatabase test,
        BusinessParty customer,
        long sequence,
        decimal total) =>
        test.Db.SalesInvoices.Add(new SalesInvoice
        {
            OrganisationId = test.Organisation.Id,
            CustomerId = customer.Id,
            SequenceNumber = sequence,
            InvoiceNumber = $"INTEL-INV-{sequence}",
            IssueDate = AsAt,
            DueDate = AsAt.AddDays(30),
            Status = InvoiceStatus.Posted,
            Subtotal = total,
            Total = total,
            CreatedByUserId = test.UserId
        });

    private static BusinessParty Party(
        AccountingTestDatabase test,
        string name,
        PartyType type) => new()
        {
            OrganisationId = test.Organisation.Id,
            Name = name,
            Type = type,
            IsActive = true
        };
}
