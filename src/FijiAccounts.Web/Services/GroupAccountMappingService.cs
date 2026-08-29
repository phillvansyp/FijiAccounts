using System.Text.Json;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record GroupSetupCompany(Guid Id, string LegalName, string BaseCurrency);

public sealed record GroupSetupLedgerAccount(
    Guid Id,
    Guid OrganisationId,
    string Code,
    string Name,
    AccountType Type);

public sealed record GroupAccountMappingView(
    Guid LedgerAccountId,
    Guid GroupLedgerAccountId);

public sealed record IntercompanyAccountConfigurationView(
    Guid Id,
    Guid OrganisationId,
    Guid CounterpartyOrganisationId,
    Guid ReceivableAccountId,
    Guid PayableAccountId,
    Guid RevenueAccountId,
    Guid ExpenseAccountId,
    DateTimeOffset UpdatedAt);

public sealed record GroupAccountSetupView(
    Guid GroupId,
    string GroupName,
    bool CanManage,
    IReadOnlyList<GroupSetupCompany> Companies,
    IReadOnlyList<GroupSetupLedgerAccount> CompanyAccounts,
    IReadOnlyList<GroupLedgerAccount> GroupAccounts,
    IReadOnlyList<GroupAccountMappingView> Mappings,
    IReadOnlyList<IntercompanyAccountConfigurationView> IntercompanyConfigurations)
{
    public int ActiveCompanyAccountCount => CompanyAccounts.Count;
    public int MappedCompanyAccountCount => Mappings.Count;
    public bool MappingComplete => ActiveCompanyAccountCount > 0 &&
                                   MappedCompanyAccountCount == ActiveCompanyAccountCount;
}

public sealed record CreateGroupAccountRequest(
    Guid CurrentOrganisationId,
    string Code,
    string Name,
    AccountType Type,
    GroupAccountPurpose Purpose = GroupAccountPurpose.Standard);

public sealed record SetGroupAccountMappingRequest(
    Guid CurrentOrganisationId,
    Guid LedgerAccountId,
    Guid? GroupLedgerAccountId);

public sealed record SaveIntercompanyAccountConfigurationRequest(
    Guid CurrentOrganisationId,
    Guid OrganisationId,
    Guid CounterpartyOrganisationId,
    Guid ReceivableAccountId,
    Guid PayableAccountId,
    Guid RevenueAccountId,
    Guid ExpenseAccountId);

public sealed class GroupAccountMappingService(ApplicationDbContext db)
{
    public async Task<GroupAccountSetupView> GetAsync(
        string userId,
        Guid currentOrganisationId,
        CancellationToken cancellationToken = default)
    {
        var access = await RequireGroupAsync(
            userId,
            currentOrganisationId,
            requireManager: false,
            cancellationToken);
        var accounts = await db.LedgerAccounts
            .AsNoTracking()
            .Where(x => access.CompanyIds.Contains(x.OrganisationId) && x.IsActive)
            .OrderBy(x => x.Organisation.LegalName)
            .ThenBy(x => x.Code)
            .Select(x => new GroupSetupLedgerAccount(
                x.Id,
                x.OrganisationId,
                x.Code,
                x.Name,
                x.Type))
            .ToListAsync(cancellationToken);
        var groupAccounts = await db.GroupLedgerAccounts
            .AsNoTracking()
            .Where(x => x.OrganisationGroupId == access.Id && x.IsActive)
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
        var mappings = await db.GroupLedgerAccountMappings
            .AsNoTracking()
            .Where(x => x.OrganisationGroupId == access.Id)
            .Select(x => new GroupAccountMappingView(
                x.LedgerAccountId,
                x.GroupLedgerAccountId))
            .ToListAsync(cancellationToken);
        var configurations = await db.IntercompanyAccountConfigurations
            .AsNoTracking()
            .Where(x => x.OrganisationGroupId == access.Id)
            .OrderBy(x => x.Organisation.LegalName)
            .ThenBy(x => x.CounterpartyOrganisation.LegalName)
            .Select(x => new IntercompanyAccountConfigurationView(
                x.Id,
                x.OrganisationId,
                x.CounterpartyOrganisationId,
                x.ReceivableAccountId,
                x.PayableAccountId,
                x.RevenueAccountId,
                x.ExpenseAccountId,
                x.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new(
            access.Id,
            access.Name,
            access.CanManage,
            access.Companies,
            accounts,
            groupAccounts,
            mappings,
            configurations);
    }

    public async Task<GroupLedgerAccount> CreateAsync(
        string userId,
        CreateGroupAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var access = await RequireGroupAsync(
            userId,
            request.CurrentOrganisationId,
            requireManager: true,
            cancellationToken);
        var code = NormaliseCode(request.Code);
        var name = NormaliseName(request.Name);
        ValidatePurpose(request.Type, request.Purpose);
        if (await db.GroupLedgerAccounts.AnyAsync(
                x => x.OrganisationGroupId == access.Id && x.Code == code,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"Group account {code} already exists.");
        }

        var account = new GroupLedgerAccount
        {
            OrganisationGroupId = access.Id,
            Code = code,
            Name = name,
            Type = request.Type,
            Purpose = request.Purpose,
            CreatedByUserId = userId
        };
        db.GroupLedgerAccounts.Add(account);
        db.AuditEvents.Add(Audit(
            request.CurrentOrganisationId,
            userId,
            "GroupLedgerAccountCreated",
            nameof(GroupLedgerAccount),
            account.Id,
            new
            {
                OrganisationGroupId = access.Id,
                account.Code,
                account.Name,
                Type = account.Type.ToString(),
                Purpose = account.Purpose.ToString()
            }));
        await db.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<int> InitialiseFromCompanyAsync(
        string userId,
        Guid currentOrganisationId,
        Guid sourceOrganisationId,
        CancellationToken cancellationToken = default)
    {
        var access = await RequireGroupAsync(
            userId,
            currentOrganisationId,
            requireManager: true,
            cancellationToken);
        if (!access.CompanyIds.Contains(sourceOrganisationId))
        {
            throw new InvalidOperationException(
                "The source company must belong to this organisation group.");
        }

        var companyAccounts = await db.LedgerAccounts
            .Where(x => x.OrganisationId == sourceOrganisationId && x.IsActive)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);
        var groupAccounts = await db.GroupLedgerAccounts
            .Where(x => x.OrganisationGroupId == access.Id)
            .ToListAsync(cancellationToken);
        var existingMappings = await db.GroupLedgerAccountMappings
            .Where(x => x.OrganisationGroupId == access.Id &&
                        x.OrganisationId == sourceOrganisationId)
            .ToListAsync(cancellationToken);
        var createdAccounts = 0;
        var createdMappings = 0;
        foreach (var companyAccount in companyAccounts)
        {
            var code = NormaliseCode(companyAccount.Code);
            var groupAccount = groupAccounts.SingleOrDefault(x => x.Code == code);
            if (groupAccount is null)
            {
                groupAccount = new GroupLedgerAccount
                {
                    OrganisationGroupId = access.Id,
                    Code = code,
                    Name = companyAccount.Name.Trim(),
                    Type = companyAccount.Type,
                    Purpose = GroupAccountPurpose.Standard,
                    CreatedByUserId = userId
                };
                db.GroupLedgerAccounts.Add(groupAccount);
                groupAccounts.Add(groupAccount);
                createdAccounts++;
            }
            else if (groupAccount.Type != companyAccount.Type)
            {
                throw new InvalidOperationException(
                    $"Company account {companyAccount.Code} has type {companyAccount.Type}, " +
                    $"but group account {groupAccount.Code} has type {groupAccount.Type}.");
            }

            if (existingMappings.All(x => x.LedgerAccountId != companyAccount.Id))
            {
                var mapping = new GroupLedgerAccountMapping
                {
                    OrganisationGroupId = access.Id,
                    OrganisationId = sourceOrganisationId,
                    LedgerAccountId = companyAccount.Id,
                    GroupLedgerAccount = groupAccount,
                    CreatedByUserId = userId
                };
                db.GroupLedgerAccountMappings.Add(mapping);
                existingMappings.Add(mapping);
                createdMappings++;
            }
        }

        if (createdAccounts == 0 && createdMappings == 0)
        {
            return 0;
        }

        db.AuditEvents.Add(Audit(
            currentOrganisationId,
            userId,
            "GroupChartInitialisedFromCompany",
            nameof(OrganisationGroup),
            access.Id,
            new
            {
                SourceOrganisationId = sourceOrganisationId,
                GroupAccountsCreated = createdAccounts,
                CompanyAccountsMapped = createdMappings
            }));
        await db.SaveChangesAsync(cancellationToken);
        return createdMappings;
    }

    public async Task SetMappingAsync(
        string userId,
        SetGroupAccountMappingRequest request,
        CancellationToken cancellationToken = default)
    {
        var access = await RequireGroupAsync(
            userId,
            request.CurrentOrganisationId,
            requireManager: true,
            cancellationToken);
        var companyAccount = await db.LedgerAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == request.LedgerAccountId &&
                     access.CompanyIds.Contains(x.OrganisationId) &&
                     x.IsActive,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "The company account is not active in this organisation group.");
        var existing = await db.GroupLedgerAccountMappings.SingleOrDefaultAsync(
            x => x.LedgerAccountId == companyAccount.Id,
            cancellationToken);
        if (request.GroupLedgerAccountId is null)
        {
            if (existing is null)
            {
                return;
            }

            db.GroupLedgerAccountMappings.Remove(existing);
            db.AuditEvents.Add(Audit(
                request.CurrentOrganisationId,
                userId,
                "GroupLedgerAccountMappingRemoved",
                nameof(GroupLedgerAccountMapping),
                existing.Id,
                new { companyAccount.OrganisationId, companyAccount.Id, companyAccount.Code }));
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var groupAccount = await db.GroupLedgerAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == request.GroupLedgerAccountId &&
                     x.OrganisationGroupId == access.Id &&
                     x.IsActive,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "The group account is not active in this organisation group.");
        if (groupAccount.Type != companyAccount.Type)
        {
            throw new InvalidOperationException(
                "Company accounts can only map to a group account of the same type.");
        }

        if (existing is not null &&
            existing.GroupLedgerAccountId == groupAccount.Id &&
            existing.OrganisationGroupId == access.Id)
        {
            return;
        }

        if (existing is null)
        {
            existing = new GroupLedgerAccountMapping
            {
                OrganisationGroupId = access.Id,
                OrganisationId = companyAccount.OrganisationId,
                LedgerAccountId = companyAccount.Id,
                GroupLedgerAccountId = groupAccount.Id,
                CreatedByUserId = userId
            };
            db.GroupLedgerAccountMappings.Add(existing);
        }
        else
        {
            if (existing.OrganisationGroupId != access.Id)
            {
                throw new InvalidOperationException(
                    "The company account is already mapped in another organisation group.");
            }

            existing.GroupLedgerAccountId = groupAccount.Id;
            existing.CreatedAt = DateTimeOffset.UtcNow;
            existing.CreatedByUserId = userId;
        }

        db.AuditEvents.Add(Audit(
            request.CurrentOrganisationId,
            userId,
            "GroupLedgerAccountMapped",
            nameof(GroupLedgerAccountMapping),
            existing.Id,
            new
            {
                companyAccount.OrganisationId,
                CompanyAccountId = companyAccount.Id,
                CompanyAccountCode = companyAccount.Code,
                GroupAccountId = groupAccount.Id,
                GroupAccountCode = groupAccount.Code
            }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveIntercompanyConfigurationAsync(
        string userId,
        SaveIntercompanyAccountConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        var access = await RequireGroupAsync(
            userId,
            request.CurrentOrganisationId,
            requireManager: true,
            cancellationToken);
        if (request.OrganisationId == request.CounterpartyOrganisationId ||
            !access.CompanyIds.Contains(request.OrganisationId) ||
            !access.CompanyIds.Contains(request.CounterpartyOrganisationId))
        {
            throw new InvalidOperationException(
                "Select two different companies in this organisation group.");
        }

        var selectedAccountIds = new[]
        {
            request.ReceivableAccountId,
            request.PayableAccountId,
            request.RevenueAccountId,
            request.ExpenseAccountId
        };
        if (selectedAccountIds.Distinct().Count() != selectedAccountIds.Length)
        {
            throw new InvalidOperationException(
                "Select a different ledger account for each intercompany purpose.");
        }

        var accounts = await db.LedgerAccounts
            .AsNoTracking()
            .Where(x => selectedAccountIds.Contains(x.Id) &&
                        x.OrganisationId == request.OrganisationId &&
                        x.IsActive)
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        ValidateSelectedAccount(accounts, request.ReceivableAccountId, AccountType.Asset, "receivable");
        ValidateSelectedAccount(accounts, request.PayableAccountId, AccountType.Liability, "payable");
        ValidateSelectedAccount(accounts, request.RevenueAccountId, AccountType.Revenue, "revenue");
        ValidateSelectedAccount(accounts, request.ExpenseAccountId, AccountType.Expense, "expense");

        var configuration = await db.IntercompanyAccountConfigurations
            .SingleOrDefaultAsync(
                x => x.OrganisationGroupId == access.Id &&
                     x.OrganisationId == request.OrganisationId &&
                     x.CounterpartyOrganisationId == request.CounterpartyOrganisationId,
                cancellationToken);
        var created = configuration is null;
        if (configuration is null)
        {
            configuration = new IntercompanyAccountConfiguration
            {
                OrganisationGroupId = access.Id,
                OrganisationId = request.OrganisationId,
                CounterpartyOrganisationId = request.CounterpartyOrganisationId,
                ReceivableAccountId = request.ReceivableAccountId,
                PayableAccountId = request.PayableAccountId,
                RevenueAccountId = request.RevenueAccountId,
                ExpenseAccountId = request.ExpenseAccountId,
                UpdatedByUserId = userId
            };
            db.IntercompanyAccountConfigurations.Add(configuration);
        }
        else
        {
            configuration.ReceivableAccountId = request.ReceivableAccountId;
            configuration.PayableAccountId = request.PayableAccountId;
            configuration.RevenueAccountId = request.RevenueAccountId;
            configuration.ExpenseAccountId = request.ExpenseAccountId;
            configuration.UpdatedAt = DateTimeOffset.UtcNow;
            configuration.UpdatedByUserId = userId;
        }

        db.AuditEvents.Add(Audit(
            request.CurrentOrganisationId,
            userId,
            created
                ? "IntercompanyAccountConfigurationCreated"
                : "IntercompanyAccountConfigurationUpdated",
            nameof(IntercompanyAccountConfiguration),
            configuration.Id,
            new
            {
                request.OrganisationId,
                request.CounterpartyOrganisationId,
                request.ReceivableAccountId,
                request.PayableAccountId,
                request.RevenueAccountId,
                request.ExpenseAccountId
            }));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateSelectedAccount(
        IReadOnlyDictionary<Guid, LedgerAccount> accounts,
        Guid accountId,
        AccountType requiredType,
        string purpose)
    {
        if (!accounts.TryGetValue(accountId, out var account) || account.Type != requiredType)
        {
            throw new InvalidOperationException(
                $"Select an active {requiredType.ToString().ToLowerInvariant()} account for intercompany {purpose}.");
        }
    }

    private static void ValidatePurpose(AccountType type, GroupAccountPurpose purpose)
    {
        if (!Enum.IsDefined(type) || !Enum.IsDefined(purpose))
        {
            throw new InvalidOperationException("Select a valid group account type and purpose.");
        }

        var requiredType = purpose switch
        {
            GroupAccountPurpose.IntercompanyReceivable => AccountType.Asset,
            GroupAccountPurpose.IntercompanyPayable => AccountType.Liability,
            GroupAccountPurpose.IntercompanyRevenue => AccountType.Revenue,
            GroupAccountPurpose.IntercompanyExpense => AccountType.Expense,
            _ => type
        };
        if (type != requiredType)
        {
            throw new InvalidOperationException(
                $"The {purpose} purpose requires a {requiredType} group account.");
        }
    }

    private static string NormaliseCode(string value)
    {
        var code = value.Trim().ToUpperInvariant();
        if (code.Length is < 1 or > 32)
        {
            throw new InvalidOperationException(
                "Enter a group account code of 32 characters or fewer.");
        }

        return code;
    }

    private static string NormaliseName(string value)
    {
        var name = value.Trim();
        if (name.Length is < 1 or > 160)
        {
            throw new InvalidOperationException(
                "Enter a group account name of 160 characters or fewer.");
        }

        return name;
    }

    private static AuditEvent Audit(
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

    private async Task<GroupAccess> RequireGroupAsync(
        string userId,
        Guid currentOrganisationId,
        bool requireManager,
        CancellationToken cancellationToken)
    {
        var group = await db.OrganisationGroups
            .AsNoTracking()
            .Where(x => x.Companies.Any(company => company.Id == currentOrganisationId))
            .Select(x => new
            {
                x.Id,
                x.Name,
                Companies = x.Companies
                    .OrderBy(company => company.LegalName)
                    .Select(company => new GroupSetupCompany(
                        company.Id,
                        company.LegalName,
                        company.BaseCurrency))
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "This organisation does not belong to an organisation group.");
        var role = await db.OrganisationGroupMemberships
            .AsNoTracking()
            .Where(x => x.OrganisationGroupId == group.Id && x.UserId == userId)
            .Select(x => (OrganisationGroupRole?)x.Role)
            .SingleOrDefaultAsync(cancellationToken);
        if (role is not null)
        {
            if (requireManager && role == OrganisationGroupRole.Viewer)
            {
                throw new UnauthorizedAccessException(
                    "You do not have permission to manage the group account setup.");
            }

            return new(
                group.Id,
                group.Name,
                group.Companies,
                role != OrganisationGroupRole.Viewer);
        }

        var managedCompanyIds = await db.OrganisationMemberships
            .AsNoTracking()
            .Where(x =>
                x.UserId == userId &&
                x.Organisation.OrganisationGroupId == group.Id &&
                (x.Role == OrganisationRole.Owner ||
                 x.Role == OrganisationRole.Administrator))
            .Select(x => x.OrganisationId)
            .ToListAsync(cancellationToken);
        if (group.Companies.Any(x => !managedCompanyIds.Contains(x.Id)))
        {
            throw new UnauthorizedAccessException(
                requireManager
                    ? "You do not have permission to manage the group account setup."
                    : "You do not have access to this organisation group.");
        }

        return new(group.Id, group.Name, group.Companies, true);
    }

    private sealed record GroupAccess(
        Guid Id,
        string Name,
        IReadOnlyList<GroupSetupCompany> Companies,
        bool CanManage)
    {
        public IReadOnlyList<Guid> CompanyIds => Companies.Select(x => x.Id).ToList();
    }
}
