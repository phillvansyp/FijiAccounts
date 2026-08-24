using System.Text.Json;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record SupplierBillDraftLineRequest(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    VatTreatment VatTreatment,
    Guid? ExpenseAccountId,
    Guid? ProductItemId = null);

public sealed record SaveSupplierBillDraftRequest(
    Guid OrganisationId,
    Guid? DraftId,
    Guid? SupplierId,
    Guid? BranchId,
    Guid? DivisionId,
    string SupplierReference,
    DateOnly BillDate,
    DateOnly DueDate,
    IReadOnlyList<SupplierBillDraftLineRequest> Lines,
    SupplierBillAttachmentRequest? Attachment = null,
    bool AmountsIncludeVat = false);

public sealed class SupplierBillDraftService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public async Task<SupplierBillDraft> SaveAsync(
        string userId,
        SaveSupplierBillDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireAccessAsync(userId, request.OrganisationId);
        await ValidateRequestAsync(request, cancellationToken);

        SupplierBillDraft draft;
        var created = request.DraftId is null;
        if (request.DraftId is Guid draftId)
        {
            draft = await db.SupplierBillDrafts.SingleOrDefaultAsync(
                x =>
                    x.Id == draftId &&
                    x.OrganisationId == request.OrganisationId,
                cancellationToken)
                ?? throw new InvalidOperationException(
                    "The supplier bill draft was not found for this organisation.");
        }
        else
        {
            draft = new SupplierBillDraft
            {
                OrganisationId = request.OrganisationId,
                CreatedByUserId = userId
            };
            db.SupplierBillDrafts.Add(draft);
        }

        var previous = created ? null : Evidence(draft);
        var firstLine = request.Lines[0];
        var additionalLinesJson = JsonSerializer.Serialize(request.Lines.Skip(1));
        var supplierReference = request.SupplierReference.Trim();
        var attachment = request.Attachment;
        var unchanged =
            !created &&
            draft.SupplierId == request.SupplierId &&
            draft.BranchId == request.BranchId &&
            draft.DivisionId == request.DivisionId &&
            draft.SupplierReference == supplierReference &&
            draft.BillDate == request.BillDate &&
            draft.DueDate == request.DueDate &&
            draft.Description == firstLine.Description.Trim() &&
            draft.Quantity == firstLine.Quantity &&
            draft.UnitPrice == firstLine.UnitPrice &&
            draft.AmountsIncludeVat == request.AmountsIncludeVat &&
            draft.VatTreatment == firstLine.VatTreatment &&
            draft.ExpenseAccountId == firstLine.ExpenseAccountId &&
            draft.ProductItemId == firstLine.ProductItemId &&
            draft.AdditionalLinesJson == additionalLinesJson &&
            draft.AttachmentFileName == attachment?.FileName.Trim() &&
            draft.AttachmentContentType == attachment?.ContentType.Trim() &&
            draft.AttachmentOriginalSize == attachment?.OriginalSize &&
            draft.AttachmentIsCompressed == (attachment?.IsCompressed ?? false) &&
            AttachmentEquals(draft.AttachmentContent, attachment?.Content);
        if (unchanged)
        {
            return draft;
        }

        draft.SupplierId = request.SupplierId;
        draft.BranchId = request.BranchId;
        draft.DivisionId = request.DivisionId;
        draft.SupplierReference = supplierReference;
        draft.BillDate = request.BillDate;
        draft.DueDate = request.DueDate;
        draft.Description = firstLine.Description.Trim();
        draft.Quantity = firstLine.Quantity;
        draft.UnitPrice = firstLine.UnitPrice;
        draft.AmountsIncludeVat = request.AmountsIncludeVat;
        draft.VatTreatment = firstLine.VatTreatment;
        draft.ExpenseAccountId = firstLine.ExpenseAccountId;
        draft.ProductItemId = firstLine.ProductItemId;
        draft.AdditionalLinesJson = additionalLinesJson;
        draft.AttachmentFileName = attachment?.FileName.Trim();
        draft.AttachmentContentType = attachment?.ContentType.Trim();
        draft.AttachmentOriginalSize = attachment?.OriginalSize;
        draft.AttachmentIsCompressed = attachment?.IsCompressed ?? false;
        draft.AttachmentContent = attachment?.Content;
        draft.UpdatedAt = DateTimeOffset.UtcNow;

        db.AuditEvents.Add(Audit(
            request.OrganisationId,
            userId,
            created ? "SupplierBillDraftCreated" : "SupplierBillDraftUpdated",
            draft.Id,
            created
                ? new { New = Evidence(draft) }
                : new { Old = previous, New = Evidence(draft) }));
        await db.SaveChangesAsync(cancellationToken);

        return draft;
    }

    public async Task<bool> DeleteAsync(
        string userId,
        Guid organisationId,
        Guid draftId,
        CancellationToken cancellationToken = default)
    {
        await RequireAccessAsync(userId, organisationId);

        var draft = await db.SupplierBillDrafts.SingleOrDefaultAsync(
            x => x.Id == draftId && x.OrganisationId == organisationId,
            cancellationToken);
        if (draft is null)
        {
            return false;
        }

        var linkedOrder = await db.PurchaseOrders.SingleOrDefaultAsync(
            x =>
                x.OrganisationId == organisationId &&
                x.SupplierBillDraftId == draftId,
            cancellationToken);
        if (linkedOrder is not null)
        {
            linkedOrder.SupplierBillDraftId = null;
            linkedOrder.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var recurringGeneration =
            await db.RecurringSupplierBillGenerations
                .SingleOrDefaultAsync(
                    x =>
                        x.OrganisationId == organisationId &&
                        x.SupplierBillDraftId == draftId,
                    cancellationToken);

        if (recurringGeneration is not null)
        {
            recurringGeneration.SupplierBillDraftId = null;
        }

        db.SupplierBillDrafts.Remove(draft);
        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "SupplierBillDraftDeleted",
            draft.Id,
            new
            {
                Old = Evidence(draft),
                SourcePurchaseOrderId = linkedOrder?.Id
            }));
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<SupplierBillDraft> CreateFromPurchaseOrderAsync(
        string userId,
        Guid organisationId,
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default)
    {
        await RequireAccessAsync(userId, organisationId);

        var order = await db.PurchaseOrders
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(
                x =>
                    x.Id == purchaseOrderId &&
                    x.OrganisationId == organisationId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "The purchase order was not found for this organisation.");

        if (order.Status != PurchaseOrderStatus.Received || order.Lines.Count == 0)
        {
            throw new InvalidOperationException(
                "Only fully received purchase orders with lines can be converted to a supplier bill draft.");
        }

        if (order.SupplierBillDraftId is Guid existingDraftId)
        {
            var existingDraft = await db.SupplierBillDrafts.SingleOrDefaultAsync(
                x =>
                    x.Id == existingDraftId &&
                    x.OrganisationId == organisationId,
                cancellationToken);
            if (existingDraft is not null)
            {
                return existingDraft;
            }

            order.SupplierBillDraftId = null;
        }

        var firstLine = order.Lines[0];
        var additionalLines = order.Lines
            .Skip(1)
            .Select(x => new SupplierBillDraftLineRequest(
                x.Description,
                x.Quantity,
                x.UnitPrice,
                x.VatTreatment,
                x.ExpenseAccountId,
                x.ProductItemId))
            .ToList();
        var draft = new SupplierBillDraft
        {
            OrganisationId = organisationId,
            SupplierId = order.SupplierId,
            SupplierReference = string.IsNullOrWhiteSpace(order.SupplierReference)
                ? order.PurchaseOrderNumber
                : order.SupplierReference,
            BillDate = order.OrderDate,
            DueDate = order.ExpectedDate ?? order.OrderDate,
            Description = firstLine.Description,
            Quantity = firstLine.Quantity,
            UnitPrice = firstLine.UnitPrice,
            VatTreatment = firstLine.VatTreatment,
            ExpenseAccountId = firstLine.ExpenseAccountId,
            ProductItemId = firstLine.ProductItemId,
            AdditionalLinesJson = JsonSerializer.Serialize(additionalLines),
            CreatedByUserId = userId,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.SupplierBillDrafts.Add(draft);
        order.SupplierBillDraftId = draft.Id;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "SupplierBillDraftCreatedFromPurchaseOrder",
            draft.Id,
            new
            {
                PurchaseOrderId = order.Id,
                order.PurchaseOrderNumber,
                New = Evidence(draft)
            }));
        await db.SaveChangesAsync(cancellationToken);

        return draft;
    }

    private async Task ValidateRequestAsync(
        SaveSupplierBillDraftRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Lines.Count == 0)
        {
            throw new InvalidOperationException(
                "A supplier bill draft needs at least one line.");
        }

        if (request.SupplierReference.Trim().Length > 80 ||
            request.Lines.Any(x =>
                x.Description.Trim().Length > 300 ||
                !Enum.IsDefined(x.VatTreatment)))
        {
            throw new InvalidOperationException(
                "The supplier bill draft contains invalid details.");
        }

        if (request.SupplierId is Guid supplierId &&
            !await db.BusinessParties.AsNoTracking().AnyAsync(
                x =>
                    x.Id == supplierId &&
                    x.OrganisationId == request.OrganisationId &&
                    x.IsActive &&
                    (x.Type & PartyType.Supplier) != 0,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Select an active supplier from this organisation.");
        }

        if (request.BranchId is not null || request.DivisionId is not null)
        {
            if (request.BranchId is not Guid branchId ||
                request.DivisionId is not Guid divisionId ||
                !await db.Divisions.AsNoTracking().AnyAsync(
                    x =>
                        x.Id == divisionId &&
                        x.BranchId == branchId &&
                        x.IsActive &&
                        x.Branch.IsActive &&
                        x.Branch.OrganisationId == request.OrganisationId,
                    cancellationToken))
            {
                throw new InvalidOperationException(
                    "Select an active branch and division from this organisation.");
            }
        }

        var accountIds = request.Lines
            .Where(x =>
                x.ExpenseAccountId is Guid accountId &&
                accountId != Guid.Empty)
            .Select(x => x.ExpenseAccountId!.Value)
            .Distinct()
            .ToArray();
        var validAccountCount = await db.LedgerAccounts.AsNoTracking().CountAsync(
            x =>
                x.OrganisationId == request.OrganisationId &&
                x.IsActive &&
                accountIds.Contains(x.Id) &&
                (x.Type == AccountType.Expense || x.Type == AccountType.Asset),
            cancellationToken);
        if (validAccountCount != accountIds.Length)
        {
            throw new InvalidOperationException(
                "Every selected account must be active and belong to this organisation.");
        }

        var productIds = request.Lines
            .Where(x => x.ProductItemId is not null)
            .Select(x => x.ProductItemId!.Value)
            .Distinct()
            .ToArray();
        var validProductCount = await db.ProductItems.AsNoTracking().CountAsync(
            x =>
                x.OrganisationId == request.OrganisationId &&
                x.IsActive &&
                productIds.Contains(x.Id),
            cancellationToken);
        if (validProductCount != productIds.Length)
        {
            throw new InvalidOperationException(
                "Every selected product must be active and belong to this organisation.");
        }

        if (request.Attachment is { } attachment)
        {
            _ = SupplierBillAttachmentService.CreateValidated(
                request.OrganisationId,
                Guid.Empty,
                string.Empty,
                attachment);
        }
    }

    private async Task RequireAccessAsync(
        string userId,
        Guid organisationId)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot manage supplier bill drafts for this organisation.");
        }
    }

    private static object Evidence(SupplierBillDraft draft) =>
        new
        {
            draft.SupplierId,
            draft.BranchId,
            draft.DivisionId,
            draft.SupplierReference,
            draft.BillDate,
            draft.DueDate,
            draft.AmountsIncludeVat,
            FirstLine = new
            {
                draft.Description,
                draft.Quantity,
                draft.UnitPrice,
                VatTreatment = draft.VatTreatment.ToString(),
                draft.ExpenseAccountId,
                draft.ProductItemId
            },
            AdditionalLineCount = CountAdditionalLines(draft.AdditionalLinesJson),
            Attachment = draft.AttachmentContent is null
                ? null
                : new
                {
                    draft.AttachmentFileName,
                    draft.AttachmentContentType,
                    draft.AttachmentOriginalSize,
                    draft.AttachmentIsCompressed,
                    StoredSize = draft.AttachmentContent.LongLength
                }
        };

    private static int CountAdditionalLines(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement[]>(json)?.Length ?? 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static bool AttachmentEquals(byte[]? first, byte[]? second) =>
        first is null
            ? second is null
            : second is not null && first.AsSpan().SequenceEqual(second);

    private static AuditEvent Audit(
        Guid organisationId,
        string userId,
        string eventType,
        Guid draftId,
        object evidence) =>
        new()
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = eventType,
            EntityType = nameof(SupplierBillDraft),
            EntityId = draftId.ToString(),
            JsonData = JsonSerializer.Serialize(evidence)
        };
}
