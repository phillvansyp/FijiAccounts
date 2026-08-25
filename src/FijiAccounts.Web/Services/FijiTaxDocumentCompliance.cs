using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record FijiTaxDocumentClassification(
    bool IsTaxInvoice,
    bool IsSimplified,
    string ComplianceVersion);

public static class FijiTaxDocumentCompliance
{
    public const decimal MandatoryIssueThreshold = 10m;
    public const decimal SimplifiedInvoiceThreshold = 100m;
    public const string CurrentComplianceVersion = "FJ-VAT-REGS-2024-08-01";

    public static FijiTaxDocumentClassification ClassifyAndValidate(
        Organisation organisation,
        BusinessParty recipient,
        DateOnly issueDate,
        decimal total,
        IReadOnlyCollection<SalesInvoiceLine> lines)
    {
        if (!string.Equals(organisation.CountryCode, "FJ", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, false, $"{organisation.CountryCode.ToUpperInvariant()}-COMMERCIAL");
        }

        var hasTaxableSupply = lines.Any(x =>
            x.VatTreatment is VatTreatment.Standard or VatTreatment.ZeroRated);
        if (!hasTaxableSupply)
        {
            return new(false, false, CurrentComplianceVersion);
        }

        var registrationActive = organisation.IsVatRegistered &&
            organisation.VatRegistrationDate is not null &&
            organisation.VatRegistrationDate <= issueDate;
        if (!registrationActive)
        {
            throw new InvalidOperationException(
                "This invoice contains a taxable supply, but the organisation's Fiji VAT registration is not active on the issue date.");
        }

        Require(organisation.Tin, "Set the organisation TIN before issuing a Fiji tax invoice.");
        Require(organisation.BusinessAddress,
            "Set the organisation business address before issuing a Fiji tax invoice.");

        var simplified = total <= SimplifiedInvoiceThreshold;
        if (!simplified)
        {
            Require(recipient.Address,
                "Set the customer's address before issuing a full Fiji tax invoice.");
        }

        return new(true, simplified, CurrentComplianceVersion);
    }

    public static void ApplySnapshot(
        SalesInvoice invoice,
        Organisation organisation,
        BusinessParty recipient)
    {
        var classification = ClassifyAndValidate(
            organisation,
            recipient,
            invoice.IssueDate,
            invoice.Total,
            invoice.Lines);
        invoice.IsTaxInvoice = classification.IsTaxInvoice;
        invoice.IsSimplifiedTaxInvoice = classification.IsSimplified;
        invoice.TaxDocumentComplianceVersion = classification.ComplianceVersion;
        invoice.SupplierNameSnapshot = Clean(organisation.LegalName);
        invoice.SupplierAddressSnapshot = Clean(organisation.BusinessAddress);
        invoice.SupplierTinSnapshot = Clean(organisation.Tin);
        invoice.RecipientNameSnapshot = Clean(recipient.Name);
        invoice.RecipientAddressSnapshot = Clean(recipient.Address);
        invoice.RecipientTinSnapshot = Clean(recipient.Tin);
    }

    public static string TaxLabel(SalesInvoiceLine line) => line.VatTreatment switch
    {
        VatTreatment.Standard => $"VAT {line.VatRate:P1}",
        VatTreatment.ZeroRated => "Zero-rated 0%",
        VatTreatment.Exempt => "Exempt",
        _ => "Out of scope"
    };

    private static void Require(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(message);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
