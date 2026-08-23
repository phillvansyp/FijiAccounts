using System.Text.Json;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class PurchaseOrderServiceTests
{
    [Fact]
    public async Task CreateAndLifecycle_AreAuditedWithoutPostingJournal()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var order = await CreateAsync(test);

        Assert.Equal("PO-000001", order.PurchaseOrderNumber);
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
        Assert.Equal(100m, order.Total);
        Assert.Empty(await test.Db.PostedJournals.ToListAsync());

        await test.PurchaseOrders.ApproveAsync(test.UserId, test.Organisation.Id, order.Id);
        await test.PurchaseOrders.MarkSentAsync(test.UserId, test.Organisation.Id, order.Id);
        var lineId = order.Lines.Single().Id;
        await test.PurchaseOrders.ReceiveAsync(
            test.UserId, test.Organisation.Id, order.Id,
            new Dictionary<Guid, decimal> { [lineId] = 1m });
        await test.PurchaseOrders.ReceiveAsync(
            test.UserId, test.Organisation.Id, order.Id,
            new Dictionary<Guid, decimal> { [lineId] = 1m });

        var saved = await ReloadAsync(test, order.Id);
        Assert.Equal(PurchaseOrderStatus.Received, saved.Status);
        Assert.Equal(2m, saved.Lines.Single().QuantityReceived);
        Assert.Empty(await test.Db.PostedJournals.ToListAsync());

        var audits = await AuditsAsync(test, order.Id);
        Assert.Equal(
            [
                "PurchaseOrderCreated",
                "PurchaseOrderApproved",
                "PurchaseOrderMarkedSent",
                "PurchaseOrderReceiptRecorded",
                "PurchaseOrderReceiptRecorded"
            ],
            audits.Select(x => x.EventType));
        Assert.All(audits, audit =>
        {
            Assert.Equal(test.UserId, audit.UserId);
            Assert.Equal(nameof(PurchaseOrder), audit.EntityType);
        });

        using var approval = JsonDocument.Parse(audits[1].JsonData);
        Assert.Equal("Draft", approval.RootElement.GetProperty("Old").GetProperty("Status").GetString());
        Assert.Equal("Approved", approval.RootElement.GetProperty("New").GetProperty("Status").GetString());

        using var firstReceipt = JsonDocument.Parse(audits[3].JsonData);
        Assert.Equal(
            0m,
            firstReceipt.RootElement.GetProperty("Old").GetProperty("Lines")[0]
                .GetProperty("QuantityReceived").GetDecimal());
        Assert.Equal(
            1m,
            firstReceipt.RootElement.GetProperty("New").GetProperty("Lines")[0]
                .GetProperty("QuantityReceived").GetDecimal());
    }

    [Fact]
    public async Task CreateAsync_RejectsInvalidAndCrossTenantReferencesWithoutAudit()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var otherOrganisation = new Organisation
        {
            LegalName = "Other Organisation Limited",
            Kind = OrganisationKind.Business
        };
        var otherAccount = new LedgerAccount
        {
            OrganisationId = otherOrganisation.Id,
            Organisation = otherOrganisation,
            Code = "6500",
            Name = "Other expenses",
            Type = AccountType.Expense
        };
        var otherProduct = new ProductItem
        {
            OrganisationId = otherOrganisation.Id,
            Organisation = otherOrganisation,
            Code = "OTHER",
            Name = "Other product",
            Kind = ProductKind.NonTrackedItem
        };
        test.Db.AddRange(otherOrganisation, otherAccount, otherProduct);
        await test.Db.SaveChangesAsync();

        var valid = Request(test);
        var invalidRequests = new[]
        {
            valid with { ExpectedDate = valid.OrderDate.AddDays(-1) },
            valid with { SupplierReference = new string('R', 81) },
            valid with { Notes = new string('N', 501) },
            valid with { Lines = [] },
            valid with { Lines = [valid.Lines[0] with { Description = " " }] },
            valid with { Lines = [valid.Lines[0] with { Quantity = 0 }] },
            valid with { Lines = [valid.Lines[0] with { UnitPrice = -1 }] },
            valid with { Lines = [valid.Lines[0] with { ExpenseAccountId = otherAccount.Id }] },
            valid with { Lines = [valid.Lines[0] with { ProductItemId = otherProduct.Id }] }
        };

        foreach (var request in invalidRequests)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                test.PurchaseOrders.CreateDraftAsync(test.UserId, request));
        }

        Assert.Empty(await test.Db.PurchaseOrders.AsNoTracking().ToListAsync());
        Assert.Empty(await test.Db.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ReadOnlyMember_CannotMutateOrdersOrCreateAuditNoise()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var order = await CreateAsync(test);
        await test.Db.OrganisationMemberships
            .Where(x => x.UserId == test.UserId && x.OrganisationId == test.Organisation.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Role, OrganisationRole.ReadOnly));
        var initialAuditCount = await test.Db.AuditEvents.CountAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            test.PurchaseOrders.CreateDraftAsync(test.UserId, Request(test)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            test.PurchaseOrders.ApproveAsync(test.UserId, test.Organisation.Id, order.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            test.PurchaseOrders.MarkSentAsync(test.UserId, test.Organisation.Id, order.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            test.PurchaseOrders.CancelAsync(test.UserId, test.Organisation.Id, order.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            test.PurchaseOrders.ReceiveAsync(
                test.UserId, test.Organisation.Id, order.Id,
                new Dictionary<Guid, decimal>()));

        Assert.Equal(initialAuditCount, await test.Db.AuditEvents.CountAsync());
        Assert.Equal(PurchaseOrderStatus.Draft, (await ReloadAsync(test, order.Id)).Status);
    }

    [Fact]
    public async Task AuthorizedOtherTenant_CannotTargetOrder()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var order = await CreateAsync(test);
        var otherOrganisation = new Organisation
        {
            LegalName = "Other Organisation Limited",
            Kind = OrganisationKind.Business
        };
        test.Db.Organisations.Add(otherOrganisation);
        test.Db.OrganisationMemberships.Add(new OrganisationMembership
        {
            OrganisationId = otherOrganisation.Id,
            Organisation = otherOrganisation,
            UserId = test.UserId,
            Role = OrganisationRole.Owner
        });
        await test.Db.SaveChangesAsync();
        var initialAuditCount = await test.Db.AuditEvents.CountAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.PurchaseOrders.ApproveAsync(test.UserId, otherOrganisation.Id, order.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.PurchaseOrders.CancelAsync(test.UserId, otherOrganisation.Id, order.Id));

        Assert.Equal(initialAuditCount, await test.Db.AuditEvents.CountAsync());
        Assert.Equal(PurchaseOrderStatus.Draft, (await ReloadAsync(test, order.Id)).Status);
    }

    [Fact]
    public async Task CancelAsync_IsIdempotentAndAuditedOnce()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var order = await CreateAsync(test);

        await test.PurchaseOrders.CancelAsync(test.UserId, test.Organisation.Id, order.Id);
        await test.PurchaseOrders.CancelAsync(test.UserId, test.Organisation.Id, order.Id);

        Assert.Equal(PurchaseOrderStatus.Cancelled, (await ReloadAsync(test, order.Id)).Status);
        Assert.Single(
            await AuditsAsync(test, order.Id),
            x => x.EventType == "PurchaseOrderCancelled");
    }

    [Fact]
    public async Task ReceiveAsync_SuppressesNoOpAndRejectsInvalidLinesBeforeMutation()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var order = await CreateAsync(test);
        await test.PurchaseOrders.ApproveAsync(test.UserId, test.Organisation.Id, order.Id);
        await test.PurchaseOrders.MarkSentAsync(test.UserId, test.Organisation.Id, order.Id);
        var line = order.Lines.Single();
        var initialAuditCount = await test.Db.AuditEvents.CountAsync();

        await test.PurchaseOrders.ReceiveAsync(
            test.UserId, test.Organisation.Id, order.Id,
            new Dictionary<Guid, decimal> { [line.Id] = 0m });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.PurchaseOrders.ReceiveAsync(
                test.UserId, test.Organisation.Id, order.Id,
                new Dictionary<Guid, decimal> { [Guid.NewGuid()] = 1m }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            test.PurchaseOrders.ReceiveAsync(
                test.UserId, test.Organisation.Id, order.Id,
                new Dictionary<Guid, decimal> { [line.Id] = 3m }));

        var saved = await ReloadAsync(test, order.Id);
        Assert.Equal(PurchaseOrderStatus.Sent, saved.Status);
        Assert.Equal(0m, saved.Lines.Single().QuantityReceived);
        Assert.Equal(initialAuditCount, await test.Db.AuditEvents.CountAsync());
    }

    private static Task<PurchaseOrder> CreateAsync(AccountingTestDatabase test) =>
        test.PurchaseOrders.CreateDraftAsync(test.UserId, Request(test));

    private static PurchaseOrderRequest Request(AccountingTestDatabase test) =>
        new(
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
            ]);

    private static Task<PurchaseOrder> ReloadAsync(AccountingTestDatabase test, Guid orderId) =>
        test.Db.PurchaseOrders
            .AsNoTracking()
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == orderId);

    private static Task<List<AuditEvent>> AuditsAsync(AccountingTestDatabase test, Guid orderId) =>
        test.Db.AuditEvents
            .AsNoTracking()
            .Where(x => x.EntityId == orderId.ToString())
            .OrderBy(x => x.Id)
            .ToListAsync();
}
