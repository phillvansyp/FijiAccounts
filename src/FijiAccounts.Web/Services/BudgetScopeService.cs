using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record BudgetScope(
    string Key,
    string Label,
    Guid? BranchId,
    Guid? DivisionId);

public sealed class BudgetScopeService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public async Task<BudgetScope> ResolveAsync(
        string userId,
        Guid organisationId,
        Guid? branchId,
        Guid? divisionId,
        CancellationToken cancellationToken = default)
    {
        if (await access.FindAsync(userId, organisationId) is null)
        {
            throw new UnauthorizedAccessException(
                "You cannot access budgets for this organisation.");
        }

        if (divisionId is not null && branchId is null)
        {
            throw new InvalidOperationException(
                "A division budget must identify its branch.");
        }

        Branch? branch = null;
        Division? division = null;
        if (branchId is Guid selectedBranchId)
        {
            branch = await db.Branches.AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.Id == selectedBranchId &&
                         x.OrganisationId == organisationId &&
                         x.IsActive,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "Select an active branch in this organisation.");
        }

        if (divisionId is Guid selectedDivisionId)
        {
            division = await db.Divisions.AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.Id == selectedDivisionId &&
                         x.BranchId == branchId &&
                         x.IsActive,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "Select an active division in the selected branch.");
        }

        var permittedDivisionIds = await access.GetReportDivisionScopeAsync(
            userId, organisationId, cancellationToken);
        if (permittedDivisionIds is not null)
        {
            if (division is not null && !permittedDivisionIds.Contains(division.Id))
            {
                throw new UnauthorizedAccessException(
                    "You cannot access budgets for this division.");
            }

            if (branch is not null && division is null)
            {
                var branchDivisionIds = await db.Divisions.AsNoTracking()
                    .Where(x => x.BranchId == branch.Id && x.IsActive)
                    .Select(x => x.Id)
                    .ToListAsync(cancellationToken);
                if (branchDivisionIds.Count == 0 ||
                    branchDivisionIds.Any(x => !permittedDivisionIds.Contains(x)))
                {
                    throw new UnauthorizedAccessException(
                        "You cannot access the complete budget for this branch.");
                }
            }

            if (branch is null)
            {
                throw new UnauthorizedAccessException(
                    "Select a branch or division within your reporting access.");
            }
        }

        if (division is not null)
        {
            return new(
                $"division:{division.Id:N}",
                $"{branch!.Code} — {division.Code} — {division.Name}",
                branch.Id,
                division.Id);
        }

        if (branch is not null)
        {
            return new(
                $"branch:{branch.Id:N}",
                $"{branch.Code} — {branch.Name}",
                branch.Id,
                null);
        }

        return new("organisation", "Whole organisation", null, null);
    }
}
