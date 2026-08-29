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
    private readonly Dictionary<Guid, decimal> creditLineAmounts = [];
    private decimal AvailableCredit => invoice is null ? 0 : invoice.Total - invoice.AmountPaid - invoice.AmountCredited;
    private decimal AllocatedCreditTotal => creditLineAmounts.Values.Sum();
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
        .Select(group => ($"{TaxDocumentCompliance.TaxLabel(group.First(), access?.Organisation.TaxLabel ?? "Tax")} supplies", group.Sum(x => x.TransactionNetAmount + x.TransactionVatAmount)))
        ?? [];

    private void OpenCredit()
    {
        showCredit = true;
        showVoid = false;
        creditAmount = AvailableCredit;
        creditVatAmount = null;
        creditLineAmounts.Clear();
        if (invoice is not null && fiscalisationEnabled && invoice.TransactionTotal > 0m)
        {
            var remaining = AvailableCredit;
            var allocated = 0m;
            for (var index = 0; index < invoice.Lines.Count; index++)
            {
                var line = invoice.Lines[index];
                var amount = index == invoice.Lines.Count - 1
                    ? remaining - allocated
                    : decimal.Round(remaining * line.TransactionGrossAmount / invoice.TransactionTotal, 2, MidpointRounding.AwayFromZero);
                amount = Math.Max(0m, Math.Min(line.TransactionGrossAmount, amount));
                creditLineAmounts[line.Id] = amount;
                allocated += amount;
            }
        }
    }

    private async Task CreateCredit()
    {
        try
        {
            if (fiscalisationEnabled)
            {
                var draft = await FiscalisedCreditPosting.CreateDraftAsync(userId, new(
                    OrganisationId,
                    InvoiceId,
                    creditDate,
                    creditReason,
                    creditLineAmounts.Where(x => x.Value > 0m).Select(x => new SalesCreditNoteAllocation(x.Key, x.Value)).ToList(),
                    restockTrackedItems));
                showCredit = false;
                await FiscalisedCreditPosting.PostAsync(userId, OrganisationId, draft.Id);
            }
            else
            {
                await CreditNoteService.CreateAsync(userId, new(
                    OrganisationId,
                    InvoiceId,
                    creditDate,
                    creditReason,
                    creditAmount,
                    restockTrackedItems,
                    creditVatAmount));
            }
            showCredit = false;
            await Reload();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            status = ex.Message;
            await Reload();
        }
    }
}
