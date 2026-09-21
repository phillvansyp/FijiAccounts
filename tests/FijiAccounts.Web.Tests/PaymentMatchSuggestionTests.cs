using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
namespace FijiAccounts.Web.Tests;
public sealed class PaymentMatchSuggestionTests
{
    private static PaymentDocument Document(string currency = "FJD", string status = "Posted") =>
        new(Guid.NewGuid(), true, "INV-1", "Customer", new(2026, 7, 1), new(2026, 7, 7), currency, 100, status);
    private static BankStatementLine Bank(decimal amount = 100) => new() { Id = Guid.NewGuid(), Description = "Customer payment", Amount = amount, TransactionDate = new(2026, 7, 5) };
    private static PaymentWorkspace Workspace(PaymentDocument doc, BankStatementLine bank, ConnectedPayment[]? payments = null, bool missing = false) =>
        new([doc], payments ?? [], [bank], missing ? new HashSet<Guid> { bank.Id } : new HashSet<Guid>());
    [Theory]
    [InlineData("FJD", 100, true)]
    [InlineData("FJD", 99, false)]
    [InlineData("NZD", 90, true)]
    [InlineData("NZD", 110, true)]
    [InlineData("NZD", 89.99, false)]
    [InlineData("NZD", -100, false)]
    public void AmountAndDirectionRules(string currency, decimal amount, bool expected)
    {
        var doc = Document(currency); var bank = Bank(amount);
        Assert.Equal(expected, PaymentMatchSuggestion.Matches(Workspace(doc, bank), doc, bank, "FJD"));
    }
    [Theory]
    [InlineData("Paid")]
    [InlineData("Voided")]
    [InlineData("Draft")]
    [InlineData("Credited")]
    public void NoMarkerForIneligibleInvoice(string status)
    {
        var doc = Document(status: status); var bank = Bank();
        Assert.False(PaymentMatchSuggestion.Matches(Workspace(doc, bank), doc, bank, "FJD"));
    }
    [Fact]
    public void ExistingPaymentAndMatchedBankAreNotSuggestedAgain()
    {
        var doc = Document(); var bank = Bank();
        var payment = new ConnectedPayment(Guid.NewGuid(), Guid.NewGuid(), bank.TransactionDate, 100, "Receipt", [doc.Id], null);
        Assert.False(PaymentMatchSuggestion.Matches(Workspace(doc, bank, [payment]), doc, bank, "FJD"));
        Assert.False(PaymentMatchSuggestion.Matches(Workspace(doc, bank, [payment with { DocumentIds = [], StatementId = bank.Id }]), doc, bank, "FJD"));
        bank.ReconciledAt = DateTimeOffset.UtcNow;
        Assert.False(PaymentMatchSuggestion.Matches(Workspace(doc, bank), doc, bank, "FJD"));
        Assert.True(PaymentMatchSuggestion.Matches(Workspace(doc, bank, missing: true), doc, bank, "FJD"));
    }
    [Fact]
    public void FutureInvoicesAndZeroBalancesAreExcluded()
    {
        var doc = Document(); var bank = Bank();
        Assert.False(PaymentMatchSuggestion.Matches(Workspace(doc, bank), doc with { Date = new(2026, 8, 1) }, bank, "FJD"));
        Assert.False(PaymentMatchSuggestion.Matches(Workspace(doc, bank), doc with { Outstanding = 0 }, bank, "FJD"));
    }
}

