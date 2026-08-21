using Microsoft.EntityFrameworkCore;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record PurchaseOrderRequest(
    Guid OrganisationId,
    Guid SupplierId,
    DateOnly OrderDate,
    DateOnly? ExpectedDate,
    string SupplierReference,
    string Notes,
    IReadOnlyList<PurchaseOrderLineRequest> Lines);

public sealed record PurchaseOrderLineRequest(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    Guid ExpenseAccountId,
    Guid? ProductItemId = null);

public sealed class PurchaseOrderService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public async Task<PurchaseOrder> CreateDraftAsync(
        string userId,
        PurchaseOrderRequest request,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                request.OrganisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot create purchase orders for this organisation.");
        }

        var supplierExists =
            await db.BusinessParties.AnyAsync(
                x =>
                    x.Id == request.SupplierId &&
                    x.OrganisationId == request.OrganisationId &&
                    x.IsActive &&
                    (x.Type & PartyType.Supplier) != 0,
                ct);

        if (!supplierExists)
        {
            throw new InvalidOperationException(
                "Select an active supplier.");
        }

        if (request.Lines.Count == 0)
        {
            throw new InvalidOperationException(
                "A purchase order needs at least one line.");
        }

        var organisation =
            await db.Organisations
                .SingleAsync(
                    x => x.Id == request.OrganisationId,
                    ct);

        var sequence =
            (await db.PurchaseOrders
                .Where(
                    x => x.OrganisationId ==
                        request.OrganisationId)
                .MaxAsync(
                    x => (long?)x.SequenceNumber,
                    ct) ?? 0) + 1;

        var lines =
            request.Lines
                .Select(
                    x =>
                        new PurchaseOrderLine
                        {
                            Description =
                                x.Description.Trim(),

                            Quantity =
                                x.Quantity,

                            UnitPrice =
                                x.UnitPrice,

                            ExpenseAccountId =
                                x.ExpenseAccountId,

                            ProductItemId =
                                x.ProductItemId,

                            NetAmount =
                                x.Quantity *
                                x.UnitPrice,

                            GrossAmount =
                                x.Quantity *
                                x.UnitPrice
                        })
                .ToList();

        var order =
            new PurchaseOrder
            {
                OrganisationId =
                    request.OrganisationId,

                SupplierId =
                    request.SupplierId,

                SequenceNumber =
                    sequence,

                PurchaseOrderNumber =
                    $"PO-{sequence:D6}",

                OrderDate =
                    request.OrderDate,

                ExpectedDate =
                    request.ExpectedDate,

                SupplierReference =
                    request.SupplierReference.Trim(),

                Notes =
                    request.Notes.Trim(),

                Currency =
                    organisation.BaseCurrency,

                Status =
                    PurchaseOrderStatus.Draft,

                Subtotal =
                    lines.Sum(x => x.NetAmount),

                Total =
                    lines.Sum(x => x.GrossAmount),

                Lines =
                    lines,

                CreatedByUserId =
                    userId
            };

        db.PurchaseOrders.Add(order);

        await db.SaveChangesAsync(ct);

        return order;
    }

    public async Task ApproveAsync(
        string userId,
        Guid organisationId,
        Guid purchaseOrderId,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot approve purchase orders.");
        }

        var order =
            await db.PurchaseOrders
                .SingleAsync(
                    x =>
                        x.Id == purchaseOrderId &&
                        x.OrganisationId == organisationId,
                    ct);

        if (order.Status != PurchaseOrderStatus.Draft)
        {
            throw new InvalidOperationException(
                "Only draft purchase orders can be approved.");
        }

        order.Status =
            PurchaseOrderStatus.Approved;

        order.UpdatedAt =
            DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    public async Task MarkSentAsync(
        string userId,
        Guid organisationId,
        Guid purchaseOrderId,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot update purchase orders.");
        }

        var order =
            await db.PurchaseOrders
                .SingleAsync(
                    x =>
                        x.Id == purchaseOrderId &&
                        x.OrganisationId == organisationId,
                    ct);

        if (order.Status != PurchaseOrderStatus.Approved)
        {
            throw new InvalidOperationException(
                "Only approved purchase orders can be sent.");
        }

        order.Status =
            PurchaseOrderStatus.Sent;

        order.UpdatedAt =
            DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(
        string userId,
        Guid organisationId,
        Guid purchaseOrderId,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot cancel purchase orders.");
        }

        var order =
            await db.PurchaseOrders
                .SingleAsync(
                    x =>
                        x.Id == purchaseOrderId &&
                        x.OrganisationId == organisationId,
                    ct);

        if (order.Status is PurchaseOrderStatus.Received or
            PurchaseOrderStatus.Closed)
        {
            throw new InvalidOperationException(
                "Received or closed purchase orders cannot be cancelled.");
        }

        order.Status =
            PurchaseOrderStatus.Cancelled;

        order.UpdatedAt =
            DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }

        public async Task ReceiveAsync(
        string userId,
        Guid organisationId,
        Guid purchaseOrderId,
        IReadOnlyDictionary<Guid, decimal> receivedQuantities,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot receive purchase orders.");
        }

        var order =
            await db.PurchaseOrders
                .Include(x => x.Lines)
                .SingleAsync(
                    x =>
                        x.Id == purchaseOrderId &&
                        x.OrganisationId == organisationId,
                    ct);

        if (order.Status is not
            (PurchaseOrderStatus.Sent or
             PurchaseOrderStatus.PartiallyReceived))
        {
            throw new InvalidOperationException(
                "Only sent purchase orders can be received.");
        }

        foreach (var line in order.Lines)
        {
            if (!receivedQuantities.TryGetValue(
                    line.Id,
                    out var quantity))
            {
                continue;
            }

            if (quantity < 0)
            {
                throw new InvalidOperationException(
                    "Received quantity cannot be negative.");
            }

            if (line.QuantityReceived + quantity >
                line.Quantity)
            {
                throw new InvalidOperationException(
                    "Received quantity cannot exceed ordered quantity.");
            }

            line.QuantityReceived += quantity;
        }

        order.Status =
            order.Lines.All(
                x => x.QuantityReceived >= x.Quantity)
                ? PurchaseOrderStatus.Received
                : PurchaseOrderStatus.PartiallyReceived;

        order.UpdatedAt =
            DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }
}