using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class FixedAssetDisposalAccountingTests
{
    [Fact]
    public async Task DisposeAsset_WithGain_PostsCorrectDisposalJournal()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var accumulatedDepreciation =
            new LedgerAccount
            {
                OrganisationId = test.Organisation.Id,
                Code = "1510",
                Name = "Accumulated Depreciation",
                Type = AccountType.Asset,
                IsActive = true
            };

        var depreciationExpense =
            new LedgerAccount
            {
                OrganisationId = test.Organisation.Id,
                Code = "6700",
                Name = "Depreciation Expense",
                Type = AccountType.Expense,
                IsActive = true
            };

        var gainOnDisposal =
            new LedgerAccount
            {
                OrganisationId = test.Organisation.Id,
                Code = "4200",
                Name = "Gain on Disposal of Assets",
                Type = AccountType.Revenue,
                IsActive = true
            };

        var lossOnDisposal =
            new LedgerAccount
            {
                OrganisationId = test.Organisation.Id,
                Code = "6800",
                Name = "Loss on Disposal of Assets",
                Type = AccountType.Expense,
                IsActive = true
            };

        test.Db.LedgerAccounts.AddRange(
            accumulatedDepreciation,
            depreciationExpense,
            gainOnDisposal,
            lossOnDisposal);

        await test.Db.SaveChangesAsync();

        var service =
            new FixedAssetService(
                test.Db,
                test.Access,
                test.Posting);

        var asset =
            await service.CreateAsync(
                test.UserId,
                new FixedAssetRequest(
                    OrganisationId: test.Organisation.Id,
                    Name: "Office Equipment",
                    AcquisitionDate: new DateOnly(2026, 1, 1),
                    Cost: 2400m,
                    ResidualValue: 0m,
                    UsefulLifeMonths: 12,
                    AssetAccountId: test.Account("1500").Id,
                    DepreciationExpenseAccountId: depreciationExpense.Id,
                    AccumulatedDepreciationAccountId:
                        accumulatedDepreciation.Id,
                    AcquisitionBankAccountId:
                        test.Account("1000").Id));

        await service.DepreciateThroughAsync(
            test.UserId,
            test.Organisation.Id,
            asset.Id,
            new DateOnly(2026, 4, 30));

        var disposal =
            await service.DisposeAsync(
                test.UserId,
                new FixedAssetDisposalRequest(
                    OrganisationId: test.Organisation.Id,
                    FixedAssetId: asset.Id,
                    DisposalDate: new DateOnly(2026, 5, 1),
                    Proceeds: 1900m,
                    BankAccountId: test.Account("1000").Id,
                    GainAccountId: gainOnDisposal.Id,
                    LossAccountId: lossOnDisposal.Id));

        Assert.Equal(800m, disposal.AccumulatedDepreciation);
        Assert.Equal(1600m, disposal.BookValue);
        Assert.Equal(300m, disposal.GainLoss);

        var journal =
            await test.LoadJournalAsync(
                disposal.PostedJournalId);

        var assetLine =
            journal.Lines.Single(
                x => x.LedgerAccount.Code == "1500");

        var accumulatedLine =
            journal.Lines.Single(
                x => x.LedgerAccount.Code == "1510");

        var bankLine =
            journal.Lines.Single(
                x => x.LedgerAccount.Code == "1000");

        var gainLine =
            journal.Lines.Single(
                x => x.LedgerAccount.Code == "4200");

        Assert.Equal(0m, assetLine.Debit);
        Assert.Equal(2400m, assetLine.Credit);

        Assert.Equal(800m, accumulatedLine.Debit);
        Assert.Equal(0m, accumulatedLine.Credit);

        Assert.Equal(1900m, bankLine.Debit);
        Assert.Equal(0m, bankLine.Credit);

        Assert.Equal(0m, gainLine.Debit);
        Assert.Equal(300m, gainLine.Credit);

        Assert.Equal(
            journal.Lines.Sum(x => x.Debit),
            journal.Lines.Sum(x => x.Credit));

        var savedAsset =
            await test.Db.FixedAssets
                .AsNoTracking()
                .SingleAsync(x => x.Id == asset.Id);

        Assert.False(savedAsset.IsActive);
    }
}