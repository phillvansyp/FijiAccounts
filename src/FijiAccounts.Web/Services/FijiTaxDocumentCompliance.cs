using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record TaxDocumentClassification(
    bool IsTaxInvoice,
    bool IsSimplified,
    string ComplianceVersion);

public static class TaxDocumentCompliance
{
    public const decimal MandatoryIssueThreshold = 10m;
    public const decimal SimplifiedInvoiceThreshold = 100m;
    public const string CurrentComplianceVersion = "FJ-VAT-REGS-2024-08-01";
    public const decimal NewZealandSellerGstNumberThreshold = 200m;
    public const decimal NewZealandBuyerDetailsThreshold = 1_000m;
    public const string NewZealandComplianceVersion = "NZ-GST-TSI-2023-04-01";

    public static TaxDocumentClassification ClassifyAndValidate(
        Organisation organisation,
        BusinessParty recipient,
        DateOnly issueDate,
        decimal total,
        IReadOnlyCollection<SalesInvoiceLine> lines)
    {
        if (string.Equals(organisation.CountryCode, "NZ", StringComparison.OrdinalIgnoreCase))
        {
            return ClassifyAndValidateNewZealand(
                organisation,
                recipient,
                issueDate,
                total,
                lines);
        }

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

    private static TaxDocumentClassification ClassifyAndValidateNewZealand(
        Organisation organisation,
        BusinessParty recipient,
        DateOnly issueDate,
        decimal total,
        IReadOnlyCollection<SalesInvoiceLine> lines)
    {
        var hasTaxableSupply = lines.Any(x =>
            x.VatTreatment is VatTreatment.Standard or VatTreatment.ZeroRated);
        if (!hasTaxableSupply)
        {
            return new(false, false, NewZealandComplianceVersion);
        }

        var registrationActive = organisation.IsVatRegistered &&
            organisation.VatRegistrationDate is not null &&
            organisation.VatRegistrationDate <= issueDate;
        if (!registrationActive)
        {
            throw new InvalidOperationException(
                "This invoice contains a taxable supply, but the organisation's New Zealand GST registration is not active on the supply date.");
        }

        if (total > NewZealandSellerGstNumberThreshold)
        {
            Require(
                organisation.Tin,
                "Set the organisation's New Zealand GST number before issuing taxable supply information over NZD 200.");
        }

        if (total > NewZealandBuyerDetailsThreshold)
        {
            Require(recipient.Name, "Set the buyer's name for taxable supply information over NZD 1,000.");
            if (string.IsNullOrWhiteSpace(recipient.Address) &&
                string.IsNullOrWhiteSpace(recipient.Email) &&
                string.IsNullOrWhiteSpace(recipient.Phone))
            {
                throw new InvalidOperationException(
                    "Set the buyer's address, email or phone for taxable supply information over NZD 1,000.");
            }
        }

        return new(true, total <= NewZealandBuyerDetailsThreshold, NewZealandComplianceVersion);
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

    public static string TaxLabel(SalesInvoiceLine line, string taxLabel = "Tax") => line.VatTreatment switch
    {
        VatTreatment.Standard => $"{taxLabel} {line.VatRate:P1}",
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

public static class FijiTaxDocumentCompliance
{
    public const decimal MandatoryIssueThreshold = TaxDocumentCompliance.MandatoryIssueThreshold;
    public const decimal SimplifiedInvoiceThreshold = TaxDocumentCompliance.SimplifiedInvoiceThreshold;
    public const string CurrentComplianceVersion = TaxDocumentCompliance.CurrentComplianceVersion;

    public static TaxDocumentClassification ClassifyAndValidate(
        Organisation organisation,
        BusinessParty recipient,
        DateOnly issueDate,
        decimal total,
        IReadOnlyCollection<SalesInvoiceLine> lines) =>
        TaxDocumentCompliance.ClassifyAndValidate(
            organisation,
            recipient,
            issueDate,
            total,
            lines);

    public static void ApplySnapshot(
        SalesInvoice invoice,
        Organisation organisation,
        BusinessParty recipient) =>
        TaxDocumentCompliance.ApplySnapshot(invoice, organisation, recipient);

    public static string TaxLabel(SalesInvoiceLine line) =>
        TaxDocumentCompliance.TaxLabel(
            line,
            line.SalesInvoice?.TaxDocumentComplianceVersion?.StartsWith(
                "NZ-",
                StringComparison.OrdinalIgnoreCase) == true
                ? "GST"
                : "VAT");
}
