namespace FijiAccounts.Domain.Fiscalisation;

public enum FiscalInvoiceType
{
    Normal = 0,
    Proforma = 1,
    Copy = 2,
    Training = 3,
    Advance = 4
}

public enum FiscalTransactionType
{
    Sale = 0,
    Refund = 1
}

public enum FiscalPaymentType
{
    Other = 0,
    Cash = 1,
    Card = 2,
    Check = 3,
    WireTransfer = 4,
    Voucher = 5,
    MobileMoney = 6
}

public sealed record FiscalInvoiceItem(
    string Name,
    decimal Quantity,
    decimal UnitPrice,
    decimal TotalAmount,
    IReadOnlyCollection<string> TaxLabels,
    string? Gtin = null);

public sealed record FiscalPayment(decimal Amount, FiscalPaymentType Type);

public sealed record FiscalInvoiceSubmission(
    Guid SourceDocumentId,
    string SourceDocumentNumber,
    DateTimeOffset IssuedAt,
    string Currency,
    FiscalInvoiceType InvoiceType,
    FiscalTransactionType TransactionType,
    IReadOnlyCollection<FiscalInvoiceItem> Items,
    IReadOnlyCollection<FiscalPayment> Payments,
    string? CashierId = null,
    string? BuyerId = null,
    string? BuyerCostCentreId = null,
    string? ReferentDocumentNumber = null,
    DateTimeOffset? ReferentDocumentIssuedAt = null);

public enum FiscalisationOutcome
{
    Accepted,
    Rejected,
    Unknown
}

public sealed record FiscalisationResult(
    FiscalisationOutcome Outcome,
    string? SdcInvoiceNumber = null,
    DateTimeOffset? SdcIssuedAt = null,
    string? VerificationUrl = null,
    string? VerificationQrCode = null,
    string? SignedPayload = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public interface IFiscalisationGateway
{
    Task<FiscalisationResult> FiscaliseAsync(
        FiscalInvoiceSubmission submission,
        CancellationToken cancellationToken = default);

    Task<FiscalisationResult> RecoverLastResultAsync(
        CancellationToken cancellationToken = default);
}

public sealed class FiscalisationValidationException(string message)
    : Exception(message);

public static class FiscalInvoiceSubmissionValidator
{
    public static void Validate(FiscalInvoiceSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        Require(submission.SourceDocumentId != Guid.Empty,
            "A source document identifier is required.");
        Require(!string.IsNullOrWhiteSpace(submission.SourceDocumentNumber),
            "A source document number is required.");
        Require(submission.Currency.Length == 3 &&
                submission.Currency.All(char.IsAsciiLetterUpper),
            "Currency must be a three-letter uppercase code.");
        Require(submission.Items.Count > 0,
            "At least one fiscal invoice item is required.");
        Require(submission.Payments.Count > 0,
            "At least one payment is required.");

        foreach (var item in submission.Items)
        {
            Require(!string.IsNullOrWhiteSpace(item.Name),
                "Every fiscal invoice item requires a name.");
            Require(item.Quantity > 0,
                "Fiscal invoice item quantities must be positive.");
            Require(item.UnitPrice >= 0 && item.TotalAmount >= 0,
                "Fiscal invoice item amounts cannot be negative.");
            Require(item.TaxLabels.Count > 0 &&
                    item.TaxLabels.All(label => !string.IsNullOrWhiteSpace(label)),
                "Every fiscal invoice item requires at least one SDC tax label.");

            var calculatedTotal = decimal.Round(
                item.Quantity * item.UnitPrice,
                2,
                MidpointRounding.AwayFromZero);
            Require(calculatedTotal == item.TotalAmount,
                $"The total for '{item.Name}' must equal quantity multiplied by unit price after two-decimal half-up rounding.");
        }

        Require(submission.Payments.All(payment => payment.Amount > 0),
            "Fiscal invoice payment amounts must be positive.");

        var itemTotal = submission.Items.Sum(item => item.TotalAmount);
        var paymentTotal = submission.Payments.Sum(payment => payment.Amount);
        Require(itemTotal == paymentTotal,
            "Fiscal invoice payments must equal the invoice item total.");

        Require(
            submission.ReferentDocumentNumber is not null ||
            submission.ReferentDocumentIssuedAt is null,
            "A referent document time requires a referent document number.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new FiscalisationValidationException(message);
        }
    }
}
