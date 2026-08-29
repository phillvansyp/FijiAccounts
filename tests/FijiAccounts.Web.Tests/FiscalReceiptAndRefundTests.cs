using FijiAccounts.Domain.Fiscalisation;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class FiscalReceiptAndRefundTests
{
    [Fact]
    public void ReceiptPresenterSeparatesSimulatorAndAllowsOnlySafeImageData()
    {
        var simulated = AcceptedRecord(
            "SIMULATED-123",
            "https://example.invalid/verify",
            "SIMULATED-QR:123");
        var simulatorView = FiscalReceiptPresenter.Create(simulated);

        Assert.True(simulatorView.IsSimulated);
        Assert.Null(simulatorView.VerificationUrl);
        Assert.Null(simulatorView.QrImageSource);

        var pngData = "data:image/png;base64,iVBORw0KGgo=";
        var realView = FiscalReceiptPresenter.Create(AcceptedRecord(
            "SDC-123",
            "https://verify.example/SDC-123",
            pngData));
        Assert.False(realView.IsSimulated);
        Assert.Equal("https://verify.example/SDC-123", realView.VerificationUrl);
        Assert.Equal(pngData, realView.QrImageSource);

        var unsafeView = FiscalReceiptPresenter.Create(AcceptedRecord(
            "SDC-124",
            "javascript:alert(1)",
            "data:image/svg+xml;base64,PHN2Zz4="));
        Assert.Null(unsafeView.VerificationUrl);
        Assert.Null(unsafeView.QrImageSource);
    }

    [Fact]
    public void CreditFactoryBuildsRefundAgainstOriginalAcceptedSdcInvoice()
    {
        var invoice = Invoice(VatTreatment.Standard);
        var credit = Credit(invoice, 56.25m);
        var originalFiscal = AcceptedRecord("SDC-ORIGINAL", null, null);
        originalFiscal.SalesInvoiceId = invoice.Id;
        var labels = new Dictionary<VatTreatment, IReadOnlyCollection<string>>
        {
            [VatTreatment.Standard] = ["VERIFIED-STANDARD"]
        };

        var result = new FiscalCreditNoteSubmissionFactory().Create(
            credit,
            invoice,
            originalFiscal,
            labels,
            [new FiscalPayment(56.25m, FiscalPaymentType.Card)],
            DateTimeOffset.UtcNow,
            "cashier-1");

        Assert.Equal(FiscalTransactionType.Refund, result.TransactionType);
        Assert.Equal("CN-000001", result.SourceDocumentNumber);
        Assert.Equal("SDC-ORIGINAL", result.ReferentDocumentNumber);
        Assert.Equal(originalFiscal.SdcIssuedAt, result.ReferentDocumentIssuedAt);
        Assert.Equal(56.25m, Assert.Single(result.Items).TotalAmount);
    }

    [Fact]
    public void CreditFactoryBlocksMixedVatWithoutLineAllocation()
    {
        var invoice = Invoice(VatTreatment.Standard);
        invoice.Lines.Add(Line(VatTreatment.ZeroRated, 20m));
        var credit = Credit(invoice, 20m);
        var originalFiscal = AcceptedRecord("SDC-ORIGINAL", null, null);
        originalFiscal.SalesInvoiceId = invoice.Id;

        var error = Assert.Throws<InvalidOperationException>(() =>
            new FiscalCreditNoteSubmissionFactory().Create(
                credit,
                invoice,
                originalFiscal,
                new Dictionary<VatTreatment, IReadOnlyCollection<string>>
                {
                    [VatTreatment.Standard] = ["STANDARD"],
                    [VatTreatment.ZeroRated] = ["ZERO"]
                },
                [new FiscalPayment(20m, FiscalPaymentType.Other)],
                DateTimeOffset.UtcNow));

        Assert.Contains("mixed VAT treatments", error.Message);
    }

    private static FiscalisationRecord AcceptedRecord(
        string number,
        string? url,
        string? qr) => new()
        {
            OrganisationId = Guid.NewGuid(),
            SalesInvoiceId = Guid.NewGuid(),
            Status = FiscalisationStatus.Accepted,
            RequestHash = new string('A', 64),
            RequestJson = "{}",
            CreatedByUserId = "user-1",
            SdcInvoiceNumber = number,
            SdcIssuedAt = DateTimeOffset.UtcNow,
            VerificationUrl = url,
            VerificationQrCode = qr,
            SignedPayload = "signed"
        };

    private static SalesInvoice Invoice(VatTreatment treatment)
    {
        var invoice = new SalesInvoice
        {
            OrganisationId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            SequenceNumber = 1,
            InvoiceNumber = "INV-000001",
            IssueDate = new DateOnly(2026, 8, 29),
            DueDate = new DateOnly(2026, 9, 28),
            Currency = "FJD",
            TransactionTotal = 112.50m,
            Total = 112.50m,
            RecipientTinSnapshot = "123456789",
            Status = InvoiceStatus.Posted,
            CreatedByUserId = "user-1"
        };
        invoice.Lines = [Line(treatment, 112.50m)];
        return invoice;
    }

    private static SalesInvoiceLine Line(VatTreatment treatment, decimal total) => new()
    {
        Description = "Service",
        Quantity = 1m,
        TransactionGrossAmount = total,
        VatTreatment = treatment
    };

    private static SalesCreditNote Credit(SalesInvoice invoice, decimal total) => new()
    {
        OrganisationId = invoice.OrganisationId,
        SalesInvoiceId = invoice.Id,
        CreditNoteNumber = "CN-000001",
        CreditDate = new DateOnly(2026, 8, 30),
        Reason = "Price adjustment",
        Currency = invoice.Currency,
        Total = total,
        PostedJournalId = Guid.NewGuid(),
        CreatedByUserId = "user-1"
    };
}
