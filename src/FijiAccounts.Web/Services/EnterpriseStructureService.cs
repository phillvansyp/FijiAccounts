using System.Text.Json;
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

public sealed record CreateStandaloneCompanyRequest(
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
    public async Task<Organisation> CreateStandaloneCompanyAsync(
        string userId,
        CreateStandaloneCompanyRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) ||
            !await db.Users.AsNoTracking().AnyAsync(x => x.Id == userId, ct))
        {
            throw new UnauthorizedAccessException(
                "You must be signed in to create an organisation.");
        }

        if (!Enum.IsDefined(request.Kind))
        {
            throw new InvalidOperationException("Select a valid organisation type.");
        }

        var jurisdiction = IslandJurisdictions.Get(request.CountryCode);
        var company = new Organisation
        {
            LegalName = NormaliseCompanyName(request.LegalName),
            TradingName = NormaliseOptionalText(request.TradingName, 80, "trading name"),
            Tin = NormaliseOptionalText(request.Tin, 32, "tax identification number"),
            Kind = request.Kind,
            CountryCode = jurisdiction.CountryCode,
            BaseCurrency = jurisdiction.CurrencyCode,
            TimeZoneId = jurisdiction.TimeZoneId,
            TaxLabel = jurisdiction.TaxLabel,
            FinancialYearEndMonth = jurisdiction.FinancialYearEndMonth,
            FinancialYearEndDay = jurisdiction.FinancialYearEndDay
        };

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.Organisations.Add(company);
        var structure = AddDefaultFor(company, userId);
        db.OrganisationMemberships.Add(new OrganisationMembership
        {
            Organisation = company,
            UserId = userId,
            Role = OrganisationRole.Owner
        });
        db.LedgerAccounts.AddRange(StarterCharts.For(company.Id, company.CountryCode));
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = company.Id,
            UserId = userId,
            EventType = "OrganisationCreated",
            EntityType = nameof(Organisation),
            EntityId = company.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                company.LegalName,
                company.TradingName,
                company.Tin,
                Kind = company.Kind.ToString(),
                company.CountryCode,
                company.BaseCurrency,
                OrganisationGroupId = structure.Group.Id,
                DefaultBranchId = structure.Branch.Id,
                DefaultDivisionId = structure.Division.Id
            })
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return company;
    }

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
        var group =
            await db.OrganisationGroups
                .AsNoTracking()
                .Where(x => x.Companies.Any(company =>
                        company.Id == currentOrganisationId))
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.PresentationCurrency,
                    Companies = x.Companies
                        .OrderBy(company => company.LegalName)
                        .ToList()
                })
                .SingleOrDefaultAsync(ct);

        if (group is null)
        {
            return null;
        }

        var role = await ResolveGroupRoleAsync(userId, group.Id, ct);

        return role is null
            ? null
            : new EnterpriseGroupDetails(
                group.Id,
                group.Name,
                group.PresentationCurrency,
                role.Value,
                group.Companies);
    }

    public async Task UpdateGroupNameAsync(
        string userId,
        Guid currentOrganisationId,
        string name,
        CancellationToken ct = default)
    {
        var normalisedName = NormaliseGroupName(name);
        var groupAccess = await RequireGroupManagerAsync(
            userId,
            currentOrganisationId,
            ct);
        var group = await db.OrganisationGroups.SingleOrDefaultAsync(
            x => x.Id == groupAccess.GroupId,
            ct);
        if (group is null)
        {
            throw new InvalidOperationException(
                "The organisation group could not be updated.");
        }

        if (group.Name == normalisedName)
        {
            return;
        }

        var oldName = group.Name;
        group.Name = normalisedName;
        db.AuditEvents.Add(StructureAudit(
            currentOrganisationId,
            userId,
            "OrganisationGroupRenamed",
            nameof(OrganisationGroup),
            group.Id,
            new { OldName = oldName, NewName = group.Name }));
        await db.SaveChangesAsync(ct);
    }

    public async Task<Organisation> AddCompanyAsync(
        string userId,
        CreateGroupCompanyRequest request,
        CancellationToken ct = default)
    {
        var groupAccess = await RequireGroupManagerAsync(
            userId,
            request.CurrentOrganisationId,
            ct);
        var jurisdiction = IslandJurisdictions.Get(request.CountryCode);
        var legalName = NormaliseCompanyName(request.LegalName);
        if (!Enum.IsDefined(request.Kind))
        {
            throw new InvalidOperationException("Select a valid organisation type.");
        }

        if (await db.Organisations.AnyAsync(
                x =>
                    x.OrganisationGroupId == groupAccess.GroupId &&
                    x.LegalName == legalName,
                ct))
        {
            throw new InvalidOperationException(
                "A company with that legal name already exists in the group.");
        }

        var company =
            new Organisation
            {
                OrganisationGroupId = groupAccess.GroupId,
                LegalName = legalName,
                TradingName = NormaliseOptionalText(request.TradingName, 80, "trading name"),
                Tin = NormaliseOptionalText(request.Tin, 32, "tax identification number"),
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
                Role = groupAccess.Role == OrganisationGroupRole.Owner
                    ? OrganisationRole.Owner
                    : OrganisationRole.Administrator
            });
        db.LedgerAccounts.AddRange(StarterCharts.For(company.Id, company.CountryCode));
        db.AuditEvents.Add(StructureAudit(
            company.Id,
            userId,
            "OrganisationCreated",
            nameof(Organisation),
            company.Id,
            new
            {
                company.LegalName,
                company.TradingName,
                company.Tin,
                Kind = company.Kind.ToString(),
                company.CountryCode,
                company.BaseCurrency,
                OrganisationGroupId = groupAccess.GroupId,
                DefaultBranchId = branch.Id,
                DefaultDivisionId = branch.Divisions.Single().Id,
                CreatedWithinGroup = true
            }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return company;
    }

    public async Task<Branch> AddBranchAsync(
        string userId,
        Guid organisationId,
        string code,
        string name,
        CancellationToken ct = default)
    {
        await RequireCompanyManagerAsync(userId, organisationId, ct);

        var normalisedCode = NormaliseCode(code);
        var normalisedName = NormaliseName(name);

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
        db.AuditEvents.Add(StructureAudit(
            organisationId,
            userId,
            "BranchCreated",
            nameof(Branch),
            branch.Id,
            new
            {
                branch.Code,
                branch.Name,
                DefaultDivisionId = branch.Divisions.Single().Id
            }));
        await db.SaveChangesAsync(ct);

        return branch;
    }

    public async Task<Division> AddDivisionAsync(
        string userId,
        Guid organisationId,
        Guid branchId,
        string code,
        string name,
        CancellationToken ct = default)
    {
        await RequireCompanyManagerAsync(userId, organisationId, ct);

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
        db.AuditEvents.Add(StructureAudit(
            organisationId,
            userId,
            "DivisionCreated",
            nameof(Division),
            division.Id,
            new
            {
                division.BranchId,
                division.Code,
                division.Name
            }));
        await db.SaveChangesAsync(ct);

        return division;
    }

    public async Task ToggleBranchAsync(
        string userId,
        Guid organisationId,
        Guid branchId,
        CancellationToken ct = default)
    {
        await RequireCompanyManagerAsync(userId, organisationId, ct);

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

        var wasActive = branch.IsActive;
        branch.IsActive = !wasActive;
        db.AuditEvents.Add(StructureAudit(
            organisationId,
            userId,
            branch.IsActive ? "BranchReactivated" : "BranchDeactivated",
            nameof(Branch),
            branch.Id,
            new
            {
                branch.Code,
                branch.Name,
                OldIsActive = wasActive,
                NewIsActive = branch.IsActive
            }));
        await db.SaveChangesAsync(ct);
    }

    public async Task ToggleDivisionAsync(
        string userId,
        Guid organisationId,
        Guid divisionId,
        CancellationToken ct = default)
    {
        await RequireCompanyManagerAsync(userId, organisationId, ct);

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

        var wasActive = division.IsActive;
        division.IsActive = !wasActive;
        db.AuditEvents.Add(StructureAudit(
            organisationId,
            userId,
            division.IsActive ? "DivisionReactivated" : "DivisionDeactivated",
            nameof(Division),
            division.Id,
            new
            {
                division.BranchId,
                division.Code,
                division.Name,
                OldIsActive = wasActive,
                NewIsActive = division.IsActive
            }));
        await db.SaveChangesAsync(ct);
    }

    private static AuditEvent StructureAudit(
        Guid organisationId,
        string userId,
        string eventType,
        string entityType,
        Guid entityId,
        object evidence) =>
        new()
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            JsonData = JsonSerializer.Serialize(evidence)
        };

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

    private async Task<GroupManagerAccess> RequireGroupManagerAsync(
        string userId,
        Guid currentOrganisationId,
        CancellationToken ct)
    {
        var groupId =
            await db.Organisations
                .Where(x => x.Id == currentOrganisationId)
                .Select(x => x.OrganisationGroupId)
                .SingleOrDefaultAsync(ct);

        if (groupId is not Guid id)
        {
            throw new UnauthorizedAccessException(
                "You do not have permission to manage this organisation group.");
        }

        var role = await ResolveGroupRoleAsync(userId, id, ct);
        if (role is null or OrganisationGroupRole.Viewer)
        {
            throw new UnauthorizedAccessException(
                "You do not have permission to manage this organisation group.");
        }

        return new GroupManagerAccess(id, role.Value);
    }

    private async Task<OrganisationGroupRole?> ResolveGroupRoleAsync(
        string userId,
        Guid groupId,
        CancellationToken ct)
    {
        var explicitRole =
            await db.OrganisationGroupMemberships
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationGroupId == groupId &&
                    x.UserId == userId)
                .Select(x => (OrganisationGroupRole?)x.Role)
                .SingleOrDefaultAsync(ct);

        if (explicitRole is not null)
        {
            return explicitRole;
        }

        var companyRoles =
            await db.Organisations
                .AsNoTracking()
                .Where(x => x.OrganisationGroupId == groupId)
                .Select(x => db.OrganisationMemberships
                    .Where(membership =>
                        membership.OrganisationId == x.Id &&
                        membership.UserId == userId &&
                        (membership.Role == OrganisationRole.Owner ||
                         membership.Role == OrganisationRole.Administrator))
                    .Select(membership => (OrganisationRole?)membership.Role)
                    .SingleOrDefault())
                .ToListAsync(ct);

        if (companyRoles.Count == 0 || companyRoles.Any(role => role is null))
        {
            return null;
        }

        return companyRoles.All(role => role == OrganisationRole.Owner)
            ? OrganisationGroupRole.Owner
            : OrganisationGroupRole.Administrator;
    }

    private async Task RequireCompanyManagerAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct)
    {
        var canManage =
            await db.OrganisationMemberships
                .AsNoTracking()
                .AnyAsync(x =>
                    x.OrganisationId == organisationId &&
                    x.UserId == userId &&
                    (x.Role == OrganisationRole.Owner ||
                     x.Role == OrganisationRole.Administrator),
                    ct);

        if (!canManage)
        {
            throw new UnauthorizedAccessException(
                "You do not have permission to manage this company's branches and divisions.");
        }
    }

    private sealed record GroupManagerAccess(
        Guid GroupId,
        OrganisationGroupRole Role);

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

    private static string? NormaliseOptionalText(
        string? value,
        int maximumLength,
        string fieldName)
    {
        var result = NullIfWhiteSpace(value);
        if (result?.Length > maximumLength)
        {
            throw new InvalidOperationException(
                $"Enter a {fieldName} no longer than {maximumLength} characters.");
        }

        return result;
    }
}
