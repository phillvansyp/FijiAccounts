using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class InternalBankTransferAccountingTests
{
    [Fact]
    public async Task BothStatementSides_UseOneTransferAndOneJournal()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bankA = test.Account("1000");

        var bankB = new LedgerAccount
        {
            OrganisationId = test.Organisation.Id,
            Code = "1001",
            Name = "Second Bank",
            Type = AccountType.Asset,
            IsBankAccount = true,
            BankAccountKind = BankAccountKind.Bank,
            IsSystemAccount = false,
            IsActive = true
        };

        test.Db.LedgerAccounts.Add(bankB);
        await test.Db.SaveChangesAsync();

        /*
         * First statement side:
         * money leaves Bank A and arrives at Bank B.
         */
        var outgoingStatement =
            new BankStatementLine
            {
                OrganisationId = test.Organisation.Id,
                BankAccountId = bankA.Id,
                TransactionDate = new DateOnly(2026, 8, 18),
                Description = "Transfer to Second Bank",
                Reference = "TRF-001",
                Amount = -500m,
                Source = "Test"
            };

        test.Db.BankStatementLines.Add(outgoingStatement);
        await test.Db.SaveChangesAsync();

        var firstJournal =
            await test.BankCoding.PostAndReconcileAsync(
                test.UserId,
                new BankTransactionCodingRequest(
                    OrganisationId: test.Organisation.Id,
                    StatementLineId: outgoingStatement.Id,
                    TargetAccountCode: "",
                    Description: "Transfer to Second Bank",
                    VatTreatment: VatTreatment.Exempt,
                    TransferToBankAccountId: bankB.Id));

        Assert.NotEqual(Guid.Empty, firstJournal.Id);

        var transfersAfterFirstSide =
            await test.Db.BankTransfers
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == test.Organisation.Id)
                .ToListAsync();

        Assert.Single(transfersAfterFirstSide);

        var transfer = transfersAfterFirstSide.Single();

        Assert.Equal(bankA.Id, transfer.FromBankAccountId);
        Assert.Equal(bankB.Id, transfer.ToBankAccountId);
        Assert.Equal(500m, transfer.Amount);
        Assert.Equal(firstJournal.Id, transfer.PostedJournalId);

        var firstOutgoing =
            firstJournal.Lines.Single(
                x => x.LedgerAccountId == bankA.Id);

        var firstIncoming =
            firstJournal.Lines.Single(
                x => x.LedgerAccountId == bankB.Id);

        Assert.Equal(0m, firstOutgoing.Debit);
        Assert.Equal(500m, firstOutgoing.Credit);

        Assert.Equal(500m, firstIncoming.Debit);
        Assert.Equal(0m, firstIncoming.Credit);

        /*
         * Second statement side:
         * Bank B statement shows the matching receipt.
         */
        var incomingStatement =
            new BankStatementLine
            {
                OrganisationId = test.Organisation.Id,
                BankAccountId = bankB.Id,
                TransactionDate = new DateOnly(2026, 8, 19),
                Description = "Transfer from Bank",
                Reference = "TRF-001",
                Amount = 500m,
                Source = "Test"
            };

        test.Db.BankStatementLines.Add(incomingStatement);
        await test.Db.SaveChangesAsync();

        var secondResult =
            await test.BankCoding.PostAndReconcileAsync(
                test.UserId,
                new BankTransactionCodingRequest(
                    OrganisationId: test.Organisation.Id,
                    StatementLineId: incomingStatement.Id,
                    TargetAccountCode: "",
                    Description: "Transfer from Bank",
                    VatTreatment: VatTreatment.Exempt,
                    TransferToBankAccountId: bankA.Id));

        /*
         * The important rule:
         * the second statement side must match the existing transfer,
         * not create another journal.
         */
        Assert.Equal(firstJournal.Id, secondResult.Id);

        var transfers =
            await test.Db.BankTransfers
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == test.Organisation.Id)
                .ToListAsync();

        Assert.Single(transfers);

        var journals =
            await test.Db.PostedJournals
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == test.Organisation.Id)
                .ToListAsync();

        Assert.Single(journals);

        var reloadedOutgoing =
            await test.Db.BankStatementLines
                .AsNoTracking()
                .SingleAsync(
                    x => x.Id == outgoingStatement.Id);

        var reloadedIncoming =
            await test.Db.BankStatementLines
                .AsNoTracking()
                .SingleAsync(
                    x => x.Id == incomingStatement.Id);

        Assert.NotNull(reloadedOutgoing.ReconciledAt);
        Assert.NotNull(reloadedIncoming.ReconciledAt);

        Assert.Equal(
            firstOutgoing.Id,
            reloadedOutgoing.MatchedPostedJournalLineId);

        Assert.Equal(
            firstIncoming.Id,
            reloadedIncoming.MatchedPostedJournalLineId);

        /*
         * Net effect of an internal transfer across the organisation:
         * total cash is unchanged.
         */
        Assert.Equal(
            -500m,
            await test.AccountBalanceAsync("1000"));

        Assert.Equal(
            500m,
            await test.AccountBalanceAsync("1001"));

        var totalBankMovement =
            await test.AccountBalanceAsync("1000") +
            await test.AccountBalanceAsync("1001");

        Assert.Equal(0m, totalBankMovement);
    }
}