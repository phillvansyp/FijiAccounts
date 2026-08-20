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

        Assert.Equal(1000m, disposal.AccumulatedDepreciation);
Assert.Equal(1400m, disposal.BookValue);
Assert.Equal(500m, disposal.GainLoss);

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

        Assert.Equal(1000m, accumulatedLine.Debit);
        Assert.Equal(0m, accumulatedLine.Credit);

        Assert.Equal(1900m, bankLine.Debit);
        Assert.Equal(0m, bankLine.Credit);

        Assert.Equal(0m, gainLine.Debit);
        Assert.Equal(500m, gainLine.Credit);

        Assert.Equal(
            journal.Lines.Sum(x => x.Debit),
            journal.Lines.Sum(x => x.Credit));

        var savedAsset =
            await test.Db.FixedAssets
                .AsNoTracking()
                .SingleAsync(x => x.Id == asset.Id);

        Assert.False(savedAsset.IsActive);
    }

        [Fact]
public async Task DisposeAsset_AutomaticallyPostsDepreciationThroughDisposalDate()
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
                Name: "Disposal Depreciation Test",
                AcquisitionDate: new DateOnly(2026, 1, 1),
                Cost: 2400m,
                ResidualValue: 0m,
                UsefulLifeMonths: 12,
                AssetAccountId: test.Account("1500").Id,
                DepreciationExpenseAccountId:
                    depreciationExpense.Id,
                AccumulatedDepreciationAccountId:
                    accumulatedDepreciation.Id,
                AcquisitionBankAccountId:
                    test.Account("1000").Id));

    var disposal =
        await service.DisposeAsync(
            test.UserId,
            new FixedAssetDisposalRequest(
                OrganisationId: test.Organisation.Id,
                FixedAssetId: asset.Id,
                DisposalDate: new DateOnly(2026, 5, 31),
                Proceeds: 1500m,
                BankAccountId: test.Account("1000").Id,
                GainAccountId: gainOnDisposal.Id,
                LossAccountId: lossOnDisposal.Id));

    Assert.Equal(1000m, disposal.AccumulatedDepreciation);
    Assert.Equal(1400m, disposal.BookValue);
    Assert.Equal(100m, disposal.GainLoss);

    var depreciationEntries =
        await test.Db.FixedAssetDepreciations
            .AsNoTracking()
            .Where(x => x.FixedAssetId == asset.Id)
            .ToListAsync();

    var depreciation =
        Assert.Single(depreciationEntries);

    Assert.Equal(
        new DateOnly(2026, 5, 31),
        depreciation.ThroughDate);

    Assert.Equal(
        1000m,
        depreciation.Amount);

    var savedAsset =
        await test.Db.FixedAssets
            .AsNoTracking()
            .SingleAsync(x => x.Id == asset.Id);

    Assert.False(savedAsset.IsActive);

    await Assert.ThrowsAsync<InvalidOperationException>(
        () => service.DepreciateThroughAsync(
            test.UserId,
            test.Organisation.Id,
            asset.Id,
            new DateOnly(2026, 6, 30)));
}

    [Fact]
public async Task DisposeAsync_WhenStoredAssetAccountHasWrongType_IsRejected()
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

    var assetAccount = test.Account("1500");

    var asset =
        await service.CreateAsync(
            test.UserId,
            new FixedAssetRequest(
                OrganisationId: test.Organisation.Id,
                Name: "Disposal Account Drift Test",
                AcquisitionDate: new DateOnly(2026, 1, 1),
                Cost: 2400m,
                ResidualValue: 0m,
                UsefulLifeMonths: 12,
                AssetAccountId: assetAccount.Id,
                DepreciationExpenseAccountId: depreciationExpense.Id,
                AccumulatedDepreciationAccountId: accumulatedDepreciation.Id,
                AcquisitionBankAccountId: test.Account("1000").Id));

    assetAccount.Type = AccountType.Expense;

    await test.Db.SaveChangesAsync();

    var journalCountBefore =
        await test.Db.PostedJournals.CountAsync();

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.DisposeAsync(
                    test.UserId,
                    new FixedAssetDisposalRequest(
                        OrganisationId: test.Organisation.Id,
                        FixedAssetId: asset.Id,
                        DisposalDate: new DateOnly(2026, 5, 31),
                        Proceeds: 1500m,
                        BankAccountId: test.Account("1000").Id,
                        GainAccountId: gainOnDisposal.Id,
                        LossAccountId: lossOnDisposal.Id)));

    Assert.Contains(
        "1500",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);

    Assert.Equal(
        journalCountBefore,
        await test.Db.PostedJournals.CountAsync());

    var saved =
        await test.Db.FixedAssets
            .AsNoTracking()
            .SingleAsync(x => x.Id == asset.Id);

    Assert.True(saved.IsActive);
}

    [Fact]
public async Task DisposeAsync_WhenStoredAccumulatedDepreciationAccountHasWrongType_IsRejected()
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
                Name: "Accumulated Depreciation Drift Test",
                AcquisitionDate: new DateOnly(2026, 1, 1),
                Cost: 2400m,
                ResidualValue: 0m,
                UsefulLifeMonths: 12,
                AssetAccountId: test.Account("1500").Id,
                DepreciationExpenseAccountId: depreciationExpense.Id,
                AccumulatedDepreciationAccountId: accumulatedDepreciation.Id,
                AcquisitionBankAccountId: test.Account("1000").Id));

    accumulatedDepreciation.Type = AccountType.Liability;

    await test.Db.SaveChangesAsync();

    var journalCountBefore =
        await test.Db.PostedJournals.CountAsync();

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.DisposeAsync(
                    test.UserId,
                    new FixedAssetDisposalRequest(
                        OrganisationId: test.Organisation.Id,
                        FixedAssetId: asset.Id,
                        DisposalDate: new DateOnly(2026, 5, 31),
                        Proceeds: 1500m,
                        BankAccountId: test.Account("1000").Id,
                        GainAccountId: gainOnDisposal.Id,
                        LossAccountId: lossOnDisposal.Id)));

    Assert.Contains(
        "1510",
        ex.Message,
        StringComparison.OrdinalIgnoreCase);

    Assert.Equal(
        journalCountBefore,
        await test.Db.PostedJournals.CountAsync());

    var saved =
        await test.Db.FixedAssets
            .AsNoTracking()
            .SingleAsync(x => x.Id == asset.Id);

    Assert.True(saved.IsActive);
}

    [Fact]
    public async Task DisposeAsync_WhenDisposalDateIsInsideLockedAccountingPeriod_IsRejectedWithoutMutation()
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
                    Name: "Locked Disposal Test",
                    AcquisitionDate: new DateOnly(2026, 1, 1),
                    Cost: 2400m,
                    ResidualValue: 0m,
                    UsefulLifeMonths: 12,
                    AssetAccountId: test.Account("1500").Id,
                    DepreciationExpenseAccountId:
                        depreciationExpense.Id,
                    AccumulatedDepreciationAccountId:
                        accumulatedDepreciation.Id,
                    AcquisitionBankAccountId:
                        test.Account("1000").Id));

        test.Db.AccountingPeriods.Add(
            new AccountingPeriod
            {
                OrganisationId = test.Organisation.Id,
                Name = "May 2026",
                StartsOn = new DateOnly(2026, 5, 1),
                EndsOn = new DateOnly(2026, 5, 31),
                IsLocked = true
            });

        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var depreciationCountBefore =
            await test.Db.FixedAssetDepreciations.CountAsync();

        var disposalCountBefore =
            await test.Db.FixedAssetDisposals.CountAsync();

        var auditCountBefore =
            await test.Db.AuditEvents.CountAsync();

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.DisposeAsync(
                        test.UserId,
                        new FixedAssetDisposalRequest(
                            OrganisationId: test.Organisation.Id,
                            FixedAssetId: asset.Id,
                            DisposalDate: new DateOnly(2026, 5, 31),
                            Proceeds: 1500m,
                            BankAccountId: test.Account("1000").Id,
                            GainAccountId: gainOnDisposal.Id,
                            LossAccountId: lossOnDisposal.Id)));

        Assert.Contains(
            "locked",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        Assert.Equal(
            depreciationCountBefore,
            await test.Db.FixedAssetDepreciations.CountAsync());

        Assert.Equal(
            disposalCountBefore,
            await test.Db.FixedAssetDisposals.CountAsync());

        Assert.Equal(
            auditCountBefore,
            await test.Db.AuditEvents.CountAsync());

        var reloadedAsset =
            await test.Db.FixedAssets
                .AsNoTracking()
                .SingleAsync(x => x.Id == asset.Id);

        Assert.True(reloadedAsset.IsActive);
    }

        [Fact]
    public async Task DisposeAsync_WhenBankProceedsAreInsideCompletedReconciliation_IsRejectedWithoutMutation()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var bank =
            test.Account("1000");

        bank.BankAccountKind =
            BankAccountKind.DebitCard;

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
                    Name: "Reconciled Disposal Test",
                    AcquisitionDate: new DateOnly(2026, 1, 1),
                    Cost: 2400m,
                    ResidualValue: 0m,
                    UsefulLifeMonths: 12,
                    AssetAccountId: test.Account("1500").Id,
                    DepreciationExpenseAccountId:
                        depreciationExpense.Id,
                    AccumulatedDepreciationAccountId:
                        accumulatedDepreciation.Id));

        test.Db.BankReconciliationSessions.Add(
            new BankReconciliationSession
            {
                OrganisationId = test.Organisation.Id,
                BankAccountId = bank.Id,
                StatementStartDate = new DateOnly(2026, 5, 1),
                StatementEndDate = new DateOnly(2026, 5, 31),
                IsCompleted = true,
                CreatedByUserId = test.UserId
            });

        await test.Db.SaveChangesAsync();

        var journalCountBefore =
            await test.Db.PostedJournals.CountAsync();

        var depreciationCountBefore =
            await test.Db.FixedAssetDepreciations.CountAsync();

        var disposalCountBefore =
            await test.Db.FixedAssetDisposals.CountAsync();

        var auditCountBefore =
            await test.Db.AuditEvents.CountAsync();

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.DisposeAsync(
                        test.UserId,
                        new FixedAssetDisposalRequest(
                            OrganisationId: test.Organisation.Id,
                            FixedAssetId: asset.Id,
                            DisposalDate: new DateOnly(2026, 5, 31),
                            Proceeds: 1500m,
                            BankAccountId: bank.Id,
                            GainAccountId: gainOnDisposal.Id,
                            LossAccountId: lossOnDisposal.Id)));

        Assert.Equal(
            "A journal cannot post to a bank account inside a completed reconciliation period.",
            exception.Message);

        Assert.Equal(
            journalCountBefore,
            await test.Db.PostedJournals.CountAsync());

        Assert.Equal(
            depreciationCountBefore,
            await test.Db.FixedAssetDepreciations.CountAsync());

        Assert.Equal(
            disposalCountBefore,
            await test.Db.FixedAssetDisposals.CountAsync());

        Assert.Equal(
            auditCountBefore,
            await test.Db.AuditEvents.CountAsync());

        var reloadedAsset =
            await test.Db.FixedAssets
                .AsNoTracking()
                .SingleAsync(x => x.Id == asset.Id);

        Assert.True(reloadedAsset.IsActive);
    }
}
