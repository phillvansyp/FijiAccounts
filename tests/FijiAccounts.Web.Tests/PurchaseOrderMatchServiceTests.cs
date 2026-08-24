using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class PurchaseOrderMatchServiceTests
{
    [Fact]
    public async Task ExactMatch_PostsBillAndPersistsEvidence()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (order, draft) = await CreateLinkedDraftAsync(test);

        var bill = await test.Purchasing.PostDraftBillAsync(
            test.UserId, draft.Id, Request(test, draft, 2m, 50m));

        var stored = await test.Db.PurchaseOrders.AsNoTracking()
            .SingleAsync(x => x.Id == order.Id);
        Assert.Equal(PurchaseMatchStatus.Matched, stored.MatchStatus);
        Assert.Equal(0m, stored.MatchQuantityVariance);
        Assert.Equal(0m, stored.MatchPriceVariance);
        Assert.Equal(0m, stored.MatchTotalVariance);
        Assert.Equal(bill.Id, stored.SupplierBillId);
        Assert.Contains(await test.Db.AuditEvents.AsNoTracking().ToListAsync(),
            x => x.EntityId == order.Id.ToString() &&
                 x.EventType == "PurchaseThreeWayMatchCompleted");
    }

    [Fact]
    public async Task PriceVariance_BlocksUntilOwnerApprovesExactInvoiceVersion()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (order, draft) = await CreateLinkedDraftAsync(test);
        var request = Request(test, draft, 2m, 60m);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.Purchasing.PostDraftBillAsync(test.UserId, draft.Id, request));

        Assert.Contains("Three-way match blocked", error.Message);
        var blocked = await test.Db.PurchaseOrders.AsNoTracking()
            .SingleAsync(x => x.Id == order.Id);
        Assert.Equal(PurchaseMatchStatus.Exception, blocked.MatchStatus);
        Assert.Equal(20m, blocked.MatchPriceVariance);
        Assert.Equal(20m, blocked.MatchTotalVariance);
        Assert.Empty(await test.Db.SupplierBills.AsNoTracking().ToListAsync());

        var matches = new PurchaseOrderMatchService(test.Db, test.Access);
        await matches.ApproveExceptionAsync(
            test.UserId, test.Organisation.Id, order.Id,
            "Supplier confirmed an urgent market price increase.");
        var bill = await test.Purchasing.PostDraftBillAsync(
            test.UserId, draft.Id, request);

        var approved = await test.Db.PurchaseOrders.AsNoTracking()
            .SingleAsync(x => x.Id == order.Id);
        Assert.Equal(PurchaseMatchStatus.ExceptionApproved, approved.MatchStatus);
        Assert.Equal(bill.Id, approved.SupplierBillId);
        Assert.NotNull(approved.MatchApprovedAt);
    }

    [Fact]
    public async Task EditingInvoiceAfterApproval_InvalidatesApprovalAndBlocksAgain()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (order, draft) = await CreateLinkedDraftAsync(test);
        var matches = new PurchaseOrderMatchService(test.Db, test.Access);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.Purchasing.PostDraftBillAsync(
                test.UserId, draft.Id, Request(test, draft, 2m, 60m)));
        await matches.ApproveExceptionAsync(
            test.UserId, test.Organisation.Id, order.Id,
            "Approved first invoice version.");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.Purchasing.PostDraftBillAsync(
                test.UserId, draft.Id, Request(test, draft, 2m, 65m)));

        var stored = await test.Db.PurchaseOrders.AsNoTracking()
            .SingleAsync(x => x.Id == order.Id);
        Assert.Equal(PurchaseMatchStatus.Exception, stored.MatchStatus);
        Assert.Null(stored.MatchApprovedAt);
        Assert.Null(stored.MatchApprovedByUserId);
        Assert.Null(stored.MatchApprovalReason);
    }

    [Fact]
    public async Task ConfiguredTolerances_AllowVarianceWithinLimits()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var (order, draft) = await CreateLinkedDraftAsync(test);
        var matches = new PurchaseOrderMatchService(test.Db, test.Access);
        await matches.ConfigureTolerancesAsync(
            test.UserId,
            new PurchaseMatchToleranceRequest(test.Organisation.Id, 0m, 5m, 5m));

        await test.Purchasing.PostDraftBillAsync(
            test.UserId, draft.Id, Request(test, draft, 2m, 52m));

        var stored = await test.Db.PurchaseOrders.AsNoTracking()
            .SingleAsync(x => x.Id == order.Id);
        Assert.Equal(PurchaseMatchStatus.Matched, stored.MatchStatus);
        Assert.Equal(4m, stored.MatchPriceVariance);
        Assert.Equal(4m, stored.MatchTotalVariance);
    }

    private static SupplierBillRequest Request(
        AccountingTestDatabase test,
        SupplierBillDraft draft,
        decimal quantity,
        decimal unitPrice) => new(
            test.Organisation.Id,
            test.Supplier.Id,
            draft.SupplierReference,
            draft.BillDate,
            draft.DueDate,
            [new SupplierBillLineRequest(
                draft.Description,
                quantity,
                unitPrice,
                draft.VatTreatment,
                draft.ExpenseAccountId!.Value)]);

    private static async Task<(PurchaseOrder Order, SupplierBillDraft Draft)>
        CreateLinkedDraftAsync(AccountingTestDatabase test)
    {
        var order = await test.PurchaseOrders.CreateDraftAsync(
            test.UserId,
            new PurchaseOrderRequest(
                test.Organisation.Id,
                test.Supplier.Id,
                new DateOnly(2026, 8, 24),
                new DateOnly(2026, 9, 23),
                "SUP-INV-001",
                "Three-way match test",
                [new PurchaseOrderLineRequest(
                    "Office supplies",
                    2m,
                    50m,
                    test.Account("6500").Id)]));
        order.Status = PurchaseOrderStatus.Received;
        order.Lines[0].QuantityReceived = 2m;
        await test.Db.SaveChangesAsync();
        var drafts = new SupplierBillDraftService(test.Db, test.Access);
        var draft = await drafts.CreateFromPurchaseOrderAsync(
            test.UserId, test.Organisation.Id, order.Id);
        return (order, draft);
    }
}
