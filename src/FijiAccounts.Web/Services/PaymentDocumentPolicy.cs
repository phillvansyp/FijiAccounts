using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public static class PaymentDocumentPolicy
{
    // Fees are supported by the bank statement. Tax/payroll/loan liabilities and
    // owner movements have their own ledger records rather than trade documents.
    public static bool RequiresTradeDocument(LedgerAccount account) =>
        !account.IsBankAccount && (account.Type switch
        {
            AccountType.Expense => account.Code != "6400",
            AccountType.Revenue => true,
            AccountType.Asset => account.Code != "1150",
            AccountType.Liability => account.Code == "2000",
            AccountType.Equity => false,
            _ => true
        });

}
