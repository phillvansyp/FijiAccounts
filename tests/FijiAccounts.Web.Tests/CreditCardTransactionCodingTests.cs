using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class CreditCardTransactionCodingTests
{
    [Fact]
    public async Task CodeCreditCardPurchase_WithFijiVat_PostsLiabilityAndReconciles()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var card = test.Account("1000");

        card.BankAccountKind = BankAccountKind.CreditCard;

        await test.Db.SaveChangesAsync();

        var statement =
            await test.Reconciliation.AddStatementLineAsync(
                test.UserId,
                new StatementLineRequest(
                    OrganisationId: test.Organisation.Id,
                    BankAccountId: card.Id,
                    Date: new DateOnly(2026, 8, 18),
                    Description: "Credit card office purchase",
                    Reference: "CC-001",
                    Amount: -112.50m));

        var journal =
            await test.BankCoding.PostAndReconcileAsync(
                test.UserId,
                new BankTransactionCodingRequest(
                    OrganisationId: test.Organisation.Id,
                    StatementLineId: statement.Id,
                    TargetAccountCode: "6500",
                    Description: "Credit card office purchase",
                    VatTreatment: VatTreatment.Standard));

        Assert.NotEqual(Guid.Empty, journal.Id);

        var expense =
            journal.Lines.Single(
                x => x.LedgerAccountId ==
                     test.Account("6500").Id);

        var vat =
            journal.Lines.Single(
                x => x.LedgerAccountId ==
                     test.Account("1150").Id);

        var cardLine =
            journal.Lines.Single(
                x => x.LedgerAccountId == card.Id);

        Assert.Equal(100m, expense.Debit);
        Assert.Equal(0m, expense.Credit);

        Assert.Equal(12.50m, vat.Debit);
        Assert.Equal(0m, vat.Credit);

        Assert.Equal(0m, cardLine.Debit);
        Assert.Equal(112.50m, cardLine.Credit);

        Assert.Equal(
            journal.Lines.Sum(x => x.Debit),
            journal.Lines.Sum(x => x.Credit));

        Assert.Equal(
            112.50m,
            journal.Lines.Sum(x => x.Debit));

        var reloadedStatement =
            await test.Db.BankStatementLines
                .AsNoTracking()
                .SingleAsync(x => x.Id == statement.Id);

        Assert.NotNull(reloadedStatement.ReconciledAt);

        Assert.Equal(
            cardLine.Id,
            reloadedStatement.MatchedPostedJournalLineId);

        Assert.Equal(
            test.UserId,
            reloadedStatement.ReconciledByUserId);

        Assert.Equal(
            100m,
            await test.AccountBalanceAsync("6500"));

        Assert.Equal(
            12.50m,
            await test.AccountBalanceAsync("1150"));

        Assert.Equal(
            -112.50m,
            await test.AccountBalanceAsync("1000"));
    }
}