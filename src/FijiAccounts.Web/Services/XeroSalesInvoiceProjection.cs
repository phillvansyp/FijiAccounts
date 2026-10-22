using System.Globalization;
using System.Text.Json;
using FijiAccounts.Web.Data;

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
    public decimal BaseTotal => Base(Total);
    public decimal BaseAmountDue => Base(AmountDue);
    public bool IsOverdue => AmountDue > 0m && DueDate < DateOnly.FromDateTime(DateTime.Today) &&
        SourceStatus is not "VOIDED" and not "DELETED";
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
        decimal.Round(amount * (CurrencyRate > 0m ? CurrencyRate : 1m), 2, MidpointRounding.AwayFromZero);
}

public static class XeroSalesInvoiceProjectionParser
{
    public static bool TryParse(
        XeroMirrorRecord record,
        out XeroSalesInvoiceProjection invoice,
        bool includeLines = true)
    {
        invoice = null!;
        if (!record.DatasetKey.Equals("sales-invoices", StringComparison.OrdinalIgnoreCase) || record.IsArchived)
            return false;

        try
        {
            using var document = JsonDocument.Parse(record.SourceJson);
            var source = document.RootElement;
            if (!TryDate(source, "Date", out var issueDate) || !TryDate(source, "DueDate", out var dueDate))
                return false;

            var rate = Decimal(source, "CurrencyRate", 1m);
            if (rate <= 0m) rate = 1m;
            var lines = new List<XeroSalesInvoiceLineProjection>();
            if (includeLines && source.TryGetProperty("LineItems", out var lineItems) && lineItems.ValueKind == JsonValueKind.Array)
            {
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
                record.Id,
                record.ExternalId,
                Fallback(Text(source, "InvoiceNumber"), record.DisplayName),
                ContactName(source),
                Text(source, "Reference"),
                issueDate,
                dueDate,
                Text(source, "Status").Trim().ToUpperInvariant(),
                Fallback(Text(source, "CurrencyCode"), "NZD").ToUpperInvariant(),
                rate,
                Decimal(source, "SubTotal"),
                Decimal(source, "TotalTax"),
                Decimal(source, "Total"),
                Decimal(source, "AmountDue"),
                Decimal(source, "AmountPaid"),
                Decimal(source, "AmountCredited"),
                lines);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ContactName(JsonElement source)
    {
        if (source.TryGetProperty("Contact", out var contact) && contact.ValueKind == JsonValueKind.Object)
            return Fallback(Text(contact, "Name"), "Xero customer");
        return "Xero customer";
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

    private static bool TryDate(JsonElement source, string name, out DateOnly date)
    {
        var value = Text(source, name);
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
        {
            date = DateOnly.FromDateTime(timestamp.DateTime);
            return true;
        }

        if (value.StartsWith("/Date(", StringComparison.Ordinal))
        {
            var end = value.IndexOfAny(['+', '-', ')'], 6);
            if (end < 0) end = value.Length;
            if (long.TryParse(value[6..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds))
            {
                date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime);
                return true;
            }
        }

        date = default;
        return false;
    }
}
