using System.Text.Json;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class SupplierBillDraftServiceTests
{
    [Fact]
    public async Task SaveAsync_CreatesUpdatesAndSuppressesUnchangedAuditNoise()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new SupplierBillDraftService(test.Db, test.Access);
        var request = (await RequestAsync(test)) with { AmountsIncludeVat = true };

        var draft = await service.SaveAsync(test.UserId, request);
        await service.SaveAsync(
            test.UserId,
            request with { DraftId = draft.Id });
        await service.SaveAsync(
            test.UserId,
            request with
            {
                DraftId = draft.Id,
                SupplierReference = "SUP-UPDATED"
            });

        var stored = await test.Db.SupplierBillDrafts
            .AsNoTracking()
            .SingleAsync(x => x.Id == draft.Id);
        Assert.Equal("SUP-UPDATED", stored.SupplierReference);
        Assert.Equal(test.Supplier.Id, stored.SupplierId);
        Assert.True(stored.AmountsIncludeVat);

        var audits = await test.Db.AuditEvents
            .AsNoTracking()
            .Where(x => x.EntityId == draft.Id.ToString())
            .OrderBy(x => x.Id)
            .ToListAsync();
        Assert.Equal(
            ["SupplierBillDraftCreated", "SupplierBillDraftUpdated"],
            audits.Select(x => x.EventType));
        Assert.All(audits, audit =>
        {
            Assert.Equal(test.UserId, audit.UserId);
            Assert.Equal(nameof(SupplierBillDraft), audit.EntityType);
        });

        using var evidence = JsonDocument.Parse(audits[1].JsonData);
        Assert.Equal(
            "SUP-001",
            evidence.RootElement.GetProperty("Old").GetProperty("SupplierReference").GetString());
        Assert.Equal(
            "SUP-UPDATED",
            evidence.RootElement.GetProperty("New").GetProperty("SupplierReference").GetString());
        Assert.False(
            evidence.RootElement.GetProperty("New").GetProperty("Attachment").TryGetProperty("Content", out _));
    }

    [Fact]
    public async Task DeleteAsync_RemovesDraftClearsPurchaseOrderLinkAndAudits()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new SupplierBillDraftService(test.Db, test.Access);
        var draft = await service.SaveAsync(test.UserId, await RequestAsync(test));
        var order = await CreateReceivedPurchaseOrderAsync(test);
        order.SupplierBillDraftId = draft.Id;
        await test.Db.SaveChangesAsync();
        var initialAuditCount = await test.Db.AuditEvents.CountAsync();

        Assert.True(await service.DeleteAsync(
            test.UserId,
            test.Organisation.Id,
            draft.Id));
        Assert.False(await service.DeleteAsync(
            test.UserId,
            test.Organisation.Id,
            draft.Id));

        Assert.False(await test.Db.SupplierBillDrafts.AnyAsync(x => x.Id == draft.Id));
        Assert.Null((await test.Db.PurchaseOrders
            .AsNoTracking()
            .SingleAsync(x => x.Id == order.Id)).SupplierBillDraftId);
        Assert.Equal(initialAuditCount + 1, await test.Db.AuditEvents.CountAsync());
        Assert.Equal(
            "SupplierBillDraftDeleted",
            (await test.Db.AuditEvents.OrderBy(x => x.Id).LastAsync()).EventType);
    }

    [Fact]
    public async Task CreateFromPurchaseOrderAsync_IsAuthorizedIdempotentAndAudited()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new SupplierBillDraftService(test.Db, test.Access);
        var order = await CreateReceivedPurchaseOrderAsync(test);

        var draft = await service.CreateFromPurchaseOrderAsync(
            test.UserId,
            test.Organisation.Id,
            order.Id);
        var repeated = await service.CreateFromPurchaseOrderAsync(
            test.UserId,
            test.Organisation.Id,
            order.Id);

        Assert.Equal(draft.Id, repeated.Id);
        Assert.Equal(test.Supplier.Id, draft.SupplierId);
        Assert.Equal("PO-SUPPLIER-REF", draft.SupplierReference);
        Assert.Single(await test.Db.SupplierBillDrafts.AsNoTracking().ToListAsync());
        Assert.Equal(
            draft.Id,
            (await test.Db.PurchaseOrders
                .AsNoTracking()
                .SingleAsync(x => x.Id == order.Id)).SupplierBillDraftId);
        Assert.Single(await test.Db.AuditEvents
            .Where(x => x.EventType == "SupplierBillDraftCreatedFromPurchaseOrder")
            .ToListAsync());
    }

    [Fact]
    public async Task PostingConvertedDraft_LinksPurchaseOrderToPostedBill()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new SupplierBillDraftService(test.Db, test.Access);
        var order = await CreateReceivedPurchaseOrderAsync(test);
        var draft = await service.CreateFromPurchaseOrderAsync(
            test.UserId,
            test.Organisation.Id,
            order.Id);

        var bill = await test.Purchasing.PostDraftBillAsync(
            test.UserId,
            draft.Id,
            new SupplierBillRequest(
                test.Organisation.Id,
                test.Supplier.Id,
                draft.SupplierReference,
                draft.BillDate,
                draft.DueDate,
                [
                    new SupplierBillLineRequest(
                        draft.Description,
                        draft.Quantity,
                        draft.UnitPrice,
                        draft.VatTreatment,
                        draft.ExpenseAccountId!.Value)
                ]));

        var storedOrder = await test.Db.PurchaseOrders
            .AsNoTracking()
            .SingleAsync(x => x.Id == order.Id);
        Assert.Equal(bill.Id, storedOrder.SupplierBillId);
        Assert.Null(storedOrder.SupplierBillDraftId);
        Assert.Equal(PurchaseOrderStatus.Closed, storedOrder.Status);
        Assert.False(await test.Db.SupplierBillDrafts.AnyAsync(x => x.Id == draft.Id));
        var audit = await test.Db.AuditEvents
            .AsNoTracking()
            .SingleAsync(x =>
                x.EventType == "PurchaseOrderConvertedToSupplierBill" &&
                x.EntityId == order.Id.ToString());
        using var evidence = JsonDocument.Parse(audit.JsonData);
        Assert.Equal(
            bill.Id.ToString(),
            evidence.RootElement.GetProperty("SupplierBillId").GetString());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateFromPurchaseOrderAsync(
                test.UserId,
                test.Organisation.Id,
                order.Id));
        Assert.Contains("already been converted", error.Message);
    }

    [Fact]
    public async Task ReadOnlyMember_CannotMutateDraftsOrCreateAuditNoise()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var service = new SupplierBillDraftService(test.Db, test.Access);
        var order = await CreateReceivedPurchaseOrderAsync(test);
        var draft = new SupplierBillDraft
        {
            OrganisationId = test.Organisation.Id,
            CreatedByUserId = test.UserId
        };
        test.Db.SupplierBillDrafts.Add(draft);
        await test.Db.SaveChangesAsync();
        await test.Db.OrganisationMemberships
            .Where(x =>
                x.UserId == test.UserId &&
                x.OrganisationId == test.Organisation.Id)
            .ExecuteUpdateAsync(update =>
                update.SetProperty(x => x.Role, OrganisationRole.ReadOnly));
        var initialAuditCount = await test.Db.AuditEvents.CountAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await service.SaveAsync(test.UserId, await RequestAsync(test)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.DeleteAsync(test.UserId, test.Organisation.Id, draft.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateFromPurchaseOrderAsync(
                test.UserId,
                test.Organisation.Id,
                order.Id));

        Assert.True(await test.Db.SupplierBillDrafts.AnyAsync(x => x.Id == draft.Id));
        Assert.Equal(initialAuditCount, await test.Db.AuditEvents.CountAsync());
    }

    private static async Task<SaveSupplierBillDraftRequest> RequestAsync(
        AccountingTestDatabase test)
    {
        var dimension = await test.Db.Divisions
            .AsNoTracking()
            .Where(x => x.Branch.OrganisationId == test.Organisation.Id)
            .Select(x => new { x.BranchId, DivisionId = x.Id })
            .FirstAsync();

        return new SaveSupplierBillDraftRequest(
            test.Organisation.Id,
            null,
            test.Supplier.Id,
            dimension.BranchId,
            dimension.DivisionId,
            " SUP-001 ",
            new DateOnly(2026, 8, 23),
            new DateOnly(2026, 9, 22),
            [
                new SupplierBillDraftLineRequest(
                    " Office supplies ",
                    2m,
                    25m,
                    VatTreatment.Standard,
                    test.Account("6500").Id)
            ],
            new SupplierBillAttachmentRequest(
                "invoice.pdf",
                "application/pdf",
                4,
                [1, 2, 3, 4],
                false));
    }

    private static async Task<PurchaseOrder> CreateReceivedPurchaseOrderAsync(
        AccountingTestDatabase test)
    {
        var order = await test.PurchaseOrders.CreateDraftAsync(
            test.UserId,
            new PurchaseOrderRequest(
                test.Organisation.Id,
                test.Supplier.Id,
                new DateOnly(2026, 8, 23),
                new DateOnly(2026, 9, 22),
                "PO-SUPPLIER-REF",
                "Test conversion",
                [
                    new PurchaseOrderLineRequest(
                        "Office supplies",
                        2m,
                        25m,
                        test.Account("6500").Id)
                ]));
        order.Status = PurchaseOrderStatus.Received;
        order.Lines[0].QuantityReceived = order.Lines[0].Quantity;
        await test.Db.SaveChangesAsync();
        return order;
    }
}
