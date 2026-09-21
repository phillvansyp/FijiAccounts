using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public static class PaymentMatchSuggestion
{
    public static bool Matches(PaymentWorkspace workspace, PaymentDocument document, BankStatementLine bank, string baseCurrency)
    {
        if (document.Status is "Draft" or "Voided" or "Void" or "Credited" or "Paid" ||
            document.Outstanding <= 0 || document.OutstandingDocumentAmount <= 0 ||
            string.IsNullOrWhiteSpace(document.Currency) || string.IsNullOrWhiteSpace(baseCurrency) ||
            workspace.Payments.Any(p => p.StatementId == bank.Id ||
                (p.StatementId == null && p.DocumentIds.Contains(document.Id))) ||
            (bank.ReconciledAt != null && !workspace.MissingDocuments.Contains(bank.Id))) return false;
        if (!string.Equals(document.Currency.Trim(), baseCurrency.Trim(), StringComparison.OrdinalIgnoreCase))
            return ForeignCurrencyMatchSuggestion.Matches(document.Currency, baseCurrency, document.Outstanding,
                bank.Amount, document.IsSales, document.Date, bank.TransactionDate);
        return (document.IsSales ? bank.Amount > 0 : bank.Amount < 0) &&
            document.Date <= bank.TransactionDate.AddDays(3) && Math.Abs(bank.Amount) == document.Outstanding;
    }
}
