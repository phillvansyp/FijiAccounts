using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class FixedAssetAccountingTests
{
    [Fact]
    public async Task CreateFixedAsset_WithBankAccount_PostsAcquisitionJournal()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var service =
            new FixedAssetService(
                test.Db,
                test.Access,
                test.Posting);

        var bank =
            test.Account("1000");

        var assetAccount =
            test.Account("1500");

        var accumulatedDepreciation =
            new LedgerAccount
            {
                OrganisationId = test.Organisation.Id,
                Code = "1510",
                Name = "Accumulated Depreciation",
                Type = AccountType.Asset,
                IsSystemAccount = false,
                IsActive = true
            };

        test.Db.LedgerAccounts.Add(accumulatedDepreciation);
        await test.Db.SaveChangesAsync();

        var asset =
            await service.CreateAsync(
                test.UserId,
                new FixedAssetRequest(
                    OrganisationId: test.Organisation.Id,
                    AssetNumber: "FA-001",
                    Name: "Office Computer",
                    AcquisitionDate: new DateOnly(2026, 8, 18),
                    Cost: 2400m,
                    ResidualValue: 400m,
                    UsefulLifeMonths: 36,
                    AssetAccountId: assetAccount.Id,
                    DepreciationExpenseAccountId:
                        test.Account("6900").Id,
                    AccumulatedDepreciationAccountId:
                        accumulatedDepreciation.Id,
                    AcquisitionBankAccountId: bank.Id));

        var saved =
            await test.Db.FixedAssets
                .AsNoTracking()
                .SingleAsync(x => x.Id == asset.Id);

        Assert.Equal(
            bank.Id,
            saved.AcquisitionBankAccountId);

        Assert.NotNull(
            saved.AcquisitionJournalId);

        var journal =
            await test.LoadJournalAsync(
                saved.AcquisitionJournalId!.Value);

        var fixedAssetLine =
            journal.Lines.Single(
                x => x.LedgerAccount.Code == "1500");

        var bankLine =
            journal.Lines.Single(
                x => x.LedgerAccount.Code == "1000");

        Assert.Equal(
            2400m,
            fixedAssetLine.Debit);

        Assert.Equal(
            0m,
            fixedAssetLine.Credit);

        Assert.Equal(
            0m,
            bankLine.Debit);

        Assert.Equal(
            2400m,
            bankLine.Credit);

        Assert.Equal(
            journal.Lines.Sum(x => x.Debit),
            journal.Lines.Sum(x => x.Credit));

        Assert.Equal(
            2400m,
            await test.AccountBalanceAsync("1500"));

        Assert.Equal(
            -2400m,
            await test.AccountBalanceAsync("1000"));
    }

    [Fact]
    public async Task DepreciateThrough_PostsCorrectStraightLineDepreciation()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var fixedAssets =
            new FixedAssetService(
                test.Db,
                test.Access,
                test.Posting);

        var accumulatedDepreciation =
            new LedgerAccount
            {
                OrganisationId = test.Organisation.Id,
                Code = "1510",
                Name = "Accumulated Depreciation",
                Type = AccountType.Asset,
                IsSystemAccount = false,
                IsActive = true
            };

        var depreciationExpense =
            new LedgerAccount
            {
                OrganisationId = test.Organisation.Id,
                Code = "6700",
                Name = "Depreciation Expense",
                Type = AccountType.Expense,
                IsSystemAccount = false,
                IsActive = true
            };

        test.Db.LedgerAccounts.AddRange(
            accumulatedDepreciation,
            depreciationExpense);

        await test.Db.SaveChangesAsync();

        var asset =
            await fixedAssets.CreateAsync(
                test.UserId,
                new FixedAssetRequest(
                    OrganisationId: test.Organisation.Id,
                    AssetNumber: "FA-DEP-001",
                    Name: "Test Equipment",
                    AcquisitionDate: new DateOnly(2026, 1, 1),
                    Cost: 12_000m,
                    ResidualValue: 0m,
                    UsefulLifeMonths: 12,
                    AssetAccountId: test.Account("1500").Id,
                    DepreciationExpenseAccountId:
                        depreciationExpense.Id,
                    AccumulatedDepreciationAccountId:
                        accumulatedDepreciation.Id));

        var depreciation =
            await fixedAssets.DepreciateThroughAsync(
                test.UserId,
                test.Organisation.Id,
                asset.Id,
                new DateOnly(2026, 3, 31));

        Assert.Equal(
            3_000m,
            depreciation.Amount);

        var journal =
            await test.LoadJournalAsync(
                depreciation.PostedJournalId);

        var expenseLine =
            journal.Lines.Single(
                x =>
                    x.LedgerAccountId ==
                    depreciationExpense.Id);

        var accumulatedLine =
            journal.Lines.Single(
                x =>
                    x.LedgerAccountId ==
                    accumulatedDepreciation.Id);

        Assert.Equal(
            3_000m,
            expenseLine.Debit);

        Assert.Equal(
            0m,
            expenseLine.Credit);

        Assert.Equal(
            0m,
            accumulatedLine.Debit);

        Assert.Equal(
            3_000m,
            accumulatedLine.Credit);

        Assert.Equal(
            journal.Lines.Sum(x => x.Debit),
            journal.Lines.Sum(x => x.Credit));

        var storedEntries =
            await test.Db.FixedAssetDepreciations
                .AsNoTracking()
                .Where(x => x.FixedAssetId == asset.Id)
                .ToListAsync();

        Assert.Single(storedEntries);

        Assert.Equal(
            3_000m,
            storedEntries.Single().Amount);

        Assert.Equal(
            3_000m,
            await test.AccountBalanceAsync("6700"));

        Assert.Equal(
            -3_000m,
            await test.AccountBalanceAsync("1510"));
    }

    [Fact]
    public async Task DepreciateThrough_OnlyPostsAdditionalDepreciation()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var fixedAssets =
            new FixedAssetService(
                test.Db,
                test.Access,
                test.Posting);

        var accumulatedDepreciation =
            new LedgerAccount
            {
                OrganisationId = test.Organisation.Id,
                Code = "1510",
                Name = "Accumulated Depreciation",
                Type = AccountType.Asset,
                IsSystemAccount = false,
                IsActive = true
            };

        var depreciationExpense =
            new LedgerAccount
            {
                OrganisationId = test.Organisation.Id,
                Code = "6700",
                Name = "Depreciation Expense",
                Type = AccountType.Expense,
                IsSystemAccount = false,
                IsActive = true
            };

        test.Db.LedgerAccounts.AddRange(
            accumulatedDepreciation,
            depreciationExpense);

        await test.Db.SaveChangesAsync();

        var asset =
            await fixedAssets.CreateAsync(
                test.UserId,
                new FixedAssetRequest(
                    OrganisationId: test.Organisation.Id,
                    AssetNumber: "FA-DEP-002",
                    Name: "Second Test Equipment",
                    AcquisitionDate: new DateOnly(2026, 1, 1),
                    Cost: 12_000m,
                    ResidualValue: 0m,
                    UsefulLifeMonths: 12,
                    AssetAccountId: test.Account("1500").Id,
                    DepreciationExpenseAccountId:
                        depreciationExpense.Id,
                    AccumulatedDepreciationAccountId:
                        accumulatedDepreciation.Id));

        var march =
            await fixedAssets.DepreciateThroughAsync(
                test.UserId,
                test.Organisation.Id,
                asset.Id,
                new DateOnly(2026, 3, 31));

        Assert.Equal(
            3_000m,
            march.Amount);

        var june =
            await fixedAssets.DepreciateThroughAsync(
                test.UserId,
                test.Organisation.Id,
                asset.Id,
                new DateOnly(2026, 6, 30));

        Assert.Equal(
            3_000m,
            june.Amount);

        var entries =
            await test.Db.FixedAssetDepreciations
                .AsNoTracking()
                .Where(x => x.FixedAssetId == asset.Id)
                .ToListAsync();

        Assert.Equal(
            2,
            entries.Count);

        Assert.Equal(
            6_000m,
            entries.Sum(x => x.Amount));

        Assert.Equal(
            6_000m,
            await test.AccountBalanceAsync("6700"));

        Assert.Equal(
            -6_000m,
            await test.AccountBalanceAsync("1510"));
    }

    [Fact]
    public async Task DepreciateThrough_CannotExceedUsefulLife()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var fixedAssets =
            new FixedAssetService(
                test.Db,
                test.Access,
                test.Posting);

        var accumulatedDepreciation =
            new LedgerAccount
            {
                OrganisationId = test.Organisation.Id,
                Code = "1510",
                Name = "Accumulated Depreciation",
                Type = AccountType.Asset,
                IsSystemAccount = false,
                IsActive = true
            };

        var depreciationExpense =
            new LedgerAccount
            {
                OrganisationId = test.Organisation.Id,
                Code = "6700",
                Name = "Depreciation Expense",
                Type = AccountType.Expense,
                IsSystemAccount = false,
                IsActive = true
            };

        test.Db.LedgerAccounts.AddRange(
            accumulatedDepreciation,
            depreciationExpense);

        await test.Db.SaveChangesAsync();

        var asset =
            await fixedAssets.CreateAsync(
                test.UserId,
                new FixedAssetRequest(
                    OrganisationId: test.Organisation.Id,
                    AssetNumber: "FA-DEP-003",
                    Name: "Residual Value Asset",
                    AcquisitionDate: new DateOnly(2026, 1, 1),
                    Cost: 12_000m,
                    ResidualValue: 2_000m,
                    UsefulLifeMonths: 10,
                    AssetAccountId: test.Account("1500").Id,
                    DepreciationExpenseAccountId:
                        depreciationExpense.Id,
                    AccumulatedDepreciationAccountId:
                        accumulatedDepreciation.Id));

        var depreciation =
            await fixedAssets.DepreciateThroughAsync(
                test.UserId,
                test.Organisation.Id,
                asset.Id,
                new DateOnly(2028, 12, 31));

        Assert.Equal(
            10_000m,
            depreciation.Amount);

        Assert.Equal(
            10_000m,
            await test.AccountBalanceAsync("6700"));

        Assert.Equal(
            -10_000m,
            await test.AccountBalanceAsync("1510"));
    }
}