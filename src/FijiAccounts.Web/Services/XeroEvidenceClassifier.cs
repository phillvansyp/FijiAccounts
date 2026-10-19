namespace FijiAccounts.Web.Services;

/// <summary>
/// Classifies original Xero export files from their report names. The rules are
/// deliberately deterministic so every classification can be explained and
/// repeated during an audit.
/// </summary>
public static class XeroEvidenceClassifier
{
    public static string? Classify(string fileName)
    {
        var name = Normalise(Path.GetFileNameWithoutExtension(fileName));
        if (string.IsNullOrWhiteSpace(name)) return null;

        if (Has(name, "receivable invoice detail", "aged receivable", "aged debtors", "accounts receivable"))
            return "Receivable Invoice Detail";
        if (Has(name, "payable invoice detail", "aged payable", "aged creditors", "accounts payable"))
            return "Payable Invoice Detail";
        if (Has(name, "bank reconciliation", "bank statement", "credit card statement", "loan statement"))
            return "Bank statements and reconciliation";
        if (Has(name, "fixed asset", "asset reconciliation", "depreciation schedule", "depreciation report"))
            return "Fixed assets and depreciation reconciliation";
        if (Has(name, "gst return", "gst reconciliation", "goods and services tax", "tax return"))
            return "GST returns and reconciliation";
        if (Has(name, "payroll history", "payroll activity", "payroll report", "employee earnings", "payroll no activity"))
            return "Payroll history reports";
        if (Has(name, "balance sheet and profit", "profit and loss and balance", "financial statements"))
            return "Balance Sheet and Profit & Loss";
        if (Has(name, "trial balance")) return "Trial balance";
        if (Has(name, "general ledger", "account transactions")) return "General ledger";
        if (Has(name, "chart of accounts", "account list")) return "Chart of accounts";
        if (Has(name, "contact", "customer and supplier")) return "Contacts";
        if (Has(name, "supplier credit", "purchase credit", "bill", "accounts payable invoice"))
            return "Bills, supplier credits and allocations";
        if (Has(name, "sales credit", "customer invoice", "sales invoice", "accounts receivable invoice"))
            return "Sales invoices, credits and allocations";
        if (Has(name, "inventory", "item list", "products and services")) return "Inventory items";
        if (Has(name, "budget")) return "Budgets";
        if (Has(name, "tracking categor", "tracking option")) return "Tracking categories";
        if (Has(name, "quote", "purchase order")) return "Quotes and purchase orders";
        if (Has(name, "repeating transaction", "repeating invoice", "repeating bill")) return "Repeating transactions";
        if (Has(name, "attachment", "document archive", "source document")) return "Document attachments";
        if (Has(name, "balance sheet", "profit and loss", "income statement"))
            return "Balance Sheet and Profit & Loss";

        return null;
    }

    private static string Normalise(string value) =>
        string.Join(' ', value.ToLowerInvariant()
            .Replace('-', ' ').Replace('_', ' ').Replace('.', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static bool Has(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}
