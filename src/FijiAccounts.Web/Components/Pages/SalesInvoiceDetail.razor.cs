namespace FijiAccounts.Web.Components.Pages;

public partial class SalesInvoiceDetail
{
    private bool showCredit;
    private DateOnly creditDate = DateOnly.FromDateTime(DateTime.Today);
    private string creditReason = string.Empty;
    private decimal creditAmount;
    private bool restockTrackedItems;
    private decimal AvailableCredit => invoice is null ? 0 : invoice.Total - invoice.AmountPaid - invoice.AmountCredited;

    private void OpenCredit()
    {
        showCredit = true;
        showVoid = false;
        creditAmount = AvailableCredit;
    }

    private async Task CreateCredit()
    {
        try
        {
            await CreditNoteService.CreateAsync(userId, new(OrganisationId, InvoiceId, creditDate, creditReason, creditAmount, restockTrackedItems));
            showCredit = false;
            await Reload();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            status = ex.Message;
        }
    }
}
