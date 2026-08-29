using FijiAccounts.Domain.Fiscalisation;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed class FiscalisationSubmissionFactory
{
    public FiscalInvoiceSubmission Create(
        SalesInvoice invoice,
        IReadOnlyDictionary<VatTreatment, IReadOnlyCollection<string>> taxLabels,
        IReadOnlyCollection<FiscalPayment> payments,
        DateTimeOffset issuedAt,
        string? cashierId = null)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(taxLabels);
        ArgumentNullException.ThrowIfNull(payments);

        if (invoice.Id == Guid.Empty || invoice.Lines.Count == 0)
        {
            throw new InvalidOperationException(
                "The source invoice must be saved and contain at least one line.");
        }

        if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ||
            invoice.InvoiceNumber.StartsWith("DRAFT-", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Fiscal preparation requires the invoice's final document number.");
        }

        var items = invoice.Lines.Select(line =>
        {
            if (!taxLabels.TryGetValue(line.VatTreatment, out var labels) ||
                labels.Count == 0 || labels.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException(
                    $"No verified SDC tax label is configured for {line.VatTreatment} sales.");
            }

            if (line.Quantity <= 0 || line.TransactionGrossAmount < 0)
            {
                throw new InvalidOperationException(
                    "Fiscal invoice lines require a positive quantity and non-negative transaction total.");
            }

            return new FiscalInvoiceItem(
                line.Description,
                line.Quantity,
                line.TransactionGrossAmount / line.Quantity,
                line.TransactionGrossAmount,
                labels);
        }).ToList();

        var submission = new FiscalInvoiceSubmission(
            invoice.Id,
            invoice.InvoiceNumber,
            issuedAt,
            invoice.Currency,
            FiscalInvoiceType.Normal,
            FiscalTransactionType.Sale,
            items,
            payments,
            cashierId,
            invoice.RecipientTinSnapshot);
        FiscalInvoiceSubmissionValidator.Validate(submission);
        return submission;
    }
}
