using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class BankTransactionReopenCodingTests
{
    [Fact]
    public async Task ReopenAndRecodeBankTransaction_ReversesOriginalAndAppliesNewCoding()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        var statement =
            new BankStatementLine
            {
                OrganisationId = test.Organisation.Id,
                BankAccountId = bank.Id,
                TransactionDate = new DateOnly(2026, 8, 18),
                Description = "Office purchase",
                Reference = "RECODE-001",
                Amount = -112.50m,
                Source = "Test"
            };

        test.Db.BankStatementLines.Add(statement);
        await test.Db.SaveChangesAsync();

        /*
         * First coding:
         * Office Consumables.
         */
        var firstJournal =
            await test.BankCoding.PostAndReconcileAsync(
                test.UserId,
                new BankTransactionCodingRequest(
                    OrganisationId: test.Organisation.Id,
                    StatementLineId: statement.Id,
                    TargetAccountCode: "6500",
                    Description: "Office purchase",
                    VatTreatment: VatTreatment.Standard));

        var afterFirstCoding =
            await test.Db.BankStatementLines
                .AsNoTracking()
                .SingleAsync(x => x.Id == statement.Id);

        Assert.NotNull(afterFirstCoding.ReconciledAt);
        Assert.NotNull(afterFirstCoding.MatchedPostedJournalLineId);

        Assert.Equal(
            100m,
            await test.AccountBalanceAsync("6500"));

        Assert.Equal(
            12.50m,
            await test.AccountBalanceAsync("1150"));

        Assert.Equal(
            -112.50m,
            await test.AccountBalanceAsync("1000"));

        /*
         * Reopen the coding.
         * This must post an exact reversing journal and clear reconciliation.
         */
        await test.BankCoding.ReopenCodingAsync(
            test.UserId,
            test.Organisation.Id,
            statement.Id);

        var reopened =
            await test.Db.BankStatementLines
                .AsNoTracking()
                .SingleAsync(x => x.Id == statement.Id);

        Assert.Null(reopened.ReconciledAt);
        Assert.Null(reopened.MatchedPostedJournalLineId);
        Assert.Null(reopened.ReconciledByUserId);

        /*
         * Original accounting should now be completely reversed.
         */
        Assert.Equal(
            0m,
            await test.AccountBalanceAsync("6500"));

        Assert.Equal(
            0m,
            await test.AccountBalanceAsync("1150"));

        Assert.Equal(
            0m,
            await test.AccountBalanceAsync("1000"));

        var journalsAfterReopen =
            await test.Db.PostedJournals
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == test.Organisation.Id)
                .ToListAsync();

        Assert.Equal(2, journalsAfterReopen.Count);

        Assert.Contains(
            journalsAfterReopen,
            x => x.Id == firstJournal.Id);

        Assert.Contains(
            journalsAfterReopen,
            x =>
                x.Reference == $"REV-{firstJournal.Reference}");

        /*
         * Re-code to a different expense account.
         * 6600 is used here deliberately so we can prove
         * the original 6500 coding remains net zero.
         */
        var secondJournal =
            await test.BankCoding.PostAndReconcileAsync(
                test.UserId,
                new BankTransactionCodingRequest(
                    OrganisationId: test.Organisation.Id,
                    StatementLineId: statement.Id,
                    TargetAccountCode: "6600",
                    Description: "Corrected expense coding",
                    VatTreatment: VatTreatment.Standard));

        Assert.NotEqual(firstJournal.Id, secondJournal.Id);

        var finalStatement =
            await test.Db.BankStatementLines
                .AsNoTracking()
                .SingleAsync(x => x.Id == statement.Id);

        Assert.NotNull(finalStatement.ReconciledAt);
        Assert.NotNull(finalStatement.MatchedPostedJournalLineId);
        Assert.Equal(
            test.UserId,
            finalStatement.ReconciledByUserId);

        /*
         * Old coding remains zero.
         */
        Assert.Equal(
            0m,
            await test.AccountBalanceAsync("6500"));

        /*
         * Corrected expense receives the final net amount.
         */
        Assert.Equal(
            100m,
            await test.AccountBalanceAsync("6600"));

        Assert.Equal(
            12.50m,
            await test.AccountBalanceAsync("1150"));

        Assert.Equal(
            -112.50m,
            await test.AccountBalanceAsync("1000"));

        /*
         * We should now have:
         * 1 original journal
         * 1 reversing journal
         * 1 corrected journal
         */
        var finalJournals =
            await test.Db.PostedJournals
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == test.Organisation.Id)
                .ToListAsync();

        Assert.Equal(3, finalJournals.Count);

        var reopenAudit =
            await test.Db.AuditEvents
                .AsNoTracking()
                .SingleAsync(x =>
                    x.OrganisationId ==
                        test.Organisation.Id &&
                    x.EventType ==
                        "BankTransactionCodingReopened" &&
                    x.EntityId ==
                        statement.Id.ToString());

        Assert.Equal(test.UserId, reopenAudit.UserId);
    }
}