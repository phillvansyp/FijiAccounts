using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record ProjectCoding(Guid? ProjectId, Guid? ProjectCostCodeId);

public static class ProjectCodingValidator
{
    public static async Task ValidateAsync(
        ApplicationDbContext db,
        Guid organisationId,
        Guid? branchId,
        Guid? divisionId,
        IEnumerable<ProjectCoding> coding,
        bool allowCompleted = false,
        CancellationToken cancellationToken = default)
    {
        var allocations = coding.ToList();
        if (allocations.Any(x => x.ProjectCostCodeId is not null && x.ProjectId is null))
        {
            throw new InvalidOperationException("A project cost code requires a project.");
        }

        var projectIds = allocations.Where(x => x.ProjectId is not null)
            .Select(x => x.ProjectId!.Value).Distinct().ToArray();
        if (projectIds.Length == 0) return;
        if (branchId is null || divisionId is null)
        {
            throw new InvalidOperationException(
                "Project coding requires a branch and division.");
        }

        var projects = await db.Projects.AsNoTracking()
            .Include(x => x.CostCodes)
            .Where(x => x.OrganisationId == organisationId && projectIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (projects.Count != projectIds.Length)
        {
            throw new InvalidOperationException(
                "Every project must belong to the selected organisation.");
        }

        foreach (var allocation in allocations.Where(x => x.ProjectId is not null))
        {
            var project = projects[allocation.ProjectId!.Value];
            var statusAllowed = project.Status is ProjectStatus.Active or ProjectStatus.OnHold ||
                allowCompleted && project.Status == ProjectStatus.Completed;
            if (!statusAllowed)
            {
                throw new InvalidOperationException(
                    allowCompleted
                        ? "Transactions can only use active, on-hold, or completed projects."
                        : "Documents can only use active or on-hold projects.");
            }
            if (project.BranchId != branchId || project.DivisionId != divisionId)
            {
                throw new InvalidOperationException(
                    "The project must belong to the document's branch and division.");
            }
            if (allocation.ProjectCostCodeId is Guid costCodeId &&
                !project.CostCodes.Any(x => x.Id == costCodeId && x.IsActive))
            {
                throw new InvalidOperationException(
                    "The project cost code must be active and belong to the selected project.");
            }
        }
    }
}
