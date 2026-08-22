using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record DefaultEnterpriseStructure(
    OrganisationGroup Group,
    Branch Branch,
    Division Division);

public sealed record EnterpriseGroupDetails(
    Guid Id,
    string Name,
    string PresentationCurrency,
    OrganisationGroupRole Role,
    IReadOnlyList<Organisation> Companies);

public sealed record CreateGroupCompanyRequest(
    Guid CurrentOrganisationId,
    string LegalName,
    string? TradingName,
    string? Tin,
    string CountryCode,
    OrganisationKind Kind);

public sealed record EnterprisePostingDimension(
    Guid BranchId,
    Guid DivisionId);

public sealed class EnterpriseStructureService(
    ApplicationDbContext db)
{
    public async Task<List<Branch>> ListBranchesAsync(
        Guid organisationId,
        CancellationToken ct = default) =>
        await db.Branches
            .AsNoTracking()
            .Include(x => x.Divisions)
            .Where(x => x.OrganisationId == organisationId)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Code)
            .ToListAsync(ct);

    public async Task<EnterprisePostingDimension> ResolveActiveDimensionAsync(
        Guid organisationId,
        Guid? requestedBranchId,
        Guid? requestedDivisionId,
        CancellationToken ct = default)
    {
        var activeBranches =
            await db.Branches
                .AsNoTracking()
                .Include(x => x.Divisions.Where(division => division.IsActive))
                .Where(x =>
                    x.OrganisationId == organisationId &&
                    x.IsActive)
                .ToListAsync(ct);
        Branch? branch = null;
        Division? division = null;

        if (requestedDivisionId is Guid divisionId)
        {
            branch = activeBranches.SingleOrDefault(x =>
                x.Divisions.Any(candidate => candidate.Id == divisionId));
            division = branch?.Divisions.Single(x => x.Id == divisionId);

            if (division is null)
            {
                throw new InvalidOperationException(
                    "The selected division must be active and belong to this organisation.");
            }
        }

        if (requestedBranchId is Guid branchId)
        {
            var selectedBranch =
                activeBranches.SingleOrDefault(x => x.Id == branchId)
                ?? throw new InvalidOperationException(
                    "The selected branch must be active and belong to this organisation.");

            if (branch is not null && branch.Id != selectedBranch.Id)
            {
                throw new InvalidOperationException(
                    "The selected division does not belong to the selected branch.");
            }

            branch = selectedBranch;
        }

        branch ??= activeBranches.SingleOrDefault(x => x.IsDefault)
            ?? throw new InvalidOperationException(
                "An active default branch is required before transactions can be posted.");
        division ??= branch.Divisions.SingleOrDefault(x => x.IsDefault)
            ?? throw new InvalidOperationException(
                "An active default division is required for the selected branch.");

        return new EnterprisePostingDimension(branch.Id, division.Id);
    }

    public DefaultEnterpriseStructure AddDefaultFor(
        Organisation company,
        string? ownerUserId = null)
    {
        if (company.OrganisationGroupId is not null ||
            company.OrganisationGroup is not null)
        {
            throw new InvalidOperationException(
                "The company already belongs to an organisation group.");
        }

        var group =
            new OrganisationGroup
            {
                Name = $"{company.LegalName.Trim()} Group",
                PresentationCurrency = company.BaseCurrency
            };

        var branch = CreateDefaultBranch(company);
        var division = branch.Divisions.Single();

        company.OrganisationGroup = group;

        db.OrganisationGroups.Add(group);
        if (!string.IsNullOrWhiteSpace(ownerUserId))
        {
            db.OrganisationGroupMemberships.Add(
                new OrganisationGroupMembership
                {
                    OrganisationGroup = group,
                    UserId = ownerUserId,
                    Role = OrganisationGroupRole.Owner
                });
        }
        db.Branches.Add(branch);

        return new(
            group,
            branch,
            division);
    }

    public async Task<EnterpriseGroupDetails?> GetGroupAsync(
        string userId,
        Guid currentOrganisationId,
        CancellationToken ct = default)
    {
        var membership =
            await db.OrganisationGroupMemberships
                .AsNoTracking()
                .Where(x =>
                    x.UserId == userId &&
                    x.OrganisationGroup.Companies.Any(company =>
                        company.Id == currentOrganisationId))
                .Select(x => new
                {
                    x.OrganisationGroupId,
                    x.OrganisationGroup.Name,
                    x.OrganisationGroup.PresentationCurrency,
                    x.Role,
                    Companies = x.OrganisationGroup.Companies
                        .OrderBy(company => company.LegalName)
                        .ToList()
                })
                .SingleOrDefaultAsync(ct);

        return membership is null
            ? null
            : new EnterpriseGroupDetails(
                membership.OrganisationGroupId,
                membership.Name,
                membership.PresentationCurrency,
                membership.Role,
                membership.Companies);
    }

    public async Task UpdateGroupNameAsync(
        string userId,
        Guid currentOrganisationId,
        string name,
        CancellationToken ct = default)
    {
        var normalisedName = NormaliseGroupName(name);
        var groupId = await RequireGroupManagerAsync(
            userId,
            currentOrganisationId,
            ct);

        var changed =
            await db.OrganisationGroups
                .Where(x => x.Id == groupId)
                .ExecuteUpdateAsync(
                    update => update.SetProperty(x => x.Name, normalisedName),
                    ct);

        if (changed != 1)
        {
            throw new InvalidOperationException(
                "The organisation group could not be updated.");
        }
    }

    public async Task<Organisation> AddCompanyAsync(
        string userId,
        CreateGroupCompanyRequest request,
        CancellationToken ct = default)
    {
        var groupId = await RequireGroupManagerAsync(
            userId,
            request.CurrentOrganisationId,
            ct);
        var groupRole =
            await db.OrganisationGroupMemberships
                .Where(x =>
                    x.OrganisationGroupId == groupId &&
                    x.UserId == userId)
                .Select(x => x.Role)
                .SingleAsync(ct);
        var jurisdiction = IslandJurisdictions.Get(request.CountryCode);
        var legalName = NormaliseCompanyName(request.LegalName);

        if (await db.Organisations.AnyAsync(
                x =>
                    x.OrganisationGroupId == groupId &&
                    x.LegalName == legalName,
                ct))
        {
            throw new InvalidOperationException(
                "A company with that legal name already exists in the group.");
        }

        var company =
            new Organisation
            {
                OrganisationGroupId = groupId,
                LegalName = legalName,
                TradingName = NullIfWhiteSpace(request.TradingName),
                Tin = NullIfWhiteSpace(request.Tin),
                Kind = request.Kind,
                CountryCode = jurisdiction.CountryCode,
                BaseCurrency = jurisdiction.CurrencyCode,
                TimeZoneId = jurisdiction.TimeZoneId,
                TaxLabel = jurisdiction.TaxLabel,
                FinancialYearEndMonth = jurisdiction.FinancialYearEndMonth,
                FinancialYearEndDay = jurisdiction.FinancialYearEndDay
            };
        var branch = CreateDefaultBranch(company);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.Organisations.Add(company);
        db.Branches.Add(branch);
        db.OrganisationMemberships.Add(
            new OrganisationMembership
            {
                Organisation = company,
                UserId = userId,
                Role = groupRole == OrganisationGroupRole.Owner
                    ? OrganisationRole.Owner
                    : OrganisationRole.Administrator
            });
        db.LedgerAccounts.AddRange(FijiStarterChart.For(company.Id));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return company;
    }

    public async Task<Branch> AddBranchAsync(
        Guid organisationId,
        string code,
        string name,
        CancellationToken ct = default)
    {
        var normalisedCode = NormaliseCode(code);
        var normalisedName = NormaliseName(name);

        if (!await db.Organisations.AnyAsync(
                x => x.Id == organisationId,
                ct))
        {
            throw new InvalidOperationException(
                "The company was not found.");
        }

        if (await db.Branches.AnyAsync(
                x =>
                    x.OrganisationId == organisationId &&
                    (x.Code == normalisedCode ||
                     x.Name == normalisedName),
                ct))
        {
            throw new InvalidOperationException(
                "That branch code or name already exists.");
        }

        var branch =
            new Branch
            {
                OrganisationId = organisationId,
                Code = normalisedCode,
                Name = normalisedName,
                Divisions =
                [
                    new Division
                    {
                        Code = "GENERAL",
                        Name = "General",
                        IsDefault = true
                    }
                ]
            };

        db.Branches.Add(branch);
        await db.SaveChangesAsync(ct);

        return branch;
    }

    public async Task<Division> AddDivisionAsync(
        Guid organisationId,
        Guid branchId,
        string code,
        string name,
        CancellationToken ct = default)
    {
        var normalisedCode = NormaliseCode(code);
        var normalisedName = NormaliseName(name);

        var branchIsActive =
            await db.Branches.AnyAsync(
                x =>
                    x.Id == branchId &&
                    x.OrganisationId == organisationId &&
                    x.IsActive,
                ct);

        if (!branchIsActive)
        {
            throw new InvalidOperationException(
                "An active branch was not found for this company.");
        }

        if (await db.Divisions.AnyAsync(
                x =>
                    x.BranchId == branchId &&
                    (x.Code == normalisedCode ||
                     x.Name == normalisedName),
                ct))
        {
            throw new InvalidOperationException(
                "That division code or name already exists in the branch.");
        }

        var division =
            new Division
            {
                BranchId = branchId,
                Code = normalisedCode,
                Name = normalisedName
            };

        db.Divisions.Add(division);
        await db.SaveChangesAsync(ct);

        return division;
    }

    public async Task ToggleBranchAsync(
        Guid organisationId,
        Guid branchId,
        CancellationToken ct = default)
    {
        var branch =
            await db.Branches.SingleOrDefaultAsync(
                x =>
                    x.Id == branchId &&
                    x.OrganisationId == organisationId,
                ct)
            ?? throw new InvalidOperationException(
                "The branch was not found for this company.");

        if (branch.IsDefault)
        {
            throw new InvalidOperationException(
                "The default branch must remain active.");
        }

        branch.IsActive = !branch.IsActive;
        await db.SaveChangesAsync(ct);
    }

    public async Task ToggleDivisionAsync(
        Guid organisationId,
        Guid divisionId,
        CancellationToken ct = default)
    {
        var division =
            await db.Divisions
                .Include(x => x.Branch)
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == divisionId &&
                        x.Branch.OrganisationId == organisationId,
                    ct)
            ?? throw new InvalidOperationException(
                "The division was not found for this company.");

        if (division.IsDefault)
        {
            throw new InvalidOperationException(
                "The default division must remain active.");
        }

        division.IsActive = !division.IsActive;
        await db.SaveChangesAsync(ct);
    }

    private static string NormaliseCode(
        string value)
    {
        var result = value.Trim().ToUpperInvariant();

        if (result.Length is < 1 or > 20)
        {
            throw new InvalidOperationException(
                "Enter a code between 1 and 20 characters.");
        }

        return result;
    }

    private static string NormaliseName(
        string value)
    {
        var result = value.Trim();

        if (result.Length is < 1 or > 120)
        {
            throw new InvalidOperationException(
                "Enter a name between 1 and 120 characters.");
        }

        return result;
    }

    private async Task<Guid> RequireGroupManagerAsync(
        string userId,
        Guid currentOrganisationId,
        CancellationToken ct)
    {
        var groupId =
            await db.OrganisationGroupMemberships
                .Where(x =>
                    x.UserId == userId &&
                    x.Role != OrganisationGroupRole.Viewer &&
                    x.OrganisationGroup.Companies.Any(company =>
                        company.Id == currentOrganisationId))
                .Select(x => (Guid?)x.OrganisationGroupId)
                .SingleOrDefaultAsync(ct);

        return groupId ?? throw new UnauthorizedAccessException(
            "You do not have permission to manage this organisation group.");
    }

    private static Branch CreateDefaultBranch(Organisation company) =>
        new()
        {
            Organisation = company,
            Code = "MAIN",
            Name = "Main Branch",
            IsDefault = true,
            Divisions =
            [
                new Division
                {
                    Code = "GENERAL",
                    Name = "General",
                    IsDefault = true
                }
            ]
        };

    private static string NormaliseGroupName(string value)
    {
        var result = value.Trim();
        if (result.Length is < 1 or > 160)
        {
            throw new InvalidOperationException(
                "Enter a group name between 1 and 160 characters.");
        }

        return result;
    }

    private static string NormaliseCompanyName(string value)
    {
        var result = value.Trim();
        if (result.Length is < 1 or > 160)
        {
            throw new InvalidOperationException(
                "Enter a legal name between 1 and 160 characters.");
        }

        return result;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
