using FijiAccounts.Domain.Fiscalisation;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed class FiscalCreditNoteSubmissionFactory
{
    public FiscalInvoiceSubmission Create(
        SalesCreditNote creditNote,
        SalesInvoice originalInvoice,
        FiscalisationRecord originalFiscalRecord,
        IReadOnlyDictionary<VatTreatment, IReadOnlyCollection<string>> taxLabels,
        IReadOnlyCollection<FiscalPayment> payments,
        DateTimeOffset issuedAt,
        string? cashierId = null)
    {
        ArgumentNullException.ThrowIfNull(creditNote);
        ArgumentNullException.ThrowIfNull(originalInvoice);
        ArgumentNullException.ThrowIfNull(originalFiscalRecord);

        if (creditNote.SalesInvoiceId != originalInvoice.Id)
        {
            throw new InvalidOperationException(
                "The credit note must refer to the supplied original invoice.");
        }
        if (originalFiscalRecord.SalesInvoiceId != originalInvoice.Id ||
            originalFiscalRecord.Status != FiscalisationStatus.Accepted ||
            string.IsNullOrWhiteSpace(originalFiscalRecord.SdcInvoiceNumber) ||
            originalFiscalRecord.SdcIssuedAt is null)
        {
            throw new InvalidOperationException(
                "A fiscal refund requires the original invoice's accepted SDC response.");
        }
        if (creditNote.Total <= 0 || string.IsNullOrWhiteSpace(creditNote.CreditNoteNumber))
        {
            throw new InvalidOperationException(
                "The credit note must have a final number and positive total.");
        }

        var items = new List<FiscalInvoiceItem>();
        if (creditNote.Lines.Count > 0)
        {
            if (creditNote.Lines.Sum(x => x.GrossAmount) != creditNote.Total)
                throw new InvalidOperationException("Credit-note line allocations do not equal the refund total.");
            foreach (var line in creditNote.Lines)
            {
                if (!taxLabels.TryGetValue(line.VatTreatment, out var lineLabels) ||
                    lineLabels.Count == 0 || lineLabels.Any(string.IsNullOrWhiteSpace))
                    throw new InvalidOperationException($"No verified SDC tax label is configured for {line.VatTreatment} refunds.");
                items.Add(new FiscalInvoiceItem(line.Description, 1m, line.GrossAmount, line.GrossAmount, lineLabels));
            }
        }
        else
        {
            var treatments = originalInvoice.Lines
                .Where(x => x.TransactionGrossAmount > 0)
                .Select(x => x.VatTreatment)
                .Distinct()
                .ToList();
            if (treatments.Count != 1)
                throw new InvalidOperationException("A credit against mixed VAT treatments needs line-level allocation before fiscal refund preparation.");
            var treatment = treatments[0];
            if (!taxLabels.TryGetValue(treatment, out var labels) ||
                labels.Count == 0 || labels.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException($"No verified SDC tax label is configured for {treatment} refunds.");
            items.Add(new FiscalInvoiceItem($"Credit: {creditNote.Reason}", 1m, creditNote.Total, creditNote.Total, labels));
        }

        var submission = new FiscalInvoiceSubmission(
            creditNote.Id,
            creditNote.CreditNoteNumber,
            issuedAt,
            creditNote.Currency,
            FiscalInvoiceType.Normal,
            FiscalTransactionType.Refund,
            items,
            payments,
            cashierId,
            originalInvoice.RecipientTinSnapshot,
            ReferentDocumentNumber: originalFiscalRecord.SdcInvoiceNumber,
            ReferentDocumentIssuedAt: originalFiscalRecord.SdcIssuedAt);
        FiscalInvoiceSubmissionValidator.Validate(submission);
        return submission;
    }
}
