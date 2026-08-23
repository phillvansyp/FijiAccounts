using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Accounting;
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

        if (request.ExpectedDate < request.OrderDate ||
            request.SupplierReference.Trim().Length > 80 ||
            request.Notes.Trim().Length > 500 ||
            request.Lines.Any(x =>
                string.IsNullOrWhiteSpace(x.Description) ||
                x.Description.Trim().Length > 300 ||
                x.Quantity <= 0 ||
                x.UnitPrice < 0 ||
                x.ExpenseAccountId == Guid.Empty))
        {
            throw new InvalidOperationException(
                "Enter valid purchase order dates, details and lines.");
        }

        var accountIds = request.Lines
            .Select(x => x.ExpenseAccountId)
            .Distinct()
            .ToArray();
        var validAccountCount = await db.LedgerAccounts
            .AsNoTracking()
            .CountAsync(
                x =>
                    x.OrganisationId == request.OrganisationId &&
                    x.IsActive &&
                    accountIds.Contains(x.Id) &&
                    (x.Type == AccountType.Expense || x.Type == AccountType.Asset),
                ct);
        if (validAccountCount != accountIds.Length)
        {
            throw new InvalidOperationException(
                "Every purchase order line must use an active expense or asset account from this organisation.");
        }

        var productIds = request.Lines
            .Where(x => x.ProductItemId is not null)
            .Select(x => x.ProductItemId!.Value)
            .Distinct()
            .ToArray();
        var validProductCount = await db.ProductItems
            .AsNoTracking()
            .CountAsync(
                x =>
                    x.OrganisationId == request.OrganisationId &&
                    x.IsActive &&
                    productIds.Contains(x.Id),
                ct);
        if (validProductCount != productIds.Length)
        {
            throw new InvalidOperationException(
                "Every selected product must be active and belong to this organisation.");
        }

        var organisation =
            await db.Organisations
                .SingleAsync(
                    x => x.Id == request.OrganisationId,
                    ct);

        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct)
            : null;

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
        db.AuditEvents.Add(Audit(
            request.OrganisationId,
            userId,
            "PurchaseOrderCreated",
            order,
            new
            {
                New = Evidence(order)
            }));

        await db.SaveChangesAsync(ct);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }

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
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == purchaseOrderId &&
                        x.OrganisationId == organisationId,
                    ct)
            ?? throw new InvalidOperationException(
                "Purchase order not found.");

        if (order.Status != PurchaseOrderStatus.Draft)
        {
            throw new InvalidOperationException(
                "Only draft purchase orders can be approved.");
        }

        var previous = Evidence(order);
        order.Status = PurchaseOrderStatus.Approved;

        order.UpdatedAt =
            DateTimeOffset.UtcNow;

        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "PurchaseOrderApproved",
            order,
            new { Old = previous, New = Evidence(order) }));
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
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == purchaseOrderId &&
                        x.OrganisationId == organisationId,
                    ct)
            ?? throw new InvalidOperationException(
                "Purchase order not found.");

        if (order.Status != PurchaseOrderStatus.Approved)
        {
            throw new InvalidOperationException(
                "Only approved purchase orders can be sent.");
        }

        var previous = Evidence(order);
        order.Status = PurchaseOrderStatus.Sent;

        order.UpdatedAt =
            DateTimeOffset.UtcNow;

        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "PurchaseOrderMarkedSent",
            order,
            new { Old = previous, New = Evidence(order) }));
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
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == purchaseOrderId &&
                        x.OrganisationId == organisationId,
                    ct)
            ?? throw new InvalidOperationException(
                "Purchase order not found.");

        if (order.Status == PurchaseOrderStatus.Cancelled)
        {
            return;
        }

        if (order.Status is PurchaseOrderStatus.Received or
            PurchaseOrderStatus.Closed)
        {
            throw new InvalidOperationException(
                "Received or closed purchase orders cannot be cancelled.");
        }

        var previous = Evidence(order);
        order.Status = PurchaseOrderStatus.Cancelled;

        order.UpdatedAt =
            DateTimeOffset.UtcNow;

        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "PurchaseOrderCancelled",
            order,
            new { Old = previous, New = Evidence(order) }));
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
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == purchaseOrderId &&
                        x.OrganisationId == organisationId,
                    ct)
            ?? throw new InvalidOperationException(
                "Purchase order not found.");

        if (order.Status is not
            (PurchaseOrderStatus.Sent or
             PurchaseOrderStatus.PartiallyReceived))
        {
            throw new InvalidOperationException(
                "Only sent purchase orders can be received.");
        }

        var lineIds = order.Lines.Select(x => x.Id).ToHashSet();
        if (receivedQuantities.Keys.Any(x => !lineIds.Contains(x)))
        {
            throw new InvalidOperationException(
                "Every received line must belong to this purchase order.");
        }

        foreach (var line in order.Lines)
        {
            if (!receivedQuantities.TryGetValue(line.Id, out var quantity))
            {
                continue;
            }

            if (quantity < 0)
            {
                throw new InvalidOperationException(
                    "Received quantity cannot be negative.");
            }

            if (line.QuantityReceived + quantity > line.Quantity)
            {
                throw new InvalidOperationException(
                    "Received quantity cannot exceed ordered quantity.");
            }
        }

        var receiptLines = new List<object>();
        var previous = Evidence(order);
        foreach (var line in order.Lines)
        {
            if (!receivedQuantities.TryGetValue(
                    line.Id,
                    out var quantity))
            {
                continue;
            }

            if (quantity == 0)
            {
                continue;
            }

            var oldQuantityReceived = line.QuantityReceived;
            line.QuantityReceived += quantity;
            receiptLines.Add(new
            {
                LineId = line.Id,
                line.Description,
                QuantityReceived = quantity,
                OldQuantityReceived = oldQuantityReceived,
                NewQuantityReceived = line.QuantityReceived
            });
        }

        if (receiptLines.Count == 0)
        {
            return;
        }

        order.Status =
            order.Lines.All(
                x => x.QuantityReceived >= x.Quantity)
                ? PurchaseOrderStatus.Received
                : PurchaseOrderStatus.PartiallyReceived;

        order.UpdatedAt =
            DateTimeOffset.UtcNow;

        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "PurchaseOrderReceiptRecorded",
            order,
            new
            {
                Old = previous,
                New = Evidence(order),
                Lines = receiptLines
            }));
        await db.SaveChangesAsync(ct);
    }

    private static object Evidence(PurchaseOrder order) =>
        new
        {
            order.PurchaseOrderNumber,
            order.SupplierId,
            order.OrderDate,
            order.ExpectedDate,
            order.SupplierReference,
            order.Notes,
            Status = order.Status.ToString(),
            order.Subtotal,
            order.Total,
            Lines = order.Lines.Select(x => new
                {
                    x.Id,
                    x.Description,
                    x.Quantity,
                    x.QuantityReceived,
                    x.UnitPrice,
                    x.ExpenseAccountId,
                    x.ProductItemId
                })
                .ToArray()
        };

    private static AuditEvent Audit(
        Guid organisationId,
        string userId,
        string eventType,
        PurchaseOrder order,
        object evidence) =>
        new()
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = eventType,
            EntityType = nameof(PurchaseOrder),
            EntityId = order.Id.ToString(),
            JsonData = JsonSerializer.Serialize(evidence)
        };
}
