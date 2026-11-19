using System.Globalization;
using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record XeroOpenBalance(
    string Contact, string Document, DateOnly? DueDate, decimal Amount, Guid? ContactId = null);

/// <summary>Open documents and unapplied credits in the current Xero mirror.</summary>
public sealed class XeroOpenBalanceService(ApplicationDbContext db)
{
    internal async Task<IReadOnlyList<XeroOpenBalance>> ReadAsync(
        Guid organisationId, bool payables, CancellationToken ct = default)
    {
        var connection = await db.XeroConnections.AsNoTracking().SingleAsync(x =>
            x.OrganisationId == organisationId && x.IsActive && x.ContinuousSyncEnabled, ct);
        var baseCurrency = await db.Organisations.Where(x => x.Id == organisationId)
            .Select(x => x.BaseCurrency).SingleAsync(ct);
        var documents = await XeroSalesInvoiceProjectionParser.ReadCurrentAsync(
            db, organisationId, supplierBills: payables, openOnly: true);
        var result = documents.Select(x => new XeroOpenBalance(
            x.ContactName, x.InvoiceNumber, x.DueDate,
            x.Currency.Equals(baseCurrency, StringComparison.OrdinalIgnoreCase) ? x.AmountDue :
                x.SourceExchangeRate is > 0m ? decimal.Round(x.AmountDue / x.SourceExchangeRate.Value, 2, MidpointRounding.AwayFromZero) :
                throw new InvalidOperationException("An open Xero invoice lacks its exchange rate."), x.SourceContactId)).ToList();

        result.AddRange(await ReadCreditsAsync(connection.Id, baseCurrency, payables, ct));
        return result;
    }

    internal async Task<IReadOnlyList<XeroOpenBalance>> ReadCreditsAsync(
        Guid organisationId, bool payables, CancellationToken ct = default)
    {
        var connectionId = await db.XeroConnections.AsNoTracking().Where(x =>
            x.OrganisationId == organisationId && x.IsActive && x.ContinuousSyncEnabled)
            .Select(x => x.Id).SingleAsync(ct);
        var baseCurrency = await db.Organisations.AsNoTracking().Where(x => x.Id == organisationId)
            .Select(x => x.BaseCurrency).SingleAsync(ct);
        return await ReadCreditsAsync(connectionId, baseCurrency, payables, ct);
    }

    private async Task<IReadOnlyList<XeroOpenBalance>> ReadCreditsAsync(
        Guid connectionId, string baseCurrency, bool payables, CancellationToken ct)
    {
        var result = new List<XeroOpenBalance>();
        var creditDataset = payables ? "supplier-credits" : "sales-credits";
        var credits = db.XeroMirrorRecords.AsNoTracking().Where(x => x.XeroConnectionId == connectionId &&
            !x.IsArchived && (x.DatasetKey == creditDataset || x.DatasetKey == "overpayments" || x.DatasetKey == "prepayments"))
            .AsAsyncEnumerable().WithCancellation(ct);
        await foreach (var row in credits)
        {
            if (TryReadCredit(row, baseCurrency, payables) is { } credit)
                result.Add(credit);
        }
        return result;
    }

    internal static XeroOpenBalance? TryReadCredit(XeroMirrorRecord row, string baseCurrency, bool payables)
    {
        try
        {
            using var json = JsonDocument.Parse(row.SourceJson);
            var body = json.RootElement;
            var type = Text(body, "Type");
            var expected = payables
                ? new[] { "ACCPAYCREDIT", "SPEND-OVERPAYMENT", "SPEND-PREPAYMENT" }
                : new[] { "ACCRECCREDIT", "RECEIVE-OVERPAYMENT", "RECEIVE-PREPAYMENT" };
            if (!expected.Contains(type, StringComparer.OrdinalIgnoreCase) ||
                !Text(body, "Status").Equals("AUTHORISED", StringComparison.OrdinalIgnoreCase) ||
                !TryDecimal(body, "RemainingCredit", out var remaining) || remaining <= 0m)
                return null;

            var currency = Text(body, "CurrencyCode");
            if (currency.Length > 0 && !currency.Equals(baseCurrency, StringComparison.OrdinalIgnoreCase))
            {
                if (!TryDecimal(body, "CurrencyRate", out var rate) || rate <= 0m)
                    throw new InvalidOperationException("The Xero copy lacks the currency rate for an open credit.");
                remaining = decimal.Round(remaining / rate, 2, MidpointRounding.AwayFromZero);
            }
            var label = Text(body, "CreditNoteNumber");
            if (label.Length == 0)
                label = Text(body, "Reference");
            if (label.Length == 0)
                label = row.DisplayName;
            var contact = body.TryGetProperty("Contact", out var c) ? Text(c, "Name") : "";
            var hasAgeingDate = XeroSalesInvoiceProjectionParser.TryDate(body, "DueDate", out var ageingDate) ||
                                XeroSalesInvoiceProjectionParser.TryDate(body, "Date", out ageingDate);
            return new(contact, label, hasAgeingDate ? ageingDate : null, -remaining,
                c.ValueKind == JsonValueKind.Object && Guid.TryParse(Text(c, "ContactID"), out var id) ? id : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryDecimal(JsonElement body, string property, out decimal value) =>
        decimal.TryParse(Text(body, property), NumberStyles.Number | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture, out value);

    private static string Text(JsonElement body, string property) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(property, out var value)
            ? value.ToString() : "";
}

