using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class CreditCardRefundCodingTests
{
    [Fact]
    public async Task CodeCreditCardRefund_WithFijiVat_ReversesExpenseAndReducesLiability()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var card = test.Account("1000");
        card.BankAccountKind = BankAccountKind.CreditCard;

        await test.Db.SaveChangesAsync();

        // First post a normal purchase so the card has an existing liability.
        var purchaseStatement =
            await test.Reconciliation.AddStatementLineAsync(
                test.UserId,
                new StatementLineRequest(
                    OrganisationId: test.Organisation.Id,
                    BankAccountId: card.Id,
                    Date: new DateOnly(2026, 8, 18),
                    Description: "Original office purchase",
                    Reference: "CC-PURCHASE-001",
                    Amount: -112.50m));

        await test.BankCoding.PostAndReconcileAsync(
            test.UserId,
            new BankTransactionCodingRequest(
                OrganisationId: test.Organisation.Id,
                StatementLineId: purchaseStatement.Id,
                TargetAccountCode: "6500",
                Description: "Original office purchase",
                VatTreatment: VatTreatment.Standard));

        // Merchant refunds half of the original purchase.
        var refundStatement =
            await test.Reconciliation.AddStatementLineAsync(
                test.UserId,
                new StatementLineRequest(
                    OrganisationId: test.Organisation.Id,
                    BankAccountId: card.Id,
                    Date: new DateOnly(2026, 8, 19),
                    Description: "Merchant refund",
                    Reference: "CC-REFUND-001",
                    Amount: 56.25m));

        var refundJournal =
            await test.BankCoding.PostAndReconcileAsync(
                test.UserId,
                new BankTransactionCodingRequest(
                    OrganisationId: test.Organisation.Id,
                    StatementLineId: refundStatement.Id,
                    TargetAccountCode: "6500",
                    Description: "Merchant refund",
                    VatTreatment: VatTreatment.Standard));

        var expense =
            refundJournal.Lines.Single(
                x => x.LedgerAccountId ==
                     test.Account("6500").Id);

        var vat =
            refundJournal.Lines.Single(
                x => x.LedgerAccountId ==
                     test.Account("1150").Id);

        var cardLine =
            refundJournal.Lines.Single(
                x => x.LedgerAccountId == card.Id);

        // $56.25 inclusive = $50.00 net + $6.25 VAT.
        Assert.Equal(0m, expense.Debit);
        Assert.Equal(50m, expense.Credit);

        Assert.Equal(0m, vat.Debit);
        Assert.Equal(6.25m, vat.Credit);

        Assert.Equal(56.25m, cardLine.Debit);
        Assert.Equal(0m, cardLine.Credit);

        Assert.Equal(
            refundJournal.Lines.Sum(x => x.Debit),
            refundJournal.Lines.Sum(x => x.Credit));

        var reloadedRefund =
            await test.Db.BankStatementLines
                .AsNoTracking()
                .SingleAsync(x => x.Id == refundStatement.Id);

        Assert.NotNull(reloadedRefund.ReconciledAt);

        Assert.Equal(
            cardLine.Id,
            reloadedRefund.MatchedPostedJournalLineId);

        // Original purchase was $112.50 liability.
        // Refund reduces it by $56.25.
        Assert.Equal(
            -56.25m,
            await test.AccountBalanceAsync("1000"));

        // Expense and VAT are also reduced by half.
        Assert.Equal(
            50m,
            await test.AccountBalanceAsync("6500"));

        Assert.Equal(
            6.25m,
            await test.AccountBalanceAsync("1150"));
    }
}