using System.Globalization;
using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record XeroSalesInvoiceLineProjection(
    string Description,
    decimal Quantity,
    decimal UnitAmount,
    decimal LineAmount,
    decimal TaxAmount,
    string AccountCode,
    string TaxType);

public sealed record XeroSalesInvoiceProjection(
    Guid RecordId,
    string ExternalId,
    string InvoiceNumber,
    string ContactName,
    string Reference,
    DateOnly IssueDate,
    DateOnly DueDate,
    string SourceStatus,
    string Currency,
    decimal CurrencyRate,
    decimal Subtotal,
    decimal TaxTotal,
    decimal Total,
    decimal AmountDue,
    decimal AmountPaid,
    decimal AmountCredited,
    IReadOnlyList<XeroSalesInvoiceLineProjection> Lines)
{
    public Guid? SourceContactId { get; init; }
    public decimal? SourceExchangeRate { get; init; }
    public decimal BaseTotal => Base(Total);
    public decimal BaseAmountDue => Base(AmountDue);
    public decimal BaseAmountPaid => Base(AmountPaid);
    public bool IsAwaitingPayment => SourceStatus == "AUTHORISED" && AmountDue > 0m;
    public bool IsOverdue => IsAwaitingPayment && DueDate < DateOnly.FromDateTime(DateTime.Today);
    public string StatusKey => IsOverdue
        ? "Overdue"
        : SourceStatus switch
        {
            "DRAFT" => "Draft",
            "VOIDED" or "DELETED" => "Voided",
            "PAID" => "Paid",
            _ when AmountDue <= 0m => "Paid",
            _ when AmountPaid > 0m || AmountCredited > 0m => "PartPaid",
            _ => "Posted"
        };
    public string StatusLabel => StatusKey switch
    {
        "PartPaid" => "Part paid",
        "Posted" => "Awaiting payment",
        _ => StatusKey
    };

    private decimal Base(decimal amount) =>
        decimal.Round(amount / (CurrencyRate > 0m ? CurrencyRate : 1m), 2, MidpointRounding.AwayFromZero);
}

public static class XeroSalesInvoiceProjectionParser
{
    internal static (string Supplier, string Reference) BillKey(string supplier, string reference) =>
        (supplier.Trim().ToUpperInvariant(), reference.Trim().ToUpperInvariant());

    public static bool TryParse(
        XeroMirrorRecord record,
        out XeroSalesInvoiceProjection invoice,
        bool includeLines = true,
        bool supplierBill = false)
    {
        invoice = null!;
        var datasetKey = supplierBill ? "bills" : "sales-invoices";
        if (!record.DatasetKey.Equals(datasetKey, StringComparison.OrdinalIgnoreCase) || record.IsArchived)
            return false;

        return TryParseHeader(record.Id, record.ExternalId, record.DisplayName, record.SourceJson, out invoice,
            includeLines, supplierBill);
    }

    internal static bool TryParseHeader(Guid id, string externalId, string displayName, string json,
        out XeroSalesInvoiceProjection invoice, bool includeLines = false, bool supplierBill = false)
    {
        invoice = null!;
        try
        {
            using var document = JsonDocument.Parse(json);
            var source = document.RootElement;
            if (!TryDate(source, "Date", out var issueDate) || !TryDate(source, "DueDate", out var dueDate))
                return false;

            var rate = Decimal(source, "CurrencyRate", 1m);
            if (rate <= 0m) rate = 1m;
            List<XeroSalesInvoiceLineProjection>? lines = null;
            if (includeLines && source.TryGetProperty("LineItems", out var lineItems) && lineItems.ValueKind == JsonValueKind.Array)
            {
                lines = new(lineItems.GetArrayLength());
                foreach (var line in lineItems.EnumerateArray())
                {
                    lines.Add(new(
                        Text(line, "Description"),
                        Decimal(line, "Quantity"),
                        Decimal(line, "UnitAmount"),
                        Decimal(line, "LineAmount"),
                        Decimal(line, "TaxAmount"),
                        Text(line, "AccountCode"),
                        Text(line, "TaxType")));
                }
            }

            invoice = new(
                id,
                externalId,
                Fallback(Text(source, "InvoiceNumber"), displayName),
                ContactName(source, supplierBill ? "Xero supplier" : "Xero customer"),
                Text(source, "Reference"),
                issueDate,
                dueDate,
                Text(source, "Status").Trim().ToUpperInvariant(),
                Fallback(Text(source, "CurrencyCode").ToUpperInvariant(), "Unspecified"),
                rate,
                Decimal(source, "SubTotal"),
                Decimal(source, "TotalTax"),
                Decimal(source, "Total"),
                Decimal(source, "AmountDue"),
                Decimal(source, "AmountPaid"),
                Decimal(source, "AmountCredited"),
                lines ?? (IReadOnlyList<XeroSalesInvoiceLineProjection>)[])
            {
                SourceContactId = source.TryGetProperty("Contact", out var party) &&
                    Guid.TryParse(Text(party, "ContactID"), out var contactId) ? contactId : null,
                SourceExchangeRate = Decimal(source, "CurrencyRate", 0m)
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static async Task<List<XeroSalesInvoiceProjection>> ReadCurrentAsync(
        ApplicationDbContext db, Guid organisationId, bool supplierBills = false, bool openOnly = false)
    {
        var datasetKey = supplierBills ? "bills" : "sales-invoices";
        var results = new List<XeroSalesInvoiceProjection>();
        // Begin the indexed lookup at the organisation's active connection. SQLite
        // otherwise prefers the full dataset index on large invoice histories.
        const string openDocumentsSql = """
            SELECT r.* FROM XeroConnections c CROSS JOIN XeroMirrorRecords r
            WHERE c.OrganisationId = {0} AND c.IsActive
              AND r.OrganisationId = c.OrganisationId AND r.XeroConnectionId = c.Id
              AND r.DatasetKey = {1} AND
            """ + " " + Data.Migrations.IndexOpenXeroDocuments.CandidateFilter;
        var query = openOnly
            ? db.XeroMirrorRecords.FromSqlRaw(openDocumentsSql, organisationId, datasetKey)
            : db.XeroMirrorRecords.Where(x => x.OrganisationId == organisationId &&
                x.DatasetKey == datasetKey && !x.IsArchived && x.Connection.IsActive);
        var records = query.AsNoTracking().AsAsyncEnumerable();
        await foreach (var record in records)
        {
            if (TryParse(record, out var projection, includeLines: false, supplierBill: supplierBills) &&
                (!openOnly || projection.IsAwaitingPayment))
                results.Add(projection);
        }
        return results;
    }

    private static string ContactName(JsonElement source, string fallback)
    {
        if (source.TryGetProperty("Contact", out var contact) && contact.ValueKind == JsonValueKind.Object)
            return Fallback(Text(contact, "Name"), fallback);
        return fallback;
    }

    private static string Fallback(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Text(JsonElement source, string name) =>
        source.ValueKind == JsonValueKind.Object && source.TryGetProperty(name, out var value) &&
        value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
            ? value.ToString()
            : string.Empty;

    private static decimal Decimal(JsonElement source, string name, decimal fallback = 0m) =>
        decimal.TryParse(Text(source, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    internal static bool TryDate(JsonElement source, string name, out DateOnly date)
    {
        var value = Text(source, name);
        if (value.StartsWith("/Date(", StringComparison.Ordinal))
        {
            var end = value.IndexOfAny(['+', '-', ')'], 6);
            if (end < 0) end = value.Length;
            if (long.TryParse(value.AsSpan(6, end - 6), NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds)
                && milliseconds is >= -62135596800000 and <= 253402300799999)
            {
                date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime);
                return true;
            }
        }
        else if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        else if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
        {
            date = DateOnly.FromDateTime(timestamp.DateTime);
            return true;
        }

        date = default;
        return false;
    }
}
