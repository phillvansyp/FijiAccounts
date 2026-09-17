using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class InvoiceCorrectionTests
{
    private static InvoiceCorrectionService Service(AccountingTestDatabase test) => new(test.Db, test.Access, test.SalesInvoices, test.Purchasing);
    private static SalesInvoiceRequest Sale(AccountingTestDatabase test, decimal price = 100) => new(test.Organisation.Id, test.Customer.Id, new(2026, 8, 15), new(2026, 9, 15), [new("Consulting", 1, price, VatTreatment.Standard, test.Account("4000").Id)]);
    private static SupplierBillRequest Purchase(AccountingTestDatabase test, decimal price = 40) => new(test.Organisation.Id, test.Supplier.Id, "SUP-EDIT-001", new(2026, 8, 15), new(2026, 9, 15), [new("Office costs", 1, price, VatTreatment.Standard, test.Account("6000").Id)]);

    [Fact]
    public async Task ReinstatedBillCanBeCorrectedAndHistoryStillBalances()
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var request = Purchase(t);
        var bill = await t.Purchasing.PostBillAsync(t.UserId, request);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await t.Purchasing.VoidBillAsync(t.UserId, t.Organisation.Id, bill.Id, new(2026, 8, 16), "Incorrect void");
            await t.Purchasing.ReinstateBillAsync(t.UserId, t.Organisation.Id, bill.Id, new(2026, 8, 17), "Restore bill");
        }
        var corrected = await Service(t).CorrectPurchaseAsync(t.UserId, bill.Id,
            request with { BillDate = new(2026, 8, 12), DueDate = new(2026, 8, 19) }, "Correct date");
        Assert.Equal(new DateOnly(2026, 8, 12), corrected.BillDate);
        Assert.Equal(3, await t.Db.SupplierBillVoids.CountAsync(v => v.SupplierBillId == bill.Id));
        Assert.Equal(2, await t.Db.SupplierBillReinstatements.CountAsync(v => v.SupplierBillId == bill.Id));
        Assert.Equal(BillStatus.Voided, (await t.Db.SupplierBills.AsNoTracking().SingleAsync(x => x.Id == bill.Id)).Status);
        Assert.Equal(-corrected.Total, await t.AccountBalanceAsync("2000"));
        var vat = await new VatWorkpaperService(t.Db).GetAsync(t.Organisation.Id, new(2026, 8, 1), new(2026, 8, 31));
        Assert.Equal(corrected.VatTotal, vat.InputTax);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(t).CorrectPurchaseAsync(t.UserId, bill.Id, request, "Repeat"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PurchaseDateCorrectionRequiresAllPaymentsReversed(bool reversePayment)
    {
        await using var t = await AccountingTestDatabase.CreateAsync();
        var request = Purchase(t) with { BillDate = new(2026, 2, 24), DueDate = new(2026, 3, 3) };
        var original = await t.Purchasing.PostBillAsync(t.UserId, request);
        var payment = await t.Purchasing.PayBillAsync(t.UserId,
            new SupplierPaymentRequest(t.Organisation.Id, original.Id, new(2026, 3, 17), "PAY-EDIT", original.Total, t.Account("1000").Id));
        if (reversePayment)
            await t.Purchasing.ReversePaymentAsync(t.UserId, t.Organisation.Id, payment.Id, new(2026, 3, 17), "Incorrect payment");
        var correctedRequest = request with { BillDate = new(2026, 3, 24), DueDate = new(2026, 3, 31) };
        if (!reversePayment)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(t).CorrectPurchaseAsync(t.UserId,
                original.Id, correctedRequest, "Move bill to March"));
            return;
        }
        var corrected = await Service(t).CorrectPurchaseAsync(t.UserId, original.Id, correctedRequest, "Move bill to March");
        Assert.Equal(new DateOnly(2026, 3, 24), corrected.BillDate);
        Assert.Equal(new DateOnly(2026, 3, 31), corrected.DueDate);
        Assert.Equal(original.Total, corrected.Total);
        Assert.Equal(BillStatus.Voided, (await t.Db.SupplierBills.AsNoTracking().SingleAsync(x => x.Id == original.Id)).Status);
        Assert.Equal(0m, await t.AccountBalanceAsync("1000"));
        Assert.Equal(-corrected.Total, await t.AccountBalanceAsync("2000"));
        Assert.True(await t.Db.SupplierPayments.AnyAsync(x => x.Id == payment.Id));
        Assert.True(await t.Db.SupplierPaymentReversals.AnyAsync(x => x.SupplierPaymentId == payment.Id));
    }

    [Fact]
    public async Task SalesEdit_RetainsOriginalAndPostsOnlyCorrectedNetAmounts()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var original = await test.SalesInvoices.CreateAndPostAsync(test.UserId, Sale(test));
        var originalJournal = original.PostedJournalId;
        var corrected = await Service(test).CorrectSalesAsync(test.UserId, original.Id, Sale(test, 200) with { DueDate = new(2026, 10, 15) }, "Correct unit price");
        Assert.NotEqual(original.Id, corrected.Id);
        Assert.NotEqual(original.InvoiceNumber, corrected.InvoiceNumber);
        Assert.Equal(InvoiceStatus.Voided, original.Status);
        Assert.Equal(InvoiceStatus.Posted, corrected.Status);
        Assert.Equal(100, original.Lines.Single().TransactionUnitPrice);
        Assert.Equal(200, corrected.Lines.Single().TransactionUnitPrice);
        Assert.Equal(new DateOnly(2026, 10, 15), corrected.DueDate);
        Assert.Equal(originalJournal, original.PostedJournalId);
        Assert.Equal(3, await test.Db.PostedJournals.CountAsync());
        Assert.Equal(2, await test.Db.AuditEvents.CountAsync(x => x.EventType == "InvoiceCorrection"));
        var report = await new FinancialReportService(test.Db).GetAsync(test.Organisation.Id, new(2026, 8, 1), new(2026, 8, 31));
        Assert.Equal(200, report.Balances.Where(x => x.Type == AccountType.Revenue).Sum(x => x.DisplayAmount));
        Assert.Equal(corrected.Total, report.Balances.Single(x => x.Code == "1100").DisplayAmount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(test).CorrectSalesAsync(test.UserId, original.Id, Sale(test, 300), "Duplicate save"));
    }

    [Fact]
    public async Task PurchaseEdit_RetainsSupplierReferenceAndAttachments()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var original = await test.Purchasing.PostBillAsync(test.UserId, Purchase(test));
        test.Db.SupplierBillAttachments.Add(new SupplierBillAttachment { OrganisationId = test.Organisation.Id, SupplierBillId = original.Id, FileName = "receipt.txt", ContentType = "text/plain", Content = [1, 2, 3], OriginalSize = 3, StoredSize = 3, UploadedByUserId = test.UserId });
        await test.Db.SaveChangesAsync();
        var corrected = await Service(test).CorrectPurchaseAsync(test.UserId, original.Id, Purchase(test, 80), "Correct amount");
        Assert.Equal(BillStatus.Voided, original.Status);
        Assert.Equal(BillStatus.Posted, corrected.Status);
        Assert.Equal(original.SupplierReference, corrected.SupplierReference);
        Assert.NotEqual(original.BillNumber, corrected.BillNumber);
        Assert.Equal(2, await test.Db.SupplierBillAttachments.CountAsync());
        Assert.Equal(new byte[] { 1, 2, 3 }, (await test.Db.SupplierBillAttachments.SingleAsync(x => x.SupplierBillId == corrected.Id)).Content);
        var report = await new FinancialReportService(test.Db).GetAsync(test.Organisation.Id, new(2026, 8, 1), new(2026, 8, 31));
        Assert.Equal(80, report.Balances.Where(x => x.Type == AccountType.Expense).Sum(x => x.DisplayAmount));
        Assert.Equal(corrected.Total, report.Balances.Single(x => x.Code == "2000").DisplayAmount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidCorrection_RollsBackReversalAndKeepsOriginal(bool isSales)
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = Service(test);
        Guid id;
        if (isSales)
        {
            id = (await test.SalesInvoices.CreateAndPostAsync(test.UserId, Sale(test))).Id;
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CorrectSalesAsync(test.UserId, id, Sale(test) with { Lines = [] }, "Invalid edit"));
        }
        else
        {
            id = (await test.Purchasing.PostBillAsync(test.UserId, Purchase(test))).Id;
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CorrectPurchaseAsync(test.UserId, id, Purchase(test) with { Lines = [] }, "Invalid edit"));
        }
        test.Db.ChangeTracker.Clear();
        Assert.Equal(1, await test.Db.PostedJournals.CountAsync());
        Assert.False(await test.Db.AuditEvents.AnyAsync(x => x.EventType == "InvoiceCorrection"));
        if (isSales) Assert.Equal(InvoiceStatus.Posted, (await test.Db.SalesInvoices.SingleAsync(x => x.Id == id)).Status);
        else Assert.Equal(BillStatus.Posted, (await test.Db.SupplierBills.SingleAsync(x => x.Id == id)).Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LockedOriginalPeriod_RejectsDateChangesWithoutReversing(bool isSales)
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var id = isSales ? (await test.SalesInvoices.CreateAndPostAsync(test.UserId, Sale(test))).Id : (await test.Purchasing.PostBillAsync(test.UserId, Purchase(test))).Id;
        test.Db.AccountingPeriods.Add(new AccountingPeriod { OrganisationId = test.Organisation.Id, Name = "August", StartsOn = new(2026, 8, 1), EndsOn = new(2026, 8, 31), IsLocked = true });
        await test.Db.SaveChangesAsync();
        if (isSales) await Assert.ThrowsAsync<InvalidOperationException>(() => Service(test).CorrectSalesAsync(test.UserId, id, Sale(test) with { IssueDate = new(2026, 9, 1) }, "Move date"));
        else await Assert.ThrowsAsync<InvalidOperationException>(() => Service(test).CorrectPurchaseAsync(test.UserId, id, Purchase(test) with { BillDate = new(2026, 9, 1) }, "Move date"));
        test.Db.ChangeTracker.Clear();
        Assert.Equal(1, await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task PermissionAndPaidStatus_AreEnforced()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var invoice = await test.SalesInvoices.CreateAndPostAsync(test.UserId, Sale(test));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service(test).CorrectSalesAsync("unrelated-user", invoice.Id, Sale(test), "Denied"));
        invoice.AmountPaid = invoice.Total; invoice.Status = InvoiceStatus.Paid;
        await test.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(test).CorrectSalesAsync(test.UserId, invoice.Id, Sale(test), "Paid invoice"));
        Assert.Equal(1, await test.Db.PostedJournals.CountAsync());
    }

    [Fact]
    public async Task TrackedSalesEdit_RestoresOriginalCostBeforePostingChangedQuantity()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var catalog = new ProductCatalogService(test.Db, test.Access);
        var inventory = new InventoryService(test.Db, test.Access, test.Posting);
        var item = await catalog.CreateAsync(test.UserId, new ProductItemRequest(test.Organisation.Id, "EDIT-STOCK", "Stock", "Stock", ProductKind.TrackedItem, 50, 20, VatTreatment.Standard, VatTreatment.Standard, test.Account("4000").Id, test.Account("5000").Id));
        await inventory.AdjustAsync(test.UserId, new InventoryAdjustmentRequest(test.Organisation.Id, item.Id, new(2026, 8, 1), 10, 20, 0, test.Account("1200").Id, test.Account("5000").Id, "STOCK-A", "Initial stock"));
        var request = Sale(test) with { Lines = [new("Stock sale", 2, 50, VatTreatment.Standard, test.Account("4000").Id, item.Id)] };
        var original = await test.SalesInvoices.CreateAndPostAsync(test.UserId, request);
        await inventory.AdjustAsync(test.UserId, new InventoryAdjustmentRequest(test.Organisation.Id, item.Id, new(2026, 8, 16), 10, 30, 0, test.Account("1200").Id, test.Account("5000").Id, "STOCK-B", "Later stock"));
        await Service(test).CorrectSalesAsync(test.UserId, original.Id, request with { Lines = [new("Stock sale", 3, 50, VatTreatment.Standard, test.Account("4000").Id, item.Id)] }, "Correct quantity");
        var stock = await test.Db.ProductItems.AsNoTracking().SingleAsync(x => x.Id == item.Id);
        Assert.Equal(17, stock.QuantityOnHand);
        Assert.Equal(25, stock.AverageCost);
        Assert.Equal(425, await test.AccountBalanceAsync("1200"));
    }
}
