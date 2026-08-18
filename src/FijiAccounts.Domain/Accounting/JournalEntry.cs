namespace FijiAccounts.Domain.Accounting;

public enum AccountType { Asset, Liability, Equity, Revenue, Expense }

public sealed record JournalLine(string AccountCode, string Description, decimal Debit, decimal Credit)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AccountCode)) throw new DomainException("Every journal line needs an account.");
        if (Debit < 0 || Credit < 0) throw new DomainException("Debit and credit amounts cannot be negative.");
        if ((Debit == 0) == (Credit == 0)) throw new DomainException("A journal line must contain either a debit or a credit.");
    }
}

public sealed class JournalEntry
{
    public Guid Id { get; } = Guid.NewGuid();
    public Guid OrganisationId { get; }
    public DateOnly Date { get; }
    public string Reference { get; }
    public IReadOnlyList<JournalLine> Lines { get; }

    public JournalEntry(Guid organisationId, DateOnly date, string reference, IEnumerable<JournalLine> lines)
    {
        OrganisationId = organisationId;
        Date = date;
        Reference = reference.Trim();
        Lines = lines.ToArray();

        if (organisationId == Guid.Empty) throw new DomainException("A journal must belong to an organisation.");
        if (Lines.Count < 2) throw new DomainException("A journal needs at least two lines.");
        foreach (var line in Lines) line.Validate();

        var debits = decimal.Round(Lines.Sum(x => x.Debit), 2);
        var credits = decimal.Round(Lines.Sum(x => x.Credit), 2);
        if (debits != credits) throw new DomainException("Journal debits must equal credits.");
    }
}

public sealed class DomainException(string message) : Exception(message);
