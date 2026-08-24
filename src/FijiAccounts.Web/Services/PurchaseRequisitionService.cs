using System.Data;
using System.Text.Json;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record PurchaseRequisitionRequest(
    Guid OrganisationId,
    Guid BranchId,
    Guid DivisionId,
    Guid SupplierId,
    DateOnly RequestDate,
    DateOnly? RequiredDate,
    string Purpose,
    IReadOnlyList<PurchaseRequisitionLineRequest> Lines);

public sealed record PurchaseRequisitionLineRequest(
    string Description,
    decimal Quantity,
    decimal EstimatedUnitPrice,
    Guid ExpenseAccountId,
    Guid? ProductItemId = null,
    Guid? ProjectId = null,
    Guid? ProjectCostCodeId = null);

public sealed class PurchaseRequisitionService(
    ApplicationDbContext db,
    TenantAccessService access,
    PurchaseOrderService purchaseOrders,
    PurchaseApprovalPolicyService approvalPolicies)
{
    public async Task<List<PurchaseRequisition>> ListAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct = default)
    {
        if (await access.FindAsync(userId, organisationId) is null)
        {
            throw new UnauthorizedAccessException("You cannot view purchase requisitions for this organisation.");
        }

        var allowedDivisions = await access.GetReportDivisionScopeAsync(userId, organisationId, ct);
        return await db.PurchaseRequisitions
            .AsNoTracking()
            .Include(x => x.Supplier)
            .Include(x => x.Branch)
            .Include(x => x.Division)
            .Include(x => x.Lines)
            .Where(x =>
                x.OrganisationId == organisationId &&
                (allowedDivisions == null || allowedDivisions.Contains(x.DivisionId)))
            .OrderByDescending(x => x.RequestDate)
            .ThenByDescending(x => x.SequenceNumber)
            .ToListAsync(ct);
    }

    public async Task<PurchaseRequisition> CreateDraftAsync(
        string userId,
        PurchaseRequisitionRequest request,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(userId, request.OrganisationId) ||
            !await access.CanAccessDimensionAsync(
                userId,
                request.OrganisationId,
                request.BranchId,
                request.DivisionId,
                ct))
        {
            throw new UnauthorizedAccessException("You cannot create a requisition for this branch and division.");
        }

        await ValidateRequestAsync(request, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var sequence = (await db.PurchaseRequisitions
            .Where(x => x.OrganisationId == request.OrganisationId)
            .MaxAsync(x => (long?)x.SequenceNumber, ct) ?? 0) + 1;
        var lines = request.Lines.Select(x => new PurchaseRequisitionLine
        {
            Description = x.Description.Trim(),
            Quantity = x.Quantity,
            EstimatedUnitPrice = x.EstimatedUnitPrice,
            EstimatedTotal = x.Quantity * x.EstimatedUnitPrice,
            ExpenseAccountId = x.ExpenseAccountId,
            ProductItemId = x.ProductItemId,
            ProjectId = x.ProjectId,
            ProjectCostCodeId = x.ProjectCostCodeId
        }).ToList();
        var requisition = new PurchaseRequisition
        {
            OrganisationId = request.OrganisationId,
            BranchId = request.BranchId,
            DivisionId = request.DivisionId,
            SupplierId = request.SupplierId,
            SequenceNumber = sequence,
            RequisitionNumber = $"PR-{sequence:D6}",
            RequestDate = request.RequestDate,
            RequiredDate = request.RequiredDate,
            Purpose = request.Purpose.Trim(),
            Total = lines.Sum(x => x.EstimatedTotal),
            CreatedByUserId = userId,
            Lines = lines
        };
        db.PurchaseRequisitions.Add(requisition);
        db.AuditEvents.Add(Audit(requisition, userId, "PurchaseRequisitionCreated", new { New = Evidence(requisition) }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return requisition;
    }

    public async Task SubmitAsync(string userId, Guid organisationId, Guid requisitionId, CancellationToken ct = default)
    {
        var requisition = await RequireRequisitionAsync(userId, organisationId, requisitionId, ct);
        if (requisition.CreatedByUserId != userId)
        {
            throw new UnauthorizedAccessException("Only the requisition creator can submit it.");
        }
        if (requisition.Status != PurchaseRequisitionStatus.Draft)
        {
            throw new InvalidOperationException("Only draft requisitions can be submitted.");
        }

        var old = Evidence(requisition);
        var policy = await approvalPolicies.ResolveAsync(
            organisationId,
            requisition.BranchId,
            requisition.DivisionId,
            requisition.Total,
            ct);
        requisition.PurchaseApprovalPolicyId = policy?.Id;
        requisition.RequiredApproval = policy?.Requirement ?? PurchaseApprovalRequirement.OwnerOrAdministrator;
        requisition.Status = PurchaseRequisitionStatus.Submitted;
        requisition.SubmittedAt = requisition.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(Audit(requisition, userId, "PurchaseRequisitionSubmitted", new { Old = old, New = Evidence(requisition) }));
        await db.SaveChangesAsync(ct);
    }

    public async Task ApproveAsync(string userId, Guid organisationId, Guid requisitionId, CancellationToken ct = default)
    {
        var requisition = await LoadAsync(organisationId, requisitionId, ct);
        if (!await approvalPolicies.CanApproveAsync(userId, organisationId, requisition.RequiredApproval, ct))
        {
            throw new UnauthorizedAccessException(
                requisition.RequiredApproval == PurchaseApprovalRequirement.OwnerOnly
                    ? "This requisition requires approval by an organisation owner."
                    : "Only an owner or administrator can approve requisitions.");
        }
        if (requisition.CreatedByUserId == userId)
        {
            throw new InvalidOperationException("A requisition creator cannot approve their own request.");
        }
        if (requisition.Status != PurchaseRequisitionStatus.Submitted)
        {
            throw new InvalidOperationException("Only submitted requisitions can be approved.");
        }

        var old = Evidence(requisition);
        requisition.Status = PurchaseRequisitionStatus.Approved;
        requisition.ApprovedByUserId = userId;
        requisition.ApprovedAt = requisition.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(Audit(requisition, userId, "PurchaseRequisitionApproved", new { Old = old, New = Evidence(requisition) }));
        await db.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(
        string userId,
        Guid organisationId,
        Guid requisitionId,
        string reason,
        CancellationToken ct = default)
    {
        var requisition = await LoadAsync(organisationId, requisitionId, ct);
        if (!await approvalPolicies.CanApproveAsync(userId, organisationId, requisition.RequiredApproval, ct))
        {
            throw new UnauthorizedAccessException(
                requisition.RequiredApproval == PurchaseApprovalRequirement.OwnerOnly
                    ? "This requisition requires action by an organisation owner."
                    : "Only an owner or administrator can reject requisitions.");
        }
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
        {
            throw new InvalidOperationException("Enter a rejection reason of 500 characters or fewer.");
        }
        if (requisition.CreatedByUserId == userId)
        {
            throw new InvalidOperationException("A requisition creator cannot reject their own request.");
        }
        if (requisition.Status != PurchaseRequisitionStatus.Submitted)
        {
            throw new InvalidOperationException("Only submitted requisitions can be rejected.");
        }

        var old = Evidence(requisition);
        requisition.Status = PurchaseRequisitionStatus.Rejected;
        requisition.RejectedByUserId = userId;
        requisition.RejectionReason = reason.Trim();
        requisition.RejectedAt = requisition.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(Audit(requisition, userId, "PurchaseRequisitionRejected", new { Old = old, New = Evidence(requisition) }));
        await db.SaveChangesAsync(ct);
    }

    public async Task<PurchaseOrder> ConvertToPurchaseOrderAsync(
        string userId,
        Guid organisationId,
        Guid requisitionId,
        CancellationToken ct = default)
    {
        var requisition = await RequireRequisitionAsync(userId, organisationId, requisitionId, ct);
        if (requisition.Status != PurchaseRequisitionStatus.Approved)
        {
            throw new InvalidOperationException("Only approved requisitions can be converted to purchase orders.");
        }
        if (string.IsNullOrWhiteSpace(requisition.ApprovedByUserId))
        {
            throw new InvalidOperationException("The requisition does not contain valid approval evidence.");
        }
        if (await db.PurchaseOrders.AnyAsync(x => x.PurchaseRequisitionId == requisition.Id, ct))
        {
            throw new InvalidOperationException("This requisition has already been converted.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var order = await purchaseOrders.CreateDraftAsync(userId, new PurchaseOrderRequest(
            organisationId,
            requisition.SupplierId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            requisition.RequiredDate,
            requisition.RequisitionNumber,
            requisition.Purpose,
            requisition.Lines.Select(x => new PurchaseOrderLineRequest(
                x.Description,
                x.Quantity,
                x.EstimatedUnitPrice,
                x.ExpenseAccountId,
                x.ProductItemId,
                x.ProjectId,
                x.ProjectCostCodeId)).ToArray(),
            requisition.BranchId,
            requisition.DivisionId), ct);
        order.PurchaseRequisitionId = requisition.Id;
        order.Status = PurchaseOrderStatus.Approved;
        order.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = organisationId,
            UserId = requisition.ApprovedByUserId,
            EventType = "PurchaseOrderApprovedFromRequisition",
            EntityType = nameof(PurchaseOrder),
            EntityId = order.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                requisition.RequisitionNumber,
                PurchaseRequisitionId = requisition.Id,
                requisition.ApprovedByUserId,
                requisition.ApprovedAt,
                order.PurchaseOrderNumber,
                order.Total
            })
        });
        var old = Evidence(requisition);
        requisition.Status = PurchaseRequisitionStatus.Converted;
        requisition.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(Audit(requisition, userId, "PurchaseRequisitionConverted", new
        {
            Old = old,
            New = Evidence(requisition),
            order.PurchaseOrderNumber,
            PurchaseOrderId = order.Id
        }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return order;
    }

    private async Task ValidateRequestAsync(PurchaseRequisitionRequest request, CancellationToken ct)
    {
        if (request.RequiredDate < request.RequestDate ||
            string.IsNullOrWhiteSpace(request.Purpose) ||
            request.Purpose.Trim().Length > 500 ||
            request.Lines.Count == 0 ||
            request.Lines.Any(x => string.IsNullOrWhiteSpace(x.Description) || x.Description.Trim().Length > 300 ||
                x.Quantity <= 0 || x.EstimatedUnitPrice < 0 || x.ExpenseAccountId == Guid.Empty))
        {
            throw new InvalidOperationException("Enter valid requisition dates, purpose and lines.");
        }
        var dimensionExists = await db.Divisions.AnyAsync(x =>
            x.Id == request.DivisionId && x.BranchId == request.BranchId &&
            x.Branch.OrganisationId == request.OrganisationId && x.IsActive && x.Branch.IsActive, ct);
        var supplierExists = await db.BusinessParties.AnyAsync(x =>
            x.Id == request.SupplierId && x.OrganisationId == request.OrganisationId && x.IsActive &&
            (x.Type & PartyType.Supplier) != 0, ct);
        if (!dimensionExists || !supplierExists)
        {
            throw new InvalidOperationException("Select an active branch, division and supplier from this organisation.");
        }
        var accountIds = request.Lines.Select(x => x.ExpenseAccountId).Distinct().ToArray();
        var accountCount = await db.LedgerAccounts.CountAsync(x =>
            x.OrganisationId == request.OrganisationId && x.IsActive && accountIds.Contains(x.Id) &&
            (x.Type == AccountType.Expense || x.Type == AccountType.Asset), ct);
        var productIds = request.Lines.Where(x => x.ProductItemId.HasValue).Select(x => x.ProductItemId!.Value).Distinct().ToArray();
        var productCount = await db.ProductItems.CountAsync(x =>
            x.OrganisationId == request.OrganisationId && x.IsActive && productIds.Contains(x.Id), ct);
        if (accountCount != accountIds.Length || productCount != productIds.Length)
        {
            throw new InvalidOperationException("Every line must use active accounts and products from this organisation.");
        }

        await ProjectCodingValidator.ValidateAsync(
            db,
            request.OrganisationId,
            request.BranchId,
            request.DivisionId,
            request.Lines.Select(x => new ProjectCoding(x.ProjectId, x.ProjectCostCodeId)),
            cancellationToken: ct);
    }

    private async Task<PurchaseRequisition> RequireRequisitionAsync(
        string userId,
        Guid organisationId,
        Guid requisitionId,
        CancellationToken ct)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException("You cannot update purchase requisitions for this organisation.");
        }
        var requisition = await LoadAsync(organisationId, requisitionId, ct);
        if (!await access.CanAccessDimensionAsync(userId, organisationId, requisition.BranchId, requisition.DivisionId, ct))
        {
            throw new UnauthorizedAccessException("You cannot update requisitions for this branch and division.");
        }
        return requisition;
    }

    private async Task<PurchaseRequisition> LoadAsync(
        Guid organisationId,
        Guid requisitionId,
        CancellationToken ct) =>
        await db.PurchaseRequisitions.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == requisitionId && x.OrganisationId == organisationId, ct)
        ?? throw new InvalidOperationException("Purchase requisition not found.");

    private static object Evidence(PurchaseRequisition requisition) => new
    {
        requisition.RequisitionNumber,
        requisition.BranchId,
        requisition.DivisionId,
        requisition.SupplierId,
        requisition.RequestDate,
        requisition.RequiredDate,
        requisition.Purpose,
        Status = requisition.Status.ToString(),
        requisition.Total,
        requisition.PurchaseApprovalPolicyId,
        RequiredApproval = requisition.RequiredApproval.ToString(),
        requisition.CreatedByUserId,
        requisition.ApprovedByUserId,
        requisition.RejectedByUserId,
        requisition.RejectionReason,
        Lines = requisition.Lines.Select(x => new
        {
            x.Id,
            x.Description,
            x.Quantity,
            x.EstimatedUnitPrice,
            x.EstimatedTotal,
            x.ExpenseAccountId,
            x.ProductItemId,
            x.ProjectId,
            x.ProjectCostCodeId
        }).ToArray()
    };

    private static AuditEvent Audit(PurchaseRequisition requisition, string userId, string eventType, object evidence) => new()
    {
        OrganisationId = requisition.OrganisationId,
        UserId = userId,
        EventType = eventType,
        EntityType = nameof(PurchaseRequisition),
        EntityId = requisition.Id.ToString(),
        JsonData = JsonSerializer.Serialize(evidence)
    };
}
