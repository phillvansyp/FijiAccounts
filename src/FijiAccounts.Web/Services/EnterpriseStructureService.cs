using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record DefaultEnterpriseStructure(
    OrganisationGroup Group,
    Branch Branch,
    Division Division);

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

    public DefaultEnterpriseStructure AddDefaultFor(
        Organisation company)
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
                Name = $"{company.LegalName.Trim()} Group"
            };

        var branch =
            new Branch
            {
                Organisation = company,
                Code = "MAIN",
                Name = "Main Branch",
                IsDefault = true
            };

        var division =
            new Division
            {
                Branch = branch,
                Code = "GENERAL",
                Name = "General",
                IsDefault = true
            };

        company.OrganisationGroup = group;

        db.OrganisationGroups.Add(group);
        db.Branches.Add(branch);
        db.Divisions.Add(division);

        return new(
            group,
            branch,
            division);
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
}
