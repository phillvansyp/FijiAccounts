using FijiAccounts.Domain.Fiscalisation;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed class FiscalCreditNoteReversalSubmissionFactory
{
    public FiscalInvoiceSubmission Create(
        SalesCreditNoteReversal reversal,
        SalesCreditNote credit,
        FiscalisationRecord acceptedRefund,
        IReadOnlyDictionary<VatTreatment, IReadOnlyCollection<string>> taxLabels,
        FiscalPaymentType paymentType,
        string? cashierId = null)
    {
        if (reversal.SalesCreditNoteId != credit.Id)
            throw new InvalidOperationException("The reversal must belong to the supplied sales credit note.");
        if (acceptedRefund.SalesCreditNoteId != credit.Id ||
            acceptedRefund.Status != FiscalisationStatus.Accepted ||
            string.IsNullOrWhiteSpace(acceptedRefund.SdcInvoiceNumber) ||
            acceptedRefund.SdcIssuedAt is null)
            throw new InvalidOperationException("A fiscal reversal requires the credit note's accepted SDC refund response.");
        if (credit.Lines.Count == 0 || credit.Lines.Sum(x => x.GrossAmount) != credit.Total)
            throw new InvalidOperationException("A fiscal reversal requires the credit note's original line allocation.");

        var items = credit.Lines.Select(line =>
        {
            if (!taxLabels.TryGetValue(line.VatTreatment, out var labels) ||
                labels.Count == 0 || labels.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException($"No verified SDC tax label is configured for {line.VatTreatment} reversals.");
            return new FiscalInvoiceItem($"Reverse credit: {line.Description}", 1m, line.GrossAmount, line.GrossAmount, labels);
        }).ToList();
        var issuedAt = new DateTimeOffset(
            reversal.ReversalDate.ToDateTime(TimeOnly.MinValue),
            TimeSpan.Zero);
        var submission = new FiscalInvoiceSubmission(
            reversal.Id,
            $"REV-{credit.CreditNoteNumber}",
            issuedAt,
            credit.Currency,
            FiscalInvoiceType.Normal,
            FiscalTransactionType.Sale,
            items,
            [new FiscalPayment(credit.Total, paymentType)],
            cashierId,
            credit.SalesInvoice.RecipientTinSnapshot,
            ReferentDocumentNumber: acceptedRefund.SdcInvoiceNumber,
            ReferentDocumentIssuedAt: acceptedRefund.SdcIssuedAt);
        FiscalInvoiceSubmissionValidator.Validate(submission);
        return submission;
    }
}
