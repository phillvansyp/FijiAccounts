using FijiAccounts.Domain.Fiscalisation;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed class FiscalSalesInvoiceVoidSubmissionFactory
{
    public FiscalInvoiceSubmission Create(
        SalesInvoiceVoid invoiceVoid,
        SalesInvoice invoice,
        FiscalisationRecord acceptedInvoice,
        IReadOnlyDictionary<VatTreatment, IReadOnlyCollection<string>> taxLabels,
        FiscalPaymentType paymentType,
        string? cashierId = null)
    {
        if (invoiceVoid.SalesInvoiceId != invoice.Id)
            throw new InvalidOperationException("The void must belong to the supplied sales invoice.");
        if (acceptedInvoice.SalesInvoiceId != invoice.Id ||
            acceptedInvoice.Status != FiscalisationStatus.Accepted ||
            string.IsNullOrWhiteSpace(acceptedInvoice.SdcInvoiceNumber) ||
            acceptedInvoice.SdcIssuedAt is null)
            throw new InvalidOperationException("A fiscal invoice void requires the invoice's accepted SDC response.");
        if (invoice.Lines.Count == 0 || invoice.Lines.Sum(x => x.TransactionGrossAmount) != invoice.TransactionTotal)
            throw new InvalidOperationException("A fiscal invoice void requires the original invoice line allocation.");

        var items = invoice.Lines.Select(line =>
        {
            if (!taxLabels.TryGetValue(line.VatTreatment, out var labels) ||
                labels.Count == 0 || labels.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException($"No verified SDC tax label is configured for {line.VatTreatment} invoice voids.");
            return new FiscalInvoiceItem(
                $"Void: {line.Description}", line.Quantity, line.TransactionGrossAmount / line.Quantity,
                line.TransactionGrossAmount, labels);
        }).ToList();
        var issuedAt = new DateTimeOffset(invoiceVoid.VoidDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var submission = new FiscalInvoiceSubmission(
            invoiceVoid.Id,
            $"VOID-{invoice.InvoiceNumber}",
            issuedAt,
            invoice.Currency,
            FiscalInvoiceType.Normal,
            FiscalTransactionType.Refund,
            items,
            [new FiscalPayment(invoice.TransactionTotal, paymentType)],
            cashierId,
            invoice.RecipientTinSnapshot,
            ReferentDocumentNumber: acceptedInvoice.SdcInvoiceNumber,
            ReferentDocumentIssuedAt: acceptedInvoice.SdcIssuedAt);
        FiscalInvoiceSubmissionValidator.Validate(submission);
        return submission;
    }
}
