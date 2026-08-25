using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class AccountingPeriodReadinessTests
{
    [Fact]
    public async Task EmptyPeriod_IsReady()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var period =
            await CreatePeriodAsync(test);

        var service =
            new AccountingPeriodService(
                test.Db,
                test.Access);

        var readiness =
            await service.GetReadinessAsync(
                test.UserId,
                test.Organisation.Id,
                period.Id);

        Assert.True(readiness.IsReady);
        Assert.Equal(0, readiness.WarningCount);
        Assert.Equal(0, readiness.UnreconciledBankStatementLines);
        Assert.Equal(0, readiness.IncompleteBankReconciliations);
        Assert.Equal(0, readiness.DraftSalesInvoices);
        Assert.Equal(0, readiness.DraftSupplierBills);
        Assert.Equal(0, readiness.FixedAssetsRequiringDepreciation);
        Assert.Equal(0, readiness.InventoryIntegrityWarnings);
    }

    [Fact]
public async Task Inventory_NegativeHistoricalQuantity_IsReported()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var period =
        await CreatePeriodAsync(test);

    var item = new ProductItem
    {
        OrganisationId = test.Organisation.Id,
        Code = "INV-NEG-QTY",
        Name = "Negative quantity item",
        Kind = ProductKind.TrackedItem,
        SalePrice = 0m,
        PurchasePrice = 0m,
        QuantityOnHand = 0m,
        AverageCost = 0m,
        ReorderLevel = 0m,
        IsActive = true
    };

    test.Db.ProductItems.Add(item);
    await test.Db.SaveChangesAsync();

    var journal =
    await test.Posting.PostAsync(
        test.UserId,
        new JournalPostRequest(
            test.Organisation.Id,
            new DateOnly(2026, 7, 15),
            "INV-NEG-QTY",
            "Inventory readiness test",
            [
                new JournalLineInput(
                    test.Account("1000").Id,
                    "Inventory readiness test",
                    1m,
                    0m),
                new JournalLineInput(
                    test.Account("2000").Id,
                    "Inventory readiness test",
                    0m,
                    1m)
            ]));

    test.Db.InventoryMovements.Add(
        new InventoryMovement
        {
            OrganisationId = test.Organisation.Id,
            BranchId = journal.Lines.First().BranchId!.Value,
            DivisionId = journal.Lines.First().DivisionId!.Value,
            ProductItemId = item.Id,
            MovementDate = new DateOnly(2026, 7, 15),
            Type = InventoryMovementType.AdjustmentDecrease,
            QuantityChange = -1m,
            UnitCost = 10m,
            ValueChange = -10m,
            Reference = "NEG-QTY",
            PostedJournalId = journal.Id,
            PostedByUserId = test.UserId
        });

    await test.Db.SaveChangesAsync();

    var service =
        new AccountingPeriodService(
            test.Db,
            test.Access);

    var readiness =
        await service.GetReadinessAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id);

    Assert.False(readiness.IsReady);
    Assert.Equal(1, readiness.InventoryIntegrityWarnings);
}

    [Fact]
public async Task Inventory_NegativeHistoricalValue_IsReported()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var period =
        await CreatePeriodAsync(test);

    var item = new ProductItem
    {
        OrganisationId = test.Organisation.Id,
        Code = "INV-NEG-VALUE",
        Name = "Negative value item",
        Kind = ProductKind.TrackedItem,
        SalePrice = 0m,
        PurchasePrice = 0m,
        QuantityOnHand = 1m,
        AverageCost = 0m,
        ReorderLevel = 0m,
        IsActive = true
    };

    test.Db.ProductItems.Add(item);
    await test.Db.SaveChangesAsync();

    var journal =
        await test.Posting.PostAsync(
            test.UserId,
            new JournalPostRequest(
                test.Organisation.Id,
                new DateOnly(2026, 7, 15),
                "INV-NEG-VALUE",
                "Inventory readiness test",
                [
                    new JournalLineInput(
                        test.Account("1000").Id,
                        "Inventory readiness test",
                        1m,
                        0m),
                    new JournalLineInput(
                        test.Account("2000").Id,
                        "Inventory readiness test",
                        0m,
                        1m)
                ]));

    test.Db.InventoryMovements.Add(
        new InventoryMovement
        {
            OrganisationId = test.Organisation.Id,
            BranchId = journal.Lines.First().BranchId!.Value,
            DivisionId = journal.Lines.First().DivisionId!.Value,
            ProductItemId = item.Id,
            MovementDate = new DateOnly(2026, 7, 15),
            Type = InventoryMovementType.AdjustmentIncrease,
            QuantityChange = 1m,
            UnitCost = 0m,
            ValueChange = -5m,
            Reference = "NEG-VALUE",
            PostedJournalId = journal.Id,
            PostedByUserId = test.UserId
        });

    await test.Db.SaveChangesAsync();

    var service =
        new AccountingPeriodService(
            test.Db,
            test.Access);

    var readiness =
        await service.GetReadinessAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id);

    Assert.False(readiness.IsReady);
    Assert.Equal(1, readiness.InventoryIntegrityWarnings);
}

    [Fact]
public async Task Inventory_ZeroQuantityWithResidualValue_IsReported()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var period =
        await CreatePeriodAsync(test);

    var item = new ProductItem
    {
        OrganisationId = test.Organisation.Id,
        Code = "INV-RESIDUAL",
        Name = "Residual value item",
        Kind = ProductKind.TrackedItem,
        SalePrice = 0m,
        PurchasePrice = 0m,
        QuantityOnHand = 0m,
        AverageCost = 0m,
        ReorderLevel = 0m,
        IsActive = true
    };

    test.Db.ProductItems.Add(item);
    await test.Db.SaveChangesAsync();

    var journal =
        await test.Posting.PostAsync(
            test.UserId,
            new JournalPostRequest(
                test.Organisation.Id,
                new DateOnly(2026, 7, 15),
                "INV-RESIDUAL",
                "Inventory readiness test",
                [
                    new JournalLineInput(
                        test.Account("1000").Id,
                        "Inventory readiness test",
                        1m,
                        0m),
                    new JournalLineInput(
                        test.Account("2000").Id,
                        "Inventory readiness test",
                        0m,
                        1m)
                ]));

    test.Db.InventoryMovements.AddRange(
        new InventoryMovement
        {
            OrganisationId = test.Organisation.Id,
            BranchId = journal.Lines.First().BranchId!.Value,
            DivisionId = journal.Lines.First().DivisionId!.Value,
            ProductItemId = item.Id,
            MovementDate = new DateOnly(2026, 7, 10),
            Type = InventoryMovementType.AdjustmentIncrease,
            QuantityChange = 1m,
            UnitCost = 10m,
            ValueChange = 10m,
            Reference = "RESIDUAL-IN",
            PostedJournalId = journal.Id,
            PostedByUserId = test.UserId
        },
        new InventoryMovement
        {
            OrganisationId = test.Organisation.Id,
            BranchId = journal.Lines.First().BranchId!.Value,
            DivisionId = journal.Lines.First().DivisionId!.Value,
            ProductItemId = item.Id,
            MovementDate = new DateOnly(2026, 7, 20),
            Type = InventoryMovementType.AdjustmentDecrease,
            QuantityChange = -1m,
            UnitCost = 9m,
            ValueChange = -9m,
            Reference = "RESIDUAL-OUT",
            PostedJournalId = journal.Id,
            PostedByUserId = test.UserId
        });

    await test.Db.SaveChangesAsync();

    var service =
        new AccountingPeriodService(
            test.Db,
            test.Access);

    var readiness =
        await service.GetReadinessAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id);

    Assert.False(readiness.IsReady);
    Assert.Equal(1, readiness.InventoryIntegrityWarnings);
}

    [Fact]
public async Task Inventory_MovementAfterPeriodEnd_DoesNotAffectReadiness()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var period =
        await CreatePeriodAsync(test);

    var item = new ProductItem
    {
        OrganisationId = test.Organisation.Id,
        Code = "INV-CUTOFF",
        Name = "Historical cutoff item",
        Kind = ProductKind.TrackedItem,
        SalePrice = 0m,
        PurchasePrice = 0m,
        QuantityOnHand = 0m,
        AverageCost = 0m,
        ReorderLevel = 0m,
        IsActive = true
    };

    test.Db.ProductItems.Add(item);
    await test.Db.SaveChangesAsync();

    var julyJournal =
        await test.Posting.PostAsync(
            test.UserId,
            new JournalPostRequest(
                test.Organisation.Id,
                new DateOnly(2026, 7, 15),
                "INV-CUTOFF-JULY",
                "Inventory readiness test",
                [
                    new JournalLineInput(
                        test.Account("1000").Id,
                        "Inventory readiness test",
                        1m,
                        0m),
                    new JournalLineInput(
                        test.Account("2000").Id,
                        "Inventory readiness test",
                        0m,
                        1m)
                ]));

    var augustJournal =
        await test.Posting.PostAsync(
            test.UserId,
            new JournalPostRequest(
                test.Organisation.Id,
                new DateOnly(2026, 8, 1),
                "INV-CUTOFF-AUG",
                "Inventory readiness test",
                [
                    new JournalLineInput(
                        test.Account("1000").Id,
                        "Inventory readiness test",
                        1m,
                        0m),
                    new JournalLineInput(
                        test.Account("2000").Id,
                        "Inventory readiness test",
                        0m,
                        1m)
                ]));

    test.Db.InventoryMovements.AddRange(
        new InventoryMovement
        {
            OrganisationId = test.Organisation.Id,
            BranchId = julyJournal.Lines.First().BranchId!.Value,
            DivisionId = julyJournal.Lines.First().DivisionId!.Value,
            ProductItemId = item.Id,
            MovementDate = new DateOnly(2026, 7, 15),
            Type = InventoryMovementType.AdjustmentIncrease,
            QuantityChange = 1m,
            UnitCost = 10m,
            ValueChange = 10m,
            Reference = "CUTOFF-JULY",
            PostedJournalId = julyJournal.Id,
            PostedByUserId = test.UserId
        },
        new InventoryMovement
        {
            OrganisationId = test.Organisation.Id,
            BranchId = augustJournal.Lines.First().BranchId!.Value,
            DivisionId = augustJournal.Lines.First().DivisionId!.Value,
            ProductItemId = item.Id,
            MovementDate = new DateOnly(2026, 8, 1),
            Type = InventoryMovementType.AdjustmentDecrease,
            QuantityChange = -2m,
            UnitCost = 10m,
            ValueChange = -20m,
            Reference = "CUTOFF-AUG",
            PostedJournalId = augustJournal.Id,
            PostedByUserId = test.UserId
        });

    await test.Db.SaveChangesAsync();

    var service =
        new AccountingPeriodService(
            test.Db,
            test.Access);

    var readiness =
        await service.GetReadinessAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id);

    Assert.True(readiness.IsReady);
    Assert.Equal(0, readiness.InventoryIntegrityWarnings);
}

    [Fact]
    public async Task UnreconciledStatementLine_InPeriod_IsReported()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var period =
            await CreatePeriodAsync(test);

        var bank = test.Account("1000");

        await test.Reconciliation.AddStatementLineAsync(
            test.UserId,
            new StatementLineRequest(
                OrganisationId: test.Organisation.Id,
                BankAccountId: bank.Id,
                Date: new DateOnly(2026, 7, 15),
                Description: "Unreconciled test",
                Reference: "READY-001",
                Amount: 100m));

        var service =
            new AccountingPeriodService(
                test.Db,
                test.Access);

        var readiness =
            await service.GetReadinessAsync(
                test.UserId,
                test.Organisation.Id,
                period.Id);

        Assert.False(readiness.IsReady);
        Assert.Equal(1, readiness.WarningCount);
        Assert.Equal(1, readiness.UnreconciledBankStatementLines);
    }

    [Fact]
    public async Task IncompleteReconciliation_OverlappingPeriod_IsReported()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var period =
            await CreatePeriodAsync(test);

        test.Db.BankReconciliationSessions.Add(
            new BankReconciliationSession
            {
                OrganisationId = test.Organisation.Id,
                BankAccountId = test.Account("1000").Id,
                StatementStartDate = new DateOnly(2026, 6, 25),
                StatementEndDate = new DateOnly(2026, 7, 5),
                OpeningStatementBalance = 0m,
                ClosingStatementBalance = 0m,
                LedgerBalance = 0m,
                Difference = 0m,
                IsCompleted = false,
                CreatedByUserId = test.UserId
            });

        await test.Db.SaveChangesAsync();

        var service =
            new AccountingPeriodService(
                test.Db,
                test.Access);

        var readiness =
            await service.GetReadinessAsync(
                test.UserId,
                test.Organisation.Id,
                period.Id);

        Assert.False(readiness.IsReady);
        Assert.Equal(1, readiness.IncompleteBankReconciliations);
    }

    [Fact]
    public async Task DraftSalesInvoice_InPeriod_IsReported()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var period =
            await CreatePeriodAsync(test);

        test.Db.SalesInvoices.Add(
            new SalesInvoice
            {
                OrganisationId = test.Organisation.Id,
                CustomerId = test.Customer.Id,
                InvoiceNumber = "DRAFT-001",
                IssueDate = new DateOnly(2026, 7, 10),
                DueDate = new DateOnly(2026, 8, 9),
                Status = InvoiceStatus.Draft,
                Subtotal = 0m,
                VatTotal = 0m,
                Total = 0m,
                AmountPaid = 0m,
                AmountCredited = 0m,
                CreatedByUserId = test.UserId
            });

        await test.Db.SaveChangesAsync();

        var service =
            new AccountingPeriodService(
                test.Db,
                test.Access);

        var readiness =
            await service.GetReadinessAsync(
                test.UserId,
                test.Organisation.Id,
                period.Id);

        Assert.False(readiness.IsReady);
        Assert.Equal(1, readiness.DraftSalesInvoices);
    }

    [Fact]
    public async Task DraftSupplierBill_InPeriod_IsReported()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var period =
            await CreatePeriodAsync(test);

        test.Db.SupplierBillDrafts.Add(
            new SupplierBillDraft
            {
                OrganisationId = test.Organisation.Id,
                SupplierId = test.Supplier.Id,
                SupplierReference = "DRAFT-BILL-001",
                BillDate = new DateOnly(2026, 7, 20),
                DueDate = new DateOnly(2026, 8, 19),
                Description = "Draft supplier bill",
                Quantity = 1m,
                UnitPrice = 100m,
                CreatedByUserId = test.UserId
            });

        await test.Db.SaveChangesAsync();

        var service =
            new AccountingPeriodService(
                test.Db,
                test.Access);

        var readiness =
            await service.GetReadinessAsync(
                test.UserId,
                test.Organisation.Id,
                period.Id);

        Assert.False(readiness.IsReady);
        Assert.Equal(1, readiness.DraftSupplierBills);
    }

    [Fact]
    public async Task ItemsOutsidePeriod_AreNotReported()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var period =
            await CreatePeriodAsync(test);

        var bank = test.Account("1000");

        await test.Reconciliation.AddStatementLineAsync(
            test.UserId,
            new StatementLineRequest(
                OrganisationId: test.Organisation.Id,
                BankAccountId: bank.Id,
                Date: new DateOnly(2026, 8, 1),
                Description: "Outside period",
                Reference: "READY-OUTSIDE",
                Amount: 100m));

        var service =
            new AccountingPeriodService(
                test.Db,
                test.Access);

        var readiness =
            await service.GetReadinessAsync(
                test.UserId,
                test.Organisation.Id,
                period.Id);

        Assert.True(readiness.IsReady);
        Assert.Equal(0, readiness.WarningCount);
    }

    [Fact]
public async Task LockPeriod_WithOutstandingItems_RequiresAcknowledgement()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var period =
        await CreatePeriodAsync(test);

    await test.Reconciliation.AddStatementLineAsync(
        test.UserId,
        new StatementLineRequest(
            OrganisationId: test.Organisation.Id,
            BankAccountId: test.Account("1000").Id,
            Date: new DateOnly(2026, 7, 15),
            Description: "Outstanding close item",
            Reference: "CLOSE-WARN-001",
            Amount: 100m));

    var service =
        new AccountingPeriodService(
            test.Db,
            test.Access);

    var ex =
        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                service.SetLockedAsync(
                    test.UserId,
                    test.Organisation.Id,
                    period.Id,
                    true,
                    acknowledgeWarnings: false));

    Assert.Equal(
        "Review and acknowledge the outstanding period items before locking.",
        ex.Message);

    var reloaded =
        await test.Db.AccountingPeriods
            .AsNoTracking()
            .SingleAsync(x => x.Id == period.Id);

    Assert.False(reloaded.IsLocked);
    Assert.Null(reloaded.LockedAt);
    Assert.Null(reloaded.LockedByUserId);
}

[Fact]
public async Task LockPeriod_WithOutstandingItems_CanBeAcknowledged()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var period =
        await CreatePeriodAsync(test);

    await test.Reconciliation.AddStatementLineAsync(
        test.UserId,
        new StatementLineRequest(
            OrganisationId: test.Organisation.Id,
            BankAccountId: test.Account("1000").Id,
            Date: new DateOnly(2026, 7, 15),
            Description: "Acknowledged close item",
            Reference: "CLOSE-WARN-002",
            Amount: 100m));

    var service =
        new AccountingPeriodService(
            test.Db,
            test.Access);

    await service.SetLockedAsync(
        test.UserId,
        test.Organisation.Id,
        period.Id,
        true,
        acknowledgeWarnings: true);

    var reloaded =
        await test.Db.AccountingPeriods
            .AsNoTracking()
            .SingleAsync(x => x.Id == period.Id);

    Assert.True(reloaded.IsLocked);
    Assert.NotNull(reloaded.LockedAt);
    Assert.Equal(
        test.UserId,
        reloaded.LockedByUserId);

    var audit =
        await test.Db.AuditEvents
            .AsNoTracking()
            .SingleAsync(
                x =>
                    x.EntityType == nameof(AccountingPeriod) &&
                    x.EntityId == period.Id.ToString() &&
                    x.EventType == "AccountingPeriodLocked");

    Assert.Contains(
        "\"UnreconciledBankStatementLines\":1",
        audit.JsonData);

    Assert.Contains(
        "\"WarningsAcknowledged\":true",
        audit.JsonData);
}

    private static async Task<AccountingPeriod> CreatePeriodAsync(
        AccountingTestDatabase test)
    {
        var period =
            new AccountingPeriod
            {
                OrganisationId = test.Organisation.Id,
                Name = "July 2026",
                StartsOn = new DateOnly(2026, 7, 1),
                EndsOn = new DateOnly(2026, 7, 31),
                IsLocked = false
            };

        test.Db.AccountingPeriods.Add(period);

        await test.Db.SaveChangesAsync();

        return period;
    }

    [Fact]
public async Task FixedAsset_WithDepreciationDue_IsReported()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var period =
        await CreatePeriodAsync(test);

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

    var fixedAssets =
        new FixedAssetService(
            test.Db,
            test.Access,
            test.Posting);

    await fixedAssets.CreateAsync(
        test.UserId,
        new FixedAssetRequest(
            OrganisationId: test.Organisation.Id,
            Name: "July Equipment",
            AcquisitionDate: new DateOnly(2026, 1, 1),
            Cost: 12_000m,
            ResidualValue: 0m,
            UsefulLifeMonths: 12,
            AssetAccountId: test.Account("1500").Id,
            DepreciationExpenseAccountId:
                depreciationExpense.Id,
            AccumulatedDepreciationAccountId:
                accumulatedDepreciation.Id));

    var service =
        new AccountingPeriodService(
            test.Db,
            test.Access);

    var readiness =
        await service.GetReadinessAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id);

    Assert.False(readiness.IsReady);
    Assert.Equal(1, readiness.WarningCount);
    Assert.Equal(
        1,
        readiness.FixedAssetsRequiringDepreciation);
}

    [Fact]
public async Task FixedAsset_DepreciatedThroughPeriodEnd_IsNotReported()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var period =
        await CreatePeriodAsync(test);

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

    var fixedAssets =
        new FixedAssetService(
            test.Db,
            test.Access,
            test.Posting);

    var asset =
        await fixedAssets.CreateAsync(
            test.UserId,
            new FixedAssetRequest(
                OrganisationId: test.Organisation.Id,
                Name: "Depreciated Equipment",
                AcquisitionDate: new DateOnly(2026, 1, 1),
                Cost: 12_000m,
                ResidualValue: 0m,
                UsefulLifeMonths: 12,
                AssetAccountId: test.Account("1500").Id,
                DepreciationExpenseAccountId:
                    depreciationExpense.Id,
                AccumulatedDepreciationAccountId:
                    accumulatedDepreciation.Id));

    await fixedAssets.DepreciateThroughAsync(
        test.UserId,
        test.Organisation.Id,
        asset.Id,
        new DateOnly(2026, 7, 31));

    var service =
        new AccountingPeriodService(
            test.Db,
            test.Access);

    var readiness =
        await service.GetReadinessAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id);

    Assert.True(readiness.IsReady);
    Assert.Equal(
        0,
        readiness.FixedAssetsRequiringDepreciation);
}

    [Fact]
public async Task FullyDepreciatedFixedAsset_IsNotReported()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var period =
        await CreatePeriodAsync(test);

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

    var fixedAssets =
        new FixedAssetService(
            test.Db,
            test.Access,
            test.Posting);

    var asset =
        await fixedAssets.CreateAsync(
            test.UserId,
            new FixedAssetRequest(
                OrganisationId: test.Organisation.Id,
                Name: "Fully Depreciated Equipment",
                AcquisitionDate: new DateOnly(2025, 1, 1),
                Cost: 12_000m,
                ResidualValue: 2_000m,
                UsefulLifeMonths: 10,
                AssetAccountId: test.Account("1500").Id,
                DepreciationExpenseAccountId:
                    depreciationExpense.Id,
                AccumulatedDepreciationAccountId:
                    accumulatedDepreciation.Id));

    await fixedAssets.DepreciateThroughAsync(
        test.UserId,
        test.Organisation.Id,
        asset.Id,
        new DateOnly(2025, 10, 31));

    var service =
        new AccountingPeriodService(
            test.Db,
            test.Access);

    var readiness =
        await service.GetReadinessAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id);

    Assert.True(readiness.IsReady);
    Assert.Equal(
        0,
        readiness.FixedAssetsRequiringDepreciation);
}

    [Fact]
public async Task FixedAsset_AcquiredAfterPeriodEnd_IsNotReported()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var period =
        await CreatePeriodAsync(test);

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

    var fixedAssets =
        new FixedAssetService(
            test.Db,
            test.Access,
            test.Posting);

    await fixedAssets.CreateAsync(
        test.UserId,
        new FixedAssetRequest(
            OrganisationId: test.Organisation.Id,
            Name: "August Equipment",
            AcquisitionDate: new DateOnly(2026, 8, 1),
            Cost: 12_000m,
            ResidualValue: 0m,
            UsefulLifeMonths: 12,
            AssetAccountId: test.Account("1500").Id,
            DepreciationExpenseAccountId:
                depreciationExpense.Id,
            AccumulatedDepreciationAccountId:
                accumulatedDepreciation.Id));

    var service =
        new AccountingPeriodService(
            test.Db,
            test.Access);

    var readiness =
        await service.GetReadinessAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id);

    Assert.True(readiness.IsReady);
    Assert.Equal(
        0,
        readiness.FixedAssetsRequiringDepreciation);
}

    [Fact]
public async Task LockPeriod_WhenAlreadyLocked_IsIdempotent()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var period =
        await CreatePeriodAsync(test);

    var service =
        new AccountingPeriodService(
            test.Db,
            test.Access);

    await service.SetLockedAsync(
        test.UserId,
        test.Organisation.Id,
        period.Id,
        true);

    var firstState =
        await test.Db.AccountingPeriods
            .AsNoTracking()
            .SingleAsync(x => x.Id == period.Id);

    var firstLockedAt =
        firstState.LockedAt;

    var firstAuditCount =
        await test.Db.AuditEvents
            .CountAsync(
                x =>
                    x.EntityType == nameof(AccountingPeriod) &&
                    x.EntityId == period.Id.ToString() &&
                    x.EventType == "AccountingPeriodLocked");

    await service.SetLockedAsync(
        test.UserId,
        test.Organisation.Id,
        period.Id,
        true);

    var secondState =
        await test.Db.AccountingPeriods
            .AsNoTracking()
            .SingleAsync(x => x.Id == period.Id);

    var secondAuditCount =
        await test.Db.AuditEvents
            .CountAsync(
                x =>
                    x.EntityType == nameof(AccountingPeriod) &&
                    x.EntityId == period.Id.ToString() &&
                    x.EventType == "AccountingPeriodLocked");

    Assert.True(secondState.IsLocked);
    Assert.Equal(firstLockedAt, secondState.LockedAt);
    Assert.Equal(
        firstState.LockedByUserId,
        secondState.LockedByUserId);
    Assert.Equal(1, firstAuditCount);
    Assert.Equal(firstAuditCount, secondAuditCount);
}

[Fact]
public async Task UnlockPeriod_WhenAlreadyUnlocked_IsIdempotent()
{
    await using var test =
        await AccountingTestDatabase.CreateAsync();

    var period =
        await CreatePeriodAsync(test);

    var service =
        new AccountingPeriodService(
            test.Db,
            test.Access);

    var beforeAuditCount =
        await test.Db.AuditEvents
            .CountAsync(
                x =>
                    x.EntityType == nameof(AccountingPeriod) &&
                    x.EntityId == period.Id.ToString() &&
                    x.EventType == "AccountingPeriodUnlocked");

    await service.SetLockedAsync(
        test.UserId,
        test.Organisation.Id,
        period.Id,
        false);

    var reloaded =
        await test.Db.AccountingPeriods
            .AsNoTracking()
            .SingleAsync(x => x.Id == period.Id);

    var afterAuditCount =
        await test.Db.AuditEvents
            .CountAsync(
                x =>
                    x.EntityType == nameof(AccountingPeriod) &&
                    x.EntityId == period.Id.ToString() &&
                    x.EventType == "AccountingPeriodUnlocked");

    Assert.False(reloaded.IsLocked);
    Assert.Null(reloaded.LockedAt);
    Assert.Null(reloaded.LockedByUserId);
    Assert.Equal(beforeAuditCount, afterAuditCount);
}
}
