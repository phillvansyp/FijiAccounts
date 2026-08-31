using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class YearEndHandoverPackServiceTests
{
    [Fact]
    public async Task LockedPeriod_ExportsSelfVerifyingAccountingBundle()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var period = await CreatePeriodAsync(test, locked: false);
        var julyJournal = await test.Posting.PostAsync(
            test.UserId,
            new JournalPostRequest(
                test.Organisation.Id,
                new DateOnly(2026, 7, 31),
                "YEAR-END-ADJ",
                "Approved year-end adjustment",
                [
                    new JournalLineInput(
                        test.Account("6000").Id,
                        "Year-end expense",
                        250m,
                        0m),
                    new JournalLineInput(
                        test.Account("2000").Id,
                        "Year-end accrual",
                        0m,
                        250m)
                ],
                Purpose: JournalPurpose.YearEndAdjustment,
                AdjustmentPeriodId: period.Id,
                ApprovalReference: "Accountant WP-AJE-04"));
        var augustJournal = await test.Posting.PostAsync(
            test.UserId,
            new JournalPostRequest(
                test.Organisation.Id,
                new DateOnly(2026, 8, 1),
                "AFTER-YEAR-END",
                "Post-cutoff activity",
                [
                    new JournalLineInput(test.Account("1000").Id, "After cutoff", 30m, 0m),
                    new JournalLineInput(test.Account("2000").Id, "After cutoff", 0m, 30m)
                ]));
        var item = new ProductItem
        {
            OrganisationId = test.Organisation.Id,
            Code = "STOCK-CUT-OFF",
            Name = "Year-end stock",
            Kind = ProductKind.TrackedItem,
            SalePrice = 20m,
            PurchasePrice = 10m,
            QuantityOnHand = 7m,
            AverageCost = 10m,
            IsActive = true
        };
        test.Db.ProductItems.Add(item);
        test.Db.InventoryMovements.AddRange(
            new InventoryMovement
            {
                OrganisationId = test.Organisation.Id,
                BranchId = julyJournal.Lines[0].BranchId!.Value,
                DivisionId = julyJournal.Lines[0].DivisionId!.Value,
                ProductItem = item,
                MovementDate = new DateOnly(2026, 7, 31),
                Type = InventoryMovementType.OpeningBalance,
                QuantityChange = 10m,
                UnitCost = 10m,
                ValueChange = 100m,
                Reference = "STOCK-OPEN",
                PostedJournalId = julyJournal.Id,
                PostedByUserId = test.UserId
            },
            new InventoryMovement
            {
                OrganisationId = test.Organisation.Id,
                BranchId = augustJournal.Lines[0].BranchId!.Value,
                DivisionId = augustJournal.Lines[0].DivisionId!.Value,
                ProductItem = item,
                MovementDate = new DateOnly(2026, 8, 1),
                Type = InventoryMovementType.AdjustmentDecrease,
                QuantityChange = -3m,
                UnitCost = 10m,
                ValueChange = -30m,
                Reference = "STOCK-AFTER",
                PostedJournalId = augustJournal.Id,
                PostedByUserId = test.UserId
            });
        var invoice = new SalesInvoice
        {
            OrganisationId = test.Organisation.Id,
            CustomerId = test.Customer.Id,
            InvoiceNumber = "INV-YEAR-END",
            IssueDate = new DateOnly(2026, 7, 20),
            DueDate = new DateOnly(2026, 7, 25),
            Status = InvoiceStatus.Posted,
            Subtotal = 100m,
            VatTotal = 15m,
            Total = 115m,
            TransactionSubtotal = 100m,
            TransactionVatTotal = 15m,
            TransactionTotal = 115m,
            PostedJournalId = julyJournal.Id,
            CreatedByUserId = test.UserId
        };
        var receipt = new CustomerReceipt
        {
            OrganisationId = test.Organisation.Id,
            CustomerId = test.Customer.Id,
            ReceiptDate = new DateOnly(2026, 8, 1),
            Reference = "RCPT-AFTER-CUTOFF",
            Amount = 115m,
            TransactionAmount = 115m,
            BankAccountId = test.Account("1000").Id,
            PostedJournalId = augustJournal.Id,
            CreatedByUserId = test.UserId,
            Allocations =
            [
                new CustomerReceiptAllocation
                {
                    SalesInvoice = invoice,
                    Amount = 115m,
                    TransactionAmount = 115m
                }
            ]
        };
        var bill = new SupplierBill
        {
            OrganisationId = test.Organisation.Id,
            SupplierId = test.Supplier.Id,
            BillNumber = "BILL-YEAR-END",
            SupplierReference = "SUP-YEAR-END",
            BillDate = new DateOnly(2026, 7, 21),
            DueDate = new DateOnly(2026, 7, 26),
            Status = BillStatus.Posted,
            Subtotal = 200m,
            VatTotal = 30m,
            Total = 230m,
            TransactionSubtotal = 200m,
            TransactionVatTotal = 30m,
            TransactionTotal = 230m,
            PostedJournalId = julyJournal.Id,
            CreatedByUserId = test.UserId
        };
        var payment = new SupplierPayment
        {
            OrganisationId = test.Organisation.Id,
            SupplierId = test.Supplier.Id,
            SupplierBill = bill,
            PaymentDate = new DateOnly(2026, 8, 1),
            Reference = "PAY-AFTER-CUTOFF",
            Amount = 230m,
            TransactionAmount = 230m,
            AllocatedBaseAmount = 230m,
            BankAccountId = test.Account("1000").Id,
            PostedJournalId = augustJournal.Id,
            CreatedByUserId = test.UserId
        };
        var asset = new FixedAsset
        {
            OrganisationId = test.Organisation.Id,
            AssetNumber = "FA-YEAR-END",
            Name = "Year-end equipment",
            AcquisitionDate = new DateOnly(2026, 7, 1),
            Cost = 1_200m,
            ResidualValue = 0m,
            UsefulLifeMonths = 12,
            AssetAccountId = test.Account("1500").Id,
            DepreciationExpenseAccountId = test.Account("6000").Id,
            AccumulatedDepreciationAccountId = test.Account("1500").Id,
            AcquisitionJournalId = julyJournal.Id,
            CreatedByUserId = test.UserId
        };
        test.Db.AddRange(invoice, receipt, bill, payment, asset);
        await test.Db.SaveChangesAsync();
        var reviewService = new YearEndReviewService(test.Db, test.Access);
        await reviewService.StartAsync(test.UserId, test.Organisation.Id, period.Id);
        foreach (var area in Enum.GetValues<YearEndReviewArea>())
        {
            await reviewService.UpdateItemAsync(
                test.UserId,
                test.Organisation.Id,
                period.Id,
                area,
                YearEndReviewStatus.Reviewed,
                $"Reviewed {area}");
        }
        await reviewService.ApproveAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id,
            "PARTNER-YE-2026");
        var periodService = new AccountingPeriodService(test.Db, test.Access);
        await periodService.SetLockedAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id,
            true,
            acknowledgeWarnings: true);
        var service = CreateService(test);

        var pack = await service.CreateAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id);

        Assert.Equal(
            "account-island-handover-20260701-20260731.zip",
            pack.FileName);
        using var stream = new MemoryStream(pack.Content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var expected = new[]
        {
            "manifest.json",
            "trial-balance.csv",
            "profit-and-loss.csv",
            "balance-sheet.csv",
            "general-ledger.csv",
            "year-end-adjustments.csv",
            "year-end-review.csv",
            "vat-workpaper.csv",
            "bank-reconciliations.csv",
            "period-control-audit.csv",
            "aged-receivables.csv",
            "aged-payables.csv",
            "fixed-assets.csv",
            "inventory-valuation.csv"
        };
        Assert.Equal(expected, archive.Entries.Select(x => x.FullName));

        using var manifest = JsonDocument.Parse(await ReadAsync(archive, "manifest.json"));
        var root = manifest.RootElement;
        Assert.Equal(test.Organisation.Id, root.GetProperty("OrganisationId").GetGuid());
        Assert.Equal(period.Id, root.GetProperty("PeriodId").GetGuid());
        Assert.Equal("FJD", root.GetProperty("Currency").GetString());
        Assert.Equal(2, root.GetProperty("Version").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, root.GetProperty("LockedAt").ValueKind);
        Assert.Equal(
            "PARTNER-YE-2026",
            root.GetProperty("YearEndReview").GetProperty("ApprovalReference").GetString());
        Assert.Equal(
            Enum.GetValues<YearEndReviewArea>().Length,
            root.GetProperty("YearEndReview").GetProperty("ReviewedSchedules").GetInt32());

        foreach (var file in root.GetProperty("Files").EnumerateArray())
        {
            var name = file.GetProperty("Name").GetString()!;
            var content = await ReadBytesAsync(archive, name);
            var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            Assert.Equal(file.GetProperty("Sha256").GetString(), hash);
        }

        var ledger = await ReadAsync(archive, "general-ledger.csv");
        Assert.Contains("YEAR-END-ADJ", ledger);
        Assert.Contains("250.00", ledger);
        Assert.DoesNotContain("AFTER-YEAR-END", ledger);
        var adjustments = await ReadAsync(archive, "year-end-adjustments.csv");
        Assert.Contains("YEAR-END-ADJ", adjustments);
        Assert.Contains("Accountant WP-AJE-04", adjustments);
        Assert.Contains("250.00", adjustments);
        Assert.DoesNotContain("AFTER-YEAR-END", adjustments);
        var review = await ReadAsync(archive, "year-end-review.csv");
        Assert.Contains("Trial balance", review);
        Assert.Contains("PARTNER-YE-2026", review);
        Assert.Contains("Reviewed", review);
        var inventory = await ReadAsync(archive, "inventory-valuation.csv");
        Assert.Contains(
            "\"STOCK-CUT-OFF\",\"Year-end stock\",\"10.00\",\"10.00\",\"100.00\"",
            inventory);
        Assert.DoesNotContain("70.00", inventory);
        var receivables = await ReadAsync(archive, "aged-receivables.csv");
        Assert.Contains("INV-YEAR-END", receivables);
        Assert.Contains("\"115.00\",\"0.00\",\"0.00\",\"115.00\"", receivables);
        Assert.DoesNotContain("RCPT-AFTER-CUTOFF", receivables);
        var payables = await ReadAsync(archive, "aged-payables.csv");
        Assert.Contains("BILL-YEAR-END", payables);
        Assert.Contains("\"230.00\",\"0.00\",\"0.00\",\"230.00\"", payables);
        Assert.DoesNotContain("PAY-AFTER-CUTOFF", payables);
        var assets = await ReadAsync(archive, "fixed-assets.csv");
        Assert.Contains("FA-YEAR-END", assets);
        Assert.Contains("\"1200.00\",\"0.00\",\"12\",\"0.00\",\"1200.00\"", assets);
        var controlAudit = await ReadAsync(archive, "period-control-audit.csv");
        Assert.Contains("AccountingPeriodLocked", controlAudit);
        Assert.True(await test.Db.AuditEvents.AnyAsync(x =>
            x.OrganisationId == test.Organisation.Id &&
            x.EntityId == period.Id.ToString() &&
            x.EventType == "YearEndHandoverPackExported"));
    }

    [Fact]
    public async Task UnlockedPeriod_CannotBeExportedAsFinalPack()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var period = await CreatePeriodAsync(test, locked: false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(test).CreateAsync(
                test.UserId,
                test.Organisation.Id,
                period.Id));

        Assert.Equal(
            "Lock the accounting period before exporting its final handover pack.",
            error.Message);
    }

    [Fact]
    public async Task NonManager_CannotExportHandoverPack()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var period = await CreatePeriodAsync(test, locked: true);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateService(test).CreateAsync(
                "not-a-member",
                test.Organisation.Id,
                period.Id));
    }

    private static YearEndHandoverPackService CreateService(
        AccountingTestDatabase test) =>
        new(
            test.Db,
            test.Access,
            new FinancialReportService(test.Db),
            new VatWorkpaperService(test.Db));

    private static async Task<AccountingPeriod> CreatePeriodAsync(
        AccountingTestDatabase test,
        bool locked)
    {
        var period = new AccountingPeriod
        {
            OrganisationId = test.Organisation.Id,
            Name = "July 2026",
            StartsOn = new DateOnly(2026, 7, 1),
            EndsOn = new DateOnly(2026, 7, 31),
            IsLocked = locked,
            LockedAt = locked ? DateTimeOffset.UtcNow : null,
            LockedByUserId = locked ? test.UserId : null
        };
        test.Db.AccountingPeriods.Add(period);
        await test.Db.SaveChangesAsync();
        return period;
    }

    private static async Task<string> ReadAsync(
        ZipArchive archive,
        string name) =>
        Encoding.UTF8.GetString(await ReadBytesAsync(archive, name));

    private static async Task<byte[]> ReadBytesAsync(
        ZipArchive archive,
        string name)
    {
        var entry = archive.GetEntry(name)
            ?? throw new InvalidOperationException($"Missing ZIP entry {name}.");
        await using var source = entry.Open();
        using var output = new MemoryStream();
        await source.CopyToAsync(output);
        return output.ToArray();
    }
}
