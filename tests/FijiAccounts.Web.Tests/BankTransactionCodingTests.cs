using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
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

    [Fact]
    public async Task PostAndReconcileAsync_WhenVatReceivableControlAccountHasWrongType_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        bank.BankAccountKind =
            FijiAccounts.Web.Data.BankAccountKind.DebitCard;

        var vatReceivable = test.Account("1150");
        vatReceivable.Type = AccountType.Liability;

        await test.Db.SaveChangesAsync();

        var statement =
            await test.Reconciliation.AddStatementLineAsync(
                test.UserId,
                new StatementLineRequest(
                    OrganisationId: test.Organisation.Id,
                    BankAccountId: bank.Id,
                    Date: new DateOnly(2026, 8, 18),
                    Description: "Office supplies purchase",
                    Reference: "CARD-WRONG-1150",
                    Amount: -112.50m));

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    test.BankCoding.PostAndReconcileAsync(
                        test.UserId,
                        new BankTransactionCodingRequest(
                            OrganisationId: test.Organisation.Id,
                            StatementLineId: statement.Id,
                            TargetAccountCode: "6500",
                            Description: "Office supplies purchase",
                            VatTreatment: VatTreatment.Standard)));

        Assert.Contains(
            "1150",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        var reloadedStatement =
            await test.Db.BankStatementLines
                .AsNoTracking()
                .SingleAsync(x => x.Id == statement.Id);

        Assert.Null(reloadedStatement.ReconciledAt);
        Assert.Null(reloadedStatement.MatchedPostedJournalLineId);
    }

    [Fact]
public async Task PostAndReconcileAsync_WhenVatPayableControlAccountHasWrongType_IsRejected()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var bank = test.Account("1000");

    bank.BankAccountKind =
        FijiAccounts.Web.Data.BankAccountKind.DebitCard;

    var vatPayable = test.Account("2100");
    vatPayable.Type = AccountType.Asset;

    await test.Db.SaveChangesAsync();

    var statement =
        await test.Reconciliation.AddStatementLineAsync(
            test.UserId,
            new StatementLineRequest(
                OrganisationId: test.Organisation.Id,
                BankAccountId: bank.Id,
                Date: new DateOnly(2026, 8, 18),
                Description: "Customer receipt style bank credit",
                Reference: "BANK-WRONG-2100",
                Amount: 112.50m));

    var journalCountBefore =
        await test.Db.PostedJournals.CountAsync();

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                test.BankCoding.PostAndReconcileAsync(
                    test.UserId,
                    new BankTransactionCodingRequest(
                        OrganisationId: test.Organisation.Id,
                        StatementLineId: statement.Id,
                        TargetAccountCode: "4000",
                        Description: "Customer receipt style bank credit",
                        VatTreatment: VatTreatment.Standard)));

    Assert.Contains(
        "2100",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);

    Assert.Equal(
        journalCountBefore,
        await test.Db.PostedJournals.CountAsync());

    var reloadedStatement =
        await test.Db.BankStatementLines
            .AsNoTracking()
            .SingleAsync(x => x.Id == statement.Id);

    Assert.Null(reloadedStatement.ReconciledAt);
    Assert.Null(reloadedStatement.MatchedPostedJournalLineId);
}

    [Fact]
    public async Task ReopenCodingAsync_WhenStatementDateIsInsideLockedAccountingPeriod_IsRejectedWithoutMutation()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank = test.Account("1000");

        var statement =
            await test.Reconciliation.AddStatementLineAsync(
                test.UserId,
                new StatementLineRequest(
                    OrganisationId: test.Organisation.Id,
                    BankAccountId: bank.Id,
                    Date: new DateOnly(2026, 8, 18),
                    Description: "Locked period coded transaction",
                    Reference: "BANK-LOCKED-001",
                    Amount: -100m));

        var journal =
            await test.BankCoding.PostAndReconcileAsync(
                test.UserId,
                new BankTransactionCodingRequest(
                    OrganisationId: test.Organisation.Id,
                    StatementLineId: statement.Id,
                    TargetAccountCode: "6500",
                    Description: "Locked period coded transaction",
                    VatTreatment: VatTreatment.Exempt));

        var reconciledStatement =
            await test.Db.BankStatementLines
                .AsNoTracking()
                .SingleAsync(x => x.Id == statement.Id);

        Assert.NotNull(reconciledStatement.ReconciledAt);
        Assert.NotNull(reconciledStatement.MatchedPostedJournalLineId);

        test.Db.AccountingPeriods.Add(
            new AccountingPeriod
            {
                OrganisationId = test.Organisation.Id,
                Name = "August 2026",
                StartsOn = new DateOnly(2026, 8, 1),
                EndsOn = new DateOnly(2026, 8, 31),
                IsLocked = true
            });

        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var auditCountBefore =
            await test.Db.AuditEvents.CountAsync();

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    test.BankCoding.ReopenCodingAsync(
                        test.UserId,
                        test.Organisation.Id,
                        statement.Id));

        Assert.Contains(
            "locked",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        Assert.Equal(
            auditCountBefore,
            await test.Db.AuditEvents.CountAsync());

        var reloadedStatement =
            await test.Db.BankStatementLines
                .AsNoTracking()
                .SingleAsync(x => x.Id == statement.Id);

        Assert.Equal(
            reconciledStatement.ReconciledAt,
            reloadedStatement.ReconciledAt);

        Assert.Equal(
            reconciledStatement.MatchedPostedJournalLineId,
            reloadedStatement.MatchedPostedJournalLineId);

        Assert.Equal(
            reconciledStatement.ReconciledByUserId,
            reloadedStatement.ReconciledByUserId);

        Assert.True(
            await test.Db.PostedJournals
                .AsNoTracking()
                .AnyAsync(x => x.Id == journal.Id));
    }
}