using System.ComponentModel.DataAnnotations;
using FijiAccounts.Domain.Accounting;

namespace FijiAccounts.Web.Data;

public enum BankAccountKind
{
    Bank = 0,
    CreditCard = 1,
    DebitCard = 2
}

public sealed class LedgerAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    [MaxLength(20)] public required string Code { get; set; }
    [MaxLength(160)] public required string Name { get; set; }
    public AccountType Type { get; set; }
    public bool IsBankAccount { get; set; }
    public BankAccountKind BankAccountKind { get; set; } = BankAccountKind.Bank;
    [MaxLength(80)] public string? BankAccountNumber { get; set; }
    public bool IsSystemAccount { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class OrganisationInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;
    [MaxLength(320)] public required string Email { get; set; }
    public OrganisationRole Role { get; set; }
    [MaxLength(64)] public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public static class FijiStarterChart
{
    public static IReadOnlyList<LedgerAccount> For(Guid organisationId)
    {
        LedgerAccount New(string code, string name, AccountType type) =>
            new() { OrganisationId = organisationId, Code = code, Name = name, Type = type, IsSystemAccount = true, IsBankAccount = code == "1000" };
        return
        [
            New("1000", "Bank", AccountType.Asset), New("1100", "Accounts Receivable", AccountType.Asset),
            New("1150", "VAT Receivable", AccountType.Asset), New("1200", "Inventory", AccountType.Asset), New("1500", "Property, Plant and Equipment", AccountType.Asset),
            New("2000", "Accounts Payable", AccountType.Liability), New("2100", "VAT Payable", AccountType.Liability),
            New("2200", "PAYE and Other Payroll Liabilities", AccountType.Liability), New("2500", "Loans", AccountType.Liability),
            New("3000", "Owner's Equity", AccountType.Equity), New("3100", "Retained Earnings", AccountType.Equity),
            New("4000", "Sales", AccountType.Revenue), New("4100", "Other Income", AccountType.Revenue),
            New("5000", "Cost of Sales", AccountType.Expense), New("6000", "Wages and Salaries", AccountType.Expense),
            New("6100", "Rent", AccountType.Expense), New("6200", "Utilities", AccountType.Expense),
            New("6300", "Professional Fees", AccountType.Expense), New("6400", "Bank Fees and Charges", AccountType.Expense),
            New("6500", "Office Consumables", AccountType.Expense),
            New("6600", "IT & Computer Expenses", AccountType.Expense),
            New("6900", "Other Operating Expenses", AccountType.Expense)
        ];
    }
}
