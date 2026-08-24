using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record ProjectRequest(
    Guid OrganisationId,
    Guid? ProjectId,
    string ProjectNumber,
    string Name,
    string? Description,
    Guid DivisionId,
    Guid? CustomerId,
    DateOnly StartDate,
    DateOnly? ExpectedCompletionDate,
    decimal OriginalContractValue,
    decimal ApprovedVariationValue,
    decimal ForecastCost,
    decimal RetentionPercent);

public sealed record ProjectCostCodeRequest(
    Guid OrganisationId,
    Guid ProjectId,
    string Code,
    string Name,
    decimal BudgetAmount);

public sealed class ProjectService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public async Task<List<Project>> ListAsync(
        string userId,
        Guid organisationId,
        CancellationToken cancellationToken = default)
    {
        if (await access.FindAsync(userId, organisationId) is null)
        {
            throw new UnauthorizedAccessException(
                "You cannot view projects for this organisation.");
        }

        var divisionScope = await access.GetReportDivisionScopeAsync(
            userId, organisationId, cancellationToken);
        var query = db.Projects.AsNoTracking()
            .Include(x => x.Branch)
            .Include(x => x.Division)
            .Include(x => x.Customer)
            .Include(x => x.CostCodes.OrderBy(code => code.Code))
            .Where(x => x.OrganisationId == organisationId);
        if (divisionScope is not null)
        {
            query = query.Where(x => divisionScope.Contains(x.DivisionId));
        }

        return await query
            .OrderBy(x => x.Status == ProjectStatus.Completed || x.Status == ProjectStatus.Cancelled)
            .ThenBy(x => x.ProjectNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<Project> SaveAsync(
        string userId,
        ProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireMaintainerAsync(userId, request.OrganisationId);
        var projectNumber = Required(request.ProjectNumber, "Project number", 40);
        var name = Required(request.Name, "Project name", 160);
        var description = Optional(request.Description, 1000);
        ValidateFinancials(request);

        var division = await db.Divisions.AsNoTracking()
            .Include(x => x.Branch)
            .SingleOrDefaultAsync(
                x => x.Id == request.DivisionId &&
                     x.IsActive &&
                     x.Branch.IsActive &&
                     x.Branch.OrganisationId == request.OrganisationId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Select an active division in this organisation.");
        if (!await access.CanAccessDimensionAsync(
                userId,
                request.OrganisationId,
                division.BranchId,
                division.Id,
                cancellationToken))
        {
            throw new UnauthorizedAccessException(
                "You cannot maintain projects for this division.");
        }

        if (request.CustomerId is Guid customerId &&
            !await db.BusinessParties.AnyAsync(
                x => x.Id == customerId &&
                     x.OrganisationId == request.OrganisationId &&
                     x.IsActive &&
                     (x.Type & PartyType.Customer) != 0,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Select an active customer in this organisation.");
        }

        var duplicateNumber = await db.Projects.AnyAsync(
            x => x.OrganisationId == request.OrganisationId &&
                 x.ProjectNumber == projectNumber &&
                 x.Id != request.ProjectId,
            cancellationToken);
        if (duplicateNumber)
        {
            throw new InvalidOperationException(
                "Project number is already in use.");
        }

        Project project;
        string eventType;
        if (request.ProjectId is Guid projectId)
        {
            project = await db.Projects.SingleOrDefaultAsync(
                x => x.Id == projectId && x.OrganisationId == request.OrganisationId,
                cancellationToken)
                ?? throw new InvalidOperationException("Project not found.");
            if (project.Status is ProjectStatus.Completed or ProjectStatus.Cancelled)
            {
                throw new InvalidOperationException(
                    "Completed or cancelled projects cannot be edited.");
            }
            eventType = "ProjectUpdated";
        }
        else
        {
            project = new Project
            {
                OrganisationId = request.OrganisationId,
                ProjectNumber = projectNumber,
                Name = name,
                BranchId = division.BranchId,
                DivisionId = division.Id,
                StartDate = request.StartDate,
                CreatedByUserId = userId
            };
            db.Projects.Add(project);
            eventType = "ProjectCreated";
        }

        project.ProjectNumber = projectNumber;
        project.Name = name;
        project.Description = description;
        project.BranchId = division.BranchId;
        project.DivisionId = division.Id;
        project.CustomerId = request.CustomerId;
        project.StartDate = request.StartDate;
        project.ExpectedCompletionDate = request.ExpectedCompletionDate;
        project.OriginalContractValue = request.OriginalContractValue;
        project.ApprovedVariationValue = request.ApprovedVariationValue;
        project.ForecastCost = request.ForecastCost;
        project.RetentionPercent = request.RetentionPercent;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        AddAudit(request.OrganisationId, userId, eventType, project, new
        {
            project.ProjectNumber,
            project.Name,
            project.BranchId,
            project.DivisionId,
            project.CustomerId,
            project.OriginalContractValue,
            project.ApprovedVariationValue,
            project.ForecastCost,
            project.RetentionPercent
        });
        await db.SaveChangesAsync(cancellationToken);
        return project;
    }

    public async Task<ProjectCostCode> AddCostCodeAsync(
        string userId,
        ProjectCostCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        await RequireMaintainerAsync(userId, request.OrganisationId);
        var code = Required(request.Code, "Cost code", 30);
        var name = Required(request.Name, "Cost code name", 120);
        if (request.BudgetAmount < 0)
        {
            throw new InvalidOperationException(
                "Cost code budget cannot be negative.");
        }

        var project = await db.Projects.SingleOrDefaultAsync(
            x => x.Id == request.ProjectId && x.OrganisationId == request.OrganisationId,
            cancellationToken)
            ?? throw new InvalidOperationException("Project not found.");
        if (project.Status is ProjectStatus.Completed or ProjectStatus.Cancelled)
        {
            throw new InvalidOperationException(
                "Cost codes cannot be added to a closed project.");
        }
        if (!await access.CanAccessDimensionAsync(
                userId,
                request.OrganisationId,
                project.BranchId,
                project.DivisionId,
                cancellationToken))
        {
            throw new UnauthorizedAccessException(
                "You cannot maintain this project's cost codes.");
        }
        if (await db.ProjectCostCodes.AnyAsync(
                x => x.ProjectId == project.Id && x.Code == code,
                cancellationToken))
        {
            throw new InvalidOperationException(
                "Cost code is already in use for this project.");
        }

        var costCode = new ProjectCostCode
        {
            ProjectId = project.Id,
            Code = code,
            Name = name,
            BudgetAmount = request.BudgetAmount
        };
        db.ProjectCostCodes.Add(costCode);
        AddAudit(request.OrganisationId, userId, "ProjectCostCodeCreated", project, new
        {
            CostCodeId = costCode.Id,
            costCode.Code,
            costCode.Name,
            costCode.BudgetAmount
        });
        await db.SaveChangesAsync(cancellationToken);
        return costCode;
    }

    public async Task<Project> ChangeStatusAsync(
        string userId,
        Guid organisationId,
        Guid projectId,
        ProjectStatus newStatus,
        CancellationToken cancellationToken = default)
    {
        await RequireMaintainerAsync(userId, organisationId);
        var project = await db.Projects.SingleOrDefaultAsync(
            x => x.Id == projectId && x.OrganisationId == organisationId,
            cancellationToken)
            ?? throw new InvalidOperationException("Project not found.");
        if (!await access.CanAccessDimensionAsync(
                userId, organisationId, project.BranchId, project.DivisionId, cancellationToken))
        {
            throw new UnauthorizedAccessException(
                "You cannot maintain this project.");
        }
        if (!AllowedTransition(project.Status, newStatus))
        {
            throw new InvalidOperationException(
                $"Project cannot move from {project.Status} to {newStatus}.");
        }

        var previousStatus = project.Status;
        project.Status = newStatus;
        project.CompletedDate = newStatus == ProjectStatus.Completed
            ? DateOnly.FromDateTime(DateTime.Today)
            : null;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        AddAudit(organisationId, userId, "ProjectStatusChanged", project, new
        {
            PreviousStatus = previousStatus.ToString(),
            NewStatus = newStatus.ToString(),
            project.CompletedDate
        });
        await db.SaveChangesAsync(cancellationToken);
        return project;
    }

    private async Task RequireMaintainerAsync(string userId, Guid organisationId)
    {
        if (!await access.CanPostJournalsAsync(userId, organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot maintain projects for this organisation.");
        }
    }

    private static void ValidateFinancials(ProjectRequest request)
    {
        if (request.ExpectedCompletionDate < request.StartDate)
        {
            throw new InvalidOperationException(
                "Expected completion cannot precede the project start date.");
        }
        if (request.OriginalContractValue < 0 ||
            request.ApprovedVariationValue < 0 ||
            request.ForecastCost < 0 ||
            request.RetentionPercent is < 0 or > 100)
        {
            throw new InvalidOperationException(
                "Project values must be non-negative and retention must be between 0 and 100 percent.");
        }
    }

    private static bool AllowedTransition(ProjectStatus current, ProjectStatus next) =>
        (current, next) switch
        {
            (ProjectStatus.Draft, ProjectStatus.Active or ProjectStatus.Cancelled) => true,
            (ProjectStatus.Active, ProjectStatus.OnHold or ProjectStatus.Completed or ProjectStatus.Cancelled) => true,
            (ProjectStatus.OnHold, ProjectStatus.Active or ProjectStatus.Cancelled) => true,
            _ => false
        };

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

    private static string? Optional(string? value, int maxLength)
    {
        var cleaned = value?.Trim();
        if (string.IsNullOrEmpty(cleaned)) return null;
        if (cleaned.Length > maxLength)
        {
            throw new InvalidOperationException(
                $"Description cannot exceed {maxLength} characters.");
        }
        return cleaned;
    }

    private void AddAudit(
        Guid organisationId,
        string userId,
        string eventType,
        Project project,
        object evidence) =>
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = eventType,
            EntityType = nameof(Project),
            EntityId = project.Id.ToString(),
            JsonData = JsonSerializer.Serialize(evidence)
        });
}
