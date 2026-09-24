using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class PayrollGovernmentPaymentSyncTests
{
    [Fact]
    public void RequiresReconciledBankLineForExactMonthlyLiabilityAndAgency()
    {
        var liability = Guid.NewGuid();
        var statement = Statement(liability);
        var empty = new HashSet<Guid>();
        Assert.True(PayrollGovernmentPaymentSyncService.IsVerified(statement, 80m, liability,
            "paye-monthly", new DateOnly(2026, 9, 1), empty));
        Assert.False(PayrollGovernmentPaymentSyncService.IsVerified(statement, 79m, liability,
            "paye-monthly", new DateOnly(2026, 9, 1), empty));
        Assert.False(PayrollGovernmentPaymentSyncService.IsVerified(statement, 80m, Guid.NewGuid(),
            "paye-monthly", new DateOnly(2026, 9, 1), empty));
        Assert.False(PayrollGovernmentPaymentSyncService.IsVerified(statement, 80m, liability,
            "fnpf-payment", new DateOnly(2026, 9, 1), empty));
        Assert.False(PayrollGovernmentPaymentSyncService.IsVerified(statement, 80m, liability,
            "paye-monthly", new DateOnly(2026, 8, 1), empty));
        statement.ReconciledAt = null;
        Assert.False(PayrollGovernmentPaymentSyncService.IsVerified(statement, 80m, liability,
            "paye-monthly", new DateOnly(2026, 9, 1), empty));
    }

    [Fact]
    public void ReversedJournalCannotVerifyPayment()
    {
        var liability = Guid.NewGuid();
        var statement = Statement(liability);
        var journalId = statement.MatchedPostedJournalLine!.PostedJournal.Id;
        Assert.False(PayrollGovernmentPaymentSyncService.IsVerified(statement, 80m, liability,
            "paye-monthly", new DateOnly(2026, 9, 1), new HashSet<Guid> { journalId }));
    }

    private static BankStatementLine Statement(Guid liability)
    {
        var organisation = Guid.NewGuid();
        var bank = Guid.NewGuid();
        var journal = new PostedJournal
        {
            OrganisationId = organisation, EntryDate = new DateOnly(2026, 10, 10),
            Reference = "PAYE 2026-09", Currency = "FJD", PostedByUserId = "tester"
        };
        var bankLine = new PostedJournalLine
        {
            PostedJournal = journal, LedgerAccountId = bank, Description = "PAYE", Credit = 80m
        };
        journal.Lines.Add(bankLine);
        journal.Lines.Add(new PostedJournalLine
        {
            PostedJournal = journal, LedgerAccountId = liability, Description = "PAYE", Debit = 80m
        });
        return new BankStatementLine
        {
            OrganisationId = organisation, BankAccountId = bank,
            TransactionDate = new DateOnly(2026, 10, 10), Description = "FRCS payment",
            Amount = -80m, MatchedPostedJournalLineId = bankLine.Id,
            MatchedPostedJournalLine = bankLine, ReconciledAt = DateTimeOffset.UtcNow
        };
    }
}
