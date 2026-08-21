using Microsoft.EntityFrameworkCore;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class PurchaseOrderServiceTests
{
    [Fact]
    public async Task CreateDraftAndApprove_CreatesPurchaseOrderWithoutPostingJournal()
    {
        await using var test =
            await AccountingTestDatabase.CreateAsync();

        var order =
            await test.PurchaseOrders.CreateDraftAsync(
                test.UserId,
                new PurchaseOrderRequest(
                    test.Organisation.Id,
                    test.Supplier.Id,
                    new DateOnly(2026, 8, 21),
                    new DateOnly(2026, 9, 20),
                    "PO-TEST-001",
                    "Office supplies order",
                    [
                        new PurchaseOrderLineRequest(
                            "Office supplies",
                            2m,
                            50m,
                            test.Account("6500").Id)
                    ]));

        Assert.Equal(
            "PO-000001",
            order.PurchaseOrderNumber);

        Assert.Equal(
            PurchaseOrderStatus.Draft,
            order.Status);

        Assert.Equal(
            100m,
            order.Subtotal);

        Assert.Equal(
            100m,
            order.Total);

        Assert.Empty(
            await test.Db.PostedJournals
                .ToListAsync());

        await test.PurchaseOrders.ApproveAsync(
            test.UserId,
            test.Organisation.Id,
            order.Id);

        var saved =
            await test.Db.PurchaseOrders
                .SingleAsync(
                    x => x.Id == order.Id);

        Assert.Equal(
            PurchaseOrderStatus.Approved,
            saved.Status);
    }
}