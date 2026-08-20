using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class ChartOfAccountsAccountingTests
{
    [Fact]
    public async Task CreateAsync_NormalizesCodeAndPersistsAccount()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new ChartOfAccountsService(
                test.Db,
                test.Access);

        var account =
            await service.CreateAsync(
                test.UserId,
                new LedgerAccountRequest(
                    OrganisationId: test.Organisation.Id,
                    Code: "  4999  ",
                    Name: "  Other Revenue  ",
                    Type: AccountType.Revenue,
                    IsBankAccount: false,
                    BankAccountKind: BankAccountKind.Bank,
                    BankAccountNumber: null));

        Assert.Equal("4999", account.Code);
        Assert.Equal("Other Revenue", account.Name);
        Assert.Equal(AccountType.Revenue, account.Type);
        Assert.True(account.IsActive);
        Assert.False(account.IsBankAccount);

        var stored =
            await test.Db.LedgerAccounts
                .AsNoTracking()
                .SingleAsync(x => x.Id == account.Id);

        Assert.Equal("4999", stored.Code);
        Assert.Equal("Other Revenue", stored.Name);
    }

    [Fact]
    public async Task CreateAsync_WhenCodeAlreadyExists_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new ChartOfAccountsService(
                test.Db,
                test.Access);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new LedgerAccountRequest(
                            OrganisationId: test.Organisation.Id,
                            Code: "4000",
                            Name: "Duplicate Revenue",
                            Type: AccountType.Revenue,
                            IsBankAccount: false,
                            BankAccountKind: BankAccountKind.Bank,
                            BankAccountNumber: null)));

        Assert.Contains(
            "already exists",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_WhenBankAccountIsNotAsset_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new ChartOfAccountsService(
                test.Db,
                test.Access);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.CreateAsync(
                        test.UserId,
                        new LedgerAccountRequest(
                            OrganisationId: test.Organisation.Id,
                            Code: "1010",
                            Name: "Invalid Bank",
                            Type: AccountType.Expense,
                            IsBankAccount: true,
                            BankAccountKind: BankAccountKind.Bank,
                            BankAccountNumber: "12345")));

        Assert.Contains(
            "Asset",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_CreditCardRequiresLiabilityType()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new ChartOfAccountsService(
                test.Db,
                test.Access);

        var account =
            await service.CreateAsync(
                test.UserId,
                new LedgerAccountRequest(
                    OrganisationId: test.Organisation.Id,
                    Code: "2050",
                    Name: "Business Credit Card",
                    Type: AccountType.Liability,
                    IsBankAccount: true,
                    BankAccountKind: BankAccountKind.CreditCard,
                    BankAccountNumber: "CC-001"));

        Assert.True(account.IsBankAccount);
        Assert.Equal(
            BankAccountKind.CreditCard,
            account.BankAccountKind);

        Assert.Equal(
            AccountType.Liability,
            account.Type);
    }

    [Fact]
    public async Task UpdateAsync_WhenSystemAccount_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var systemAccount =
            test.Db.LedgerAccounts.Local
                .First(x => x.IsSystemAccount);

        var service =
            new ChartOfAccountsService(
                test.Db,
                test.Access);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.UpdateAsync(
                        test.UserId,
                        new UpdateLedgerAccountRequest(
                            OrganisationId: test.Organisation.Id,
                            AccountId: systemAccount.Id,
                            Name: "Changed",
                            Type: systemAccount.Type,
                            IsBankAccount: systemAccount.IsBankAccount,
                            BankAccountKind: systemAccount.BankAccountKind,
                            BankAccountNumber:
                                systemAccount.BankAccountNumber)));

        Assert.Contains(
            "system accounts",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetActiveAsync_WhenSystemAccountArchiveRequested_IsRejected()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var systemAccount =
            test.Db.LedgerAccounts.Local
                .First(x => x.IsSystemAccount);

        var service =
            new ChartOfAccountsService(
                test.Db,
                test.Access);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.SetActiveAsync(
                        test.UserId,
                        test.Organisation.Id,
                        systemAccount.Id,
                        false));

        Assert.Contains(
            "cannot be archived",
            ex.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetActiveAsync_ArchivesAndReactivatesCustomAccount()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new ChartOfAccountsService(
                test.Db,
                test.Access);

        var account =
            await service.CreateAsync(
                test.UserId,
                new LedgerAccountRequest(
                    OrganisationId: test.Organisation.Id,
                    Code: "4998",
                    Name: "Temporary Revenue",
                    Type: AccountType.Revenue,
                    IsBankAccount: false,
                    BankAccountKind: BankAccountKind.Bank,
                    BankAccountNumber: null));

        await service.SetActiveAsync(
            test.UserId,
            test.Organisation.Id,
            account.Id,
            false);

        var archived =
            await test.Db.LedgerAccounts
                .AsNoTracking()
                .SingleAsync(x => x.Id == account.Id);

        Assert.False(archived.IsActive);

        await service.SetActiveAsync(
            test.UserId,
            test.Organisation.Id,
            account.Id,
            true);

        var reactivated =
            await test.Db.LedgerAccounts
                .AsNoTracking()
                .SingleAsync(x => x.Id == account.Id);

        Assert.True(reactivated.IsActive);
    }

    [Fact]
    public async Task CreateAndUpdateAsync_WriteAuditEvents()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new ChartOfAccountsService(
                test.Db,
                test.Access);

        var account =
            await service.CreateAsync(
                test.UserId,
                new LedgerAccountRequest(
                    OrganisationId: test.Organisation.Id,
                    Code: "4997",
                    Name: "Audit Revenue",
                    Type: AccountType.Revenue,
                    IsBankAccount: false,
                    BankAccountKind: BankAccountKind.Bank,
                    BankAccountNumber: null));

        await service.UpdateAsync(
            test.UserId,
            new UpdateLedgerAccountRequest(
                OrganisationId: test.Organisation.Id,
                AccountId: account.Id,
                Name: "Updated Audit Revenue",
                Type: AccountType.Revenue,
                IsBankAccount: false,
                BankAccountKind: BankAccountKind.Bank,
                BankAccountNumber: null));

        var events =
            await test.Db.AuditEvents
                .AsNoTracking()
                .Where(x =>
                    x.EntityType == nameof(LedgerAccount) &&
                    x.EntityId == account.Id.ToString())
                .ToListAsync();

        Assert.Contains(
            events,
            x => x.EventType == "LedgerAccountCreated");

        Assert.Contains(
            events,
            x => x.EventType == "LedgerAccountUpdated");
    }
}