using FijiAccounts.Domain.Fiscalisation;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class FiscalisationSubmissionFactoryTests
{
    [Fact]
    public void CreateMapsInvoiceLinesAndSplitPaymentsWithoutInventingLabels()
    {
        var invoice = Invoice();
        invoice.Lines =
        [
            Line("Standard service", 2m, 112.50m, VatTreatment.Standard),
            Line("Export service", 1m, 40m, VatTreatment.ZeroRated)
        ];
        invoice.TransactionTotal = 152.50m;
        var labels = new Dictionary<VatTreatment, IReadOnlyCollection<string>>
        {
            [VatTreatment.Standard] = ["VERIFIED-STANDARD"],
            [VatTreatment.ZeroRated] = ["VERIFIED-ZERO"]
        };

        var result = new FiscalisationSubmissionFactory().Create(
            invoice,
            labels,
            [
                new FiscalPayment(100m, FiscalPaymentType.Card),
                new FiscalPayment(52.50m, FiscalPaymentType.MobileMoney)
            ],
            new DateTimeOffset(2026, 8, 29, 9, 30, 0, TimeSpan.FromHours(12)),
            "cashier-1");

        Assert.Equal("INV-000123", result.SourceDocumentNumber);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(56.25m, result.Items.First().UnitPrice);
        Assert.Equal(["VERIFIED-STANDARD"], result.Items.First().TaxLabels);
        Assert.Equal(2, result.Payments.Count);
        Assert.Equal("123456789", result.BuyerId);
    }

    [Fact]
    public void CreateBlocksMissingVerifiedTaxLabelsAndDraftNumbers()
    {
        var invoice = Invoice();
        invoice.Lines = [Line("Service", 1m, 112.50m, VatTreatment.Standard)];

        var missingLabel = Assert.Throws<InvalidOperationException>(() =>
            new FiscalisationSubmissionFactory().Create(
                invoice,
                new Dictionary<VatTreatment, IReadOnlyCollection<string>>(),
                [new FiscalPayment(112.50m, FiscalPaymentType.Other)],
                DateTimeOffset.UtcNow));
        Assert.Contains("No verified SDC tax label", missingLabel.Message);

        invoice.InvoiceNumber = "DRAFT-000123";
        var draftNumber = Assert.Throws<InvalidOperationException>(() =>
            new FiscalisationSubmissionFactory().Create(
                invoice,
                new Dictionary<VatTreatment, IReadOnlyCollection<string>>
                {
                    [VatTreatment.Standard] = ["VERIFIED-STANDARD"]
                },
                [new FiscalPayment(112.50m, FiscalPaymentType.Other)],
                DateTimeOffset.UtcNow));
        Assert.Contains("final document number", draftNumber.Message);
    }

    private static SalesInvoice Invoice() => new()
    {
        OrganisationId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        SequenceNumber = 123,
        InvoiceNumber = "INV-000123",
        IssueDate = new DateOnly(2026, 8, 29),
        DueDate = new DateOnly(2026, 9, 28),
        Currency = "FJD",
        TransactionTotal = 112.50m,
        Total = 112.50m,
        RecipientTinSnapshot = "123456789",
        CreatedByUserId = "user-1"
    };

    private static SalesInvoiceLine Line(
        string description,
        decimal quantity,
        decimal total,
        VatTreatment treatment) => new()
    {
        Description = description,
        Quantity = quantity,
        TransactionUnitPrice = total / quantity,
        TransactionGrossAmount = total,
        VatTreatment = treatment
    };
}
