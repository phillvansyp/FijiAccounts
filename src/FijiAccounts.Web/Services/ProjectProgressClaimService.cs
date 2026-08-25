using System.Data;
using System.Text.Json;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record ProjectProgressClaimRequest(
    Guid OrganisationId,
    Guid ProjectId,
    string ClaimNumber,
    string Description,
    DateOnly ClaimPeriodEnd,
    decimal WorkCompletedAmount,
    decimal RetentionReleasedAmount,
    Guid RevenueAccountId,
    VatTreatment VatTreatment);

public sealed class ProjectProgressClaimService(
    ApplicationDbContext db,
    TenantAccessService access,
    SalesInvoiceService salesInvoices)
{
    public async Task<ProjectProgressClaim> CreateAsync(
        string userId,
        ProjectProgressClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireMaintainerAsync(userId, request.OrganisationId);
        var project = await LoadProjectAsync(
            userId, request.OrganisationId, request.ProjectId, cancellationToken);
        if (project.Status is not (ProjectStatus.Active or ProjectStatus.OnHold))
        {
            throw new InvalidOperationException(
                "Progress claims can only be created for active or on-hold projects.");
        }
        if (project.CustomerId is null)
        {
            throw new InvalidOperationException(
                "Select a project customer before creating a progress claim.");
        }

        var claimNumber = Required(request.ClaimNumber, "Claim number", 40);
        var description = Required(request.Description, "Claim description", 500);
        if (request.WorkCompletedAmount < 0 || request.RetentionReleasedAmount < 0 ||
            request.WorkCompletedAmount == 0 && request.RetentionReleasedAmount == 0)
        {
            throw new InvalidOperationException(
                "Enter a positive work amount, retention release, or both.");
        }
        if (!await db.LedgerAccounts.AnyAsync(x =>
                x.Id == request.RevenueAccountId &&
                x.OrganisationId == request.OrganisationId &&
                x.IsActive && x.Type == AccountType.Revenue,
                cancellationToken))
        {
            throw new InvalidOperationException("Select an active revenue account.");
        }
        if (await db.ProjectProgressClaims.AnyAsync(x =>
                x.ProjectId == project.Id && x.ClaimNumber == claimNumber,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Claim number is already in use for this project.");
        }

        var retentionHeld = Math.Round(
            request.WorkCompletedAmount * project.RetentionPercent / 100m,
            2,
            MidpointRounding.AwayFromZero);
        var claim = new ProjectProgressClaim
        {
            ProjectId = project.Id,
            ClaimNumber = claimNumber,
            Description = description,
            ClaimPeriodEnd = request.ClaimPeriodEnd,
            WorkCompletedAmount = request.WorkCompletedAmount,
            RetentionRate = project.RetentionPercent,
            RetentionHeldAmount = retentionHeld,
            RetentionReleasedAmount = request.RetentionReleasedAmount,
            RevenueAccountId = request.RevenueAccountId,
            VatTreatment = request.VatTreatment,
            CreatedByUserId = userId
        };
        if (claim.CertifiedAmount <= 0)
        {
            throw new InvalidOperationException(
                "The certified amount after retention must be greater than zero.");
        }

        db.ProjectProgressClaims.Add(claim);
        AddAudit(request.OrganisationId, userId, "ProjectProgressClaimCreated", claim);
        await db.SaveChangesAsync(cancellationToken);
        return claim;
    }

    public async Task SubmitAsync(
        string userId,
        Guid organisationId,
        Guid claimId,
        CancellationToken cancellationToken = default)
    {
        await RequireMaintainerAsync(userId, organisationId);
        var claim = await LoadClaimAsync(userId, organisationId, claimId, cancellationToken);
        if (claim.Status != ProjectProgressClaimStatus.Draft)
        {
            throw new InvalidOperationException("Only draft progress claims can be submitted.");
        }

        claim.Status = ProjectProgressClaimStatus.Submitted;
        claim.SubmittedAt = DateTimeOffset.UtcNow;
        AddAudit(organisationId, userId, "ProjectProgressClaimSubmitted", claim);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DecideAsync(
        string userId,
        Guid organisationId,
        Guid claimId,
        bool approve,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (!await access.CanManageTeamAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "Only an organisation owner or administrator can decide progress claims.");
        }
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var claim = await LoadClaimAsync(userId, organisationId, claimId, cancellationToken);
        if (claim.Status != ProjectProgressClaimStatus.Submitted)
        {
            throw new InvalidOperationException("Only submitted progress claims can be decided.");
        }

        var cleanedReason = Optional(reason, "Decision reason", 500);
        if (!approve && cleanedReason is null)
        {
            throw new InvalidOperationException("Enter a rejection reason.");
        }
        if (approve)
        {
            ValidateApproval(claim);
        }

        claim.Status = approve
            ? ProjectProgressClaimStatus.Approved
            : ProjectProgressClaimStatus.Rejected;
        claim.DecidedByUserId = userId;
        claim.DecisionReason = cleanedReason;
        claim.DecidedAt = DateTimeOffset.UtcNow;
        claim.Project.UpdatedAt = claim.DecidedAt.Value;
        AddAudit(
            organisationId,
            userId,
            approve ? "ProjectProgressClaimApproved" : "ProjectProgressClaimRejected",
            claim);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SalesInvoice> GenerateDraftInvoiceAsync(
        string userId,
        Guid organisationId,
        Guid claimId,
        DateOnly issueDate,
        DateOnly dueDate,
        CancellationToken cancellationToken = default)
    {
        await RequireMaintainerAsync(userId, organisationId);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var claim = await LoadClaimAsync(userId, organisationId, claimId, cancellationToken);
        if (claim.Status != ProjectProgressClaimStatus.Approved || claim.SalesInvoiceId is not null)
        {
            throw new InvalidOperationException(
                "Only an approved, uninvoiced progress claim can generate an invoice.");
        }
        if (claim.Project.CustomerId is not Guid customerId)
        {
            throw new InvalidOperationException(
                "Select a project customer before generating the invoice.");
        }

        var invoice = await salesInvoices.CreateDraftAsync(userId, new SalesInvoiceRequest(
            organisationId,
            customerId,
            issueDate,
            dueDate,
            [new SalesInvoiceLineRequest(
                $"Progress claim {claim.ClaimNumber}: {claim.Description}",
                1m,
                claim.CertifiedAmount,
                claim.VatTreatment,
                claim.RevenueAccountId,
                ProjectId: claim.ProjectId)],
            claim.Project.BranchId,
            claim.Project.DivisionId), cancellationToken);

        claim.SalesInvoiceId = invoice.Id;
        claim.SalesInvoice = invoice;
        claim.Status = ProjectProgressClaimStatus.Invoiced;
        claim.InvoicedAt = DateTimeOffset.UtcNow;
        claim.Project.UpdatedAt = claim.InvoicedAt.Value;
        AddAudit(organisationId, userId, "ProjectProgressClaimInvoiced", claim);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return invoice;
    }

    public async Task CancelAsync(
        string userId,
        Guid organisationId,
        Guid claimId,
        CancellationToken cancellationToken = default)
    {
        await RequireMaintainerAsync(userId, organisationId);
        var claim = await LoadClaimAsync(userId, organisationId, claimId, cancellationToken);
        if (claim.Status is not (ProjectProgressClaimStatus.Draft or
            ProjectProgressClaimStatus.Submitted))
        {
            throw new InvalidOperationException(
                "Only draft or submitted progress claims can be cancelled.");
        }

        claim.Status = ProjectProgressClaimStatus.Cancelled;
        claim.DecidedByUserId = userId;
        claim.DecidedAt = DateTimeOffset.UtcNow;
        AddAudit(organisationId, userId, "ProjectProgressClaimCancelled", claim);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Project> LoadProjectAsync(
        string userId,
        Guid organisationId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .Include(x => x.Variations)
            .Include(x => x.ProgressClaims)
            .SingleOrDefaultAsync(x =>
                x.Id == projectId && x.OrganisationId == organisationId,
                cancellationToken)
            ?? throw new InvalidOperationException("Project not found.");
        await RequireDimensionAsync(userId, organisationId, project, cancellationToken);
        return project;
    }

    private async Task<ProjectProgressClaim> LoadClaimAsync(
        string userId,
        Guid organisationId,
        Guid claimId,
        CancellationToken cancellationToken)
    {
        var claim = await db.ProjectProgressClaims
            .Include(x => x.Project).ThenInclude(x => x.Variations)
            .Include(x => x.Project).ThenInclude(x => x.ProgressClaims)
            .Include(x => x.SalesInvoice)
            .SingleOrDefaultAsync(x =>
                x.Id == claimId && x.Project.OrganisationId == organisationId,
                cancellationToken)
            ?? throw new InvalidOperationException("Progress claim not found.");
        await RequireDimensionAsync(userId, organisationId, claim.Project, cancellationToken);
        return claim;
    }

    private static void ValidateApproval(ProjectProgressClaim claim)
    {
        var priorClaims = claim.Project.ProgressClaims.Where(x =>
            x.Id != claim.Id &&
            x.Status is ProjectProgressClaimStatus.Approved or ProjectProgressClaimStatus.Invoiced);
        var priorWork = priorClaims.Sum(x => x.WorkCompletedAmount);
        if (priorWork + claim.WorkCompletedAmount > claim.Project.RevisedContractValue)
        {
            throw new InvalidOperationException(
                "Approving this claim would exceed the revised contract value.");
        }

        var outstandingRetention = priorClaims.Sum(x =>
            x.RetentionHeldAmount - x.RetentionReleasedAmount);
        if (claim.RetentionReleasedAmount > outstandingRetention)
        {
            throw new InvalidOperationException(
                "Retention released cannot exceed the project's outstanding retention.");
        }
    }

    private async Task RequireDimensionAsync(
        string userId,
        Guid organisationId,
        Project project,
        CancellationToken cancellationToken)
    {
        if (!await access.CanAccessDimensionAsync(
                userId, organisationId, project.BranchId, project.DivisionId,
                cancellationToken))
        {
            throw new UnauthorizedAccessException(
                "You cannot maintain progress claims for this project.");
        }
    }

    private async Task RequireMaintainerAsync(string userId, Guid organisationId)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot maintain project progress claims for this organisation.");
        }
    }

    private static string Required(string value, string label, int maxLength)
    {
        var cleaned = value.Trim();
        if (cleaned.Length == 0 || cleaned.Length > maxLength)
        {
            throw new InvalidOperationException(
                $"{label} is required and cannot exceed {maxLength} characters.");
        }
        return cleaned;
    }

    private static string? Optional(string? value, string label, int maxLength)
    {
        var cleaned = value?.Trim();
        if (string.IsNullOrEmpty(cleaned)) return null;
        if (cleaned.Length > maxLength)
        {
            throw new InvalidOperationException(
                $"{label} cannot exceed {maxLength} characters.");
        }
        return cleaned;
    }

    private void AddAudit(
        Guid organisationId,
        string userId,
        string eventType,
        ProjectProgressClaim claim) =>
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = eventType,
            EntityType = nameof(ProjectProgressClaim),
            EntityId = claim.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                claim.ProjectId,
                claim.ClaimNumber,
                claim.ClaimPeriodEnd,
                claim.WorkCompletedAmount,
                claim.RetentionRate,
                claim.RetentionHeldAmount,
                claim.RetentionReleasedAmount,
                claim.CertifiedAmount,
                claim.RevenueAccountId,
                VatTreatment = claim.VatTreatment.ToString(),
                Status = claim.Status.ToString(),
                claim.SalesInvoiceId,
                claim.CreatedByUserId,
                claim.DecidedByUserId,
                claim.DecisionReason,
                claim.SubmittedAt,
                claim.DecidedAt,
                claim.InvoicedAt
            })
        });
}
