using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BankTransactionCodingTests
{
    [Fact]
    public async Task CodeDebitCardPurchase_WithFijiVat_PostsAndReconcilesCorrectly()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        bank.BankAccountKind =
            FijiAccounts.Web.Data.BankAccountKind.DebitCard;

        await test.Db.SaveChangesAsync();

        var statement =
            await test.Reconciliation.AddStatementLineAsync(
                test.UserId,
                new StatementLineRequest(
                    OrganisationId: test.Organisation.Id,
                    BankAccountId: bank.Id,
                    Date: new DateOnly(2026, 8, 18),
                    Description: "Office supplies purchase",
                    Reference: "CARD-001",
                    Amount: -112.50m));

        var journal =
            await test.BankCoding.PostAndReconcileAsync(
                test.UserId,
                new BankTransactionCodingRequest(
                    OrganisationId: test.Organisation.Id,
                    StatementLineId: statement.Id,
                    TargetAccountCode: "6500",
                    Description: "Office supplies purchase",
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

        var bankLine =
            journal.Lines.Single(
                x => x.LedgerAccountId == bank.Id);

        Assert.Equal(100m, expense.Debit);
        Assert.Equal(0m, expense.Credit);

        Assert.Equal(12.50m, vat.Debit);
        Assert.Equal(0m, vat.Credit);

        Assert.Equal(0m, bankLine.Debit);
        Assert.Equal(112.50m, bankLine.Credit);

        Assert.Equal(
            112.50m,
            journal.Lines.Sum(x => x.Debit));

        Assert.Equal(
            112.50m,
            journal.Lines.Sum(x => x.Credit));

        var reloadedStatement =
            await test.Db.BankStatementLines
                .AsNoTracking()
                .SingleAsync(x => x.Id == statement.Id);

        Assert.NotNull(reloadedStatement.ReconciledAt);

        Assert.Equal(
            bankLine.Id,
            reloadedStatement.MatchedPostedJournalLineId);

        Assert.Equal(
            test.UserId,
            reloadedStatement.ReconciledByUserId);

        Assert.Equal(
            -112.50m,
            await test.AccountBalanceAsync("1000"));

        Assert.Equal(
            100m,
            await test.AccountBalanceAsync("6500"));

        Assert.Equal(
            12.50m,
            await test.AccountBalanceAsync("1150"));
    }
}