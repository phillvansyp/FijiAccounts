using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Components.Pages;

public partial class SalesInvoiceDetail
{
    private bool showCredit;
    private DateOnly creditDate = DateOnly.FromDateTime(DateTime.Today);
    private string creditReason = string.Empty;
    private decimal creditAmount;
    private decimal? creditVatAmount;
    private bool restockTrackedItems;
    private decimal AvailableCredit => invoice is null ? 0 : invoice.Total - invoice.AmountPaid - invoice.AmountCredited;
    private bool IsTaxDocument => invoice?.IsTaxInvoice
        ?? invoice?.Lines.Any(x => x.VatTreatment is VatTreatment.Standard or VatTreatment.ZeroRated) == true;
    private string DocumentTitle => invoice?.Status == Data.InvoiceStatus.Draft
        ? "Draft Invoice"
        : IsTaxDocument ? "Tax Invoice" : "Commercial Invoice";
    private string SupplierName => invoice?.SupplierNameSnapshot ?? access?.Organisation.LegalName ?? string.Empty;
    private string? SupplierAddress => invoice?.SupplierAddressSnapshot ?? access?.Organisation.BusinessAddress;
    private string? SupplierTin => invoice?.SupplierTinSnapshot ?? access?.Organisation.Tin;
    private string RecipientName => invoice?.RecipientNameSnapshot ?? invoice?.Customer.Name ?? string.Empty;
    private string? RecipientAddress => invoice?.RecipientAddressSnapshot ?? invoice?.Customer.Address;
    private string? RecipientTin => invoice?.RecipientTinSnapshot ?? invoice?.Customer.Tin;
    private bool HasMixedVatRates => invoice?.Lines
        .Where(x => x.GrossAmount > 0m)
        .Select(x => x.VatRate)
        .Distinct()
        .Skip(1)
        .Any() == true;
    private IEnumerable<(string Label, decimal Amount)> SupplyTotals => invoice?.Lines
        .Where(x => x.VatTreatment != VatTreatment.Standard)
        .GroupBy(x => x.VatTreatment)
        .Select(group => ($"{FijiTaxDocumentCompliance.TaxLabel(group.First())} supplies", group.Sum(x => x.TransactionNetAmount + x.TransactionVatAmount)))
        ?? [];

    private void OpenCredit()
    {
        showCredit = true;
        showVoid = false;
        creditAmount = AvailableCredit;
        creditVatAmount = null;
    }

    private async Task CreateCredit()
    {
        try
        {
            await CreditNoteService.CreateAsync(userId, new(
                OrganisationId,
                InvoiceId,
                creditDate,
                creditReason,
                creditAmount,
                restockTrackedItems,
                creditVatAmount));
            showCredit = false;
            await Reload();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            status = ex.Message;
        }
    }
}
