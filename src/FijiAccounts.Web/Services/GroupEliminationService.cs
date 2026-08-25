using System.Text.Json;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record GroupEliminationAccount(
    string Code,
    string Name,
    AccountType Type);

public sealed record GroupEliminationConfiguration(
    Guid GroupId,
    string GroupName,
    string Currency,
    bool CanManage,
    IReadOnlyList<GroupEliminationAccount> Accounts,
    IReadOnlyList<GroupEliminationJournal> Journals);

public sealed record GroupEliminationLineInput(
    string AccountCode,
    string AccountName,
    AccountType AccountType,
    string Description,
    decimal Debit,
    decimal Credit);

public sealed record PostGroupEliminationRequest(
    Guid CurrentOrganisationId,
    DateOnly EntryDate,
    string Reference,
    string? Description,
    IReadOnlyList<GroupEliminationLineInput> Lines);

public sealed class GroupEliminationService(ApplicationDbContext db)
{
    public async Task<GroupEliminationConfiguration> GetAsync(
        string userId,
        Guid currentOrganisationId,
        CancellationToken cancellationToken = default)
    {
        var access = await RequireGroupAsync(
            userId,
            currentOrganisationId,
            requireManager: false,
            cancellationToken);
        var storedAccounts = await db.LedgerAccounts
            .AsNoTracking()
            .Where(x =>
                access.CompanyIds.Contains(x.OrganisationId) &&
                x.IsActive)
            .Select(x => new { x.Code, x.Name, x.Type })
            .ToListAsync(cancellationToken);
        var accounts = storedAccounts
            .Select(x => new GroupEliminationAccount(x.Code, x.Name, x.Type))
            .Distinct()
            .OrderBy(x => x.Code)
            .ThenBy(x => x.Name)
            .ToList();
        var journals = await db.GroupEliminationJournals
            .AsNoTracking()
            .Include(x => x.Lines)
            .Where(x => x.OrganisationGroupId == access.Id)
            .OrderByDescending(x => x.EntryDate)
            .Take(100)
            .ToListAsync(cancellationToken);
        journals = journals
            .OrderByDescending(x => x.EntryDate)
            .ThenByDescending(x => x.PostedAt)
            .ToList();

        return new(
            access.Id,
            access.Name,
            access.PresentationCurrency,
            access.CanManage,
            accounts,
            journals);
    }

    public async Task<GroupEliminationJournal> PostAsync(
        string userId,
        PostGroupEliminationRequest request,
        CancellationToken cancellationToken = default)
    {
        var access = await RequireGroupAsync(
            userId,
            request.CurrentOrganisationId,
            requireManager: true,
            cancellationToken);
        var reference = request.Reference.Trim();
        if (reference.Length is < 1 or > 80)
        {
            throw new InvalidOperationException("Enter an elimination reference of 80 characters or fewer.");
        }

        if (request.Lines.Count < 2)
        {
            throw new InvalidOperationException("An elimination journal requires at least two lines.");
        }

        var normalisedLines = request.Lines.Select(NormaliseLine).ToList();
        var totalDebits = normalisedLines.Sum(x => x.Debit);
        var totalCredits = normalisedLines.Sum(x => x.Credit);
        if (totalDebits <= 0m || totalDebits != totalCredits)
        {
            throw new InvalidOperationException("Elimination journal debits and credits must balance and be greater than zero.");
        }

        var validAccounts = await db.LedgerAccounts
            .AsNoTracking()
            .Where(x => access.CompanyIds.Contains(x.OrganisationId) && x.IsActive)
            .Select(x => new { x.Code, x.Name, x.Type })
            .Distinct()
            .ToListAsync(cancellationToken);
        foreach (var line in normalisedLines)
        {
            if (!validAccounts.Any(x =>
                    x.Code == line.AccountCode &&
                    x.Name == line.AccountName &&
                    x.Type == line.AccountType))
            {
                throw new InvalidOperationException(
                    $"Account {line.AccountCode} - {line.AccountName} is not active in this organisation group.");
            }
        }

        if (await db.GroupEliminationJournals.AnyAsync(
                x => x.OrganisationGroupId == access.Id && x.Reference == reference,
                cancellationToken))
        {
            throw new InvalidOperationException("That elimination reference is already in use for this group.");
        }

        var journal = new GroupEliminationJournal
        {
            OrganisationGroupId = access.Id,
            EntryDate = request.EntryDate,
            Reference = reference,
            Description = CleanOptional(request.Description, 500),
            Currency = access.PresentationCurrency,
            PostedByUserId = userId,
            Lines = normalisedLines.Select(x => new GroupEliminationJournalLine
            {
                AccountCode = x.AccountCode,
                AccountName = x.AccountName,
                AccountType = x.AccountType,
                Description = x.Description,
                Debit = x.Debit,
                Credit = x.Credit
            }).ToList()
        };
        db.GroupEliminationJournals.Add(journal);
        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = request.CurrentOrganisationId,
            UserId = userId,
            EventType = "GroupEliminationJournalPosted",
            EntityType = nameof(GroupEliminationJournal),
            EntityId = journal.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                OrganisationGroupId = access.Id,
                GroupName = access.Name,
                journal.Reference,
                journal.EntryDate,
                journal.Currency,
                Total = totalDebits,
                LineCount = journal.Lines.Count
            })
        });
        await db.SaveChangesAsync(cancellationToken);
        return journal;
    }

    private static GroupEliminationLineInput NormaliseLine(GroupEliminationLineInput line)
    {
        var code = line.AccountCode.Trim();
        var name = line.AccountName.Trim();
        var description = line.Description.Trim();
        if (!Enum.IsDefined(line.AccountType) || code.Length is < 1 or > 32 ||
            name.Length is < 1 or > 160 || description.Length is < 1 or > 300)
        {
            throw new InvalidOperationException("Each elimination line requires a valid account and description.");
        }

        if (line.Debit < 0m || line.Credit < 0m ||
            (line.Debit == 0m && line.Credit == 0m) ||
            (line.Debit > 0m && line.Credit > 0m))
        {
            throw new InvalidOperationException("Each elimination line must contain one positive debit or credit amount.");
        }

        return line with
        {
            AccountCode = code,
            AccountName = name,
            Description = description,
            Debit = decimal.Round(line.Debit, 2, MidpointRounding.AwayFromZero),
            Credit = decimal.Round(line.Credit, 2, MidpointRounding.AwayFromZero)
        };
    }

    private static string? CleanOptional(string? value, int maxLength)
    {
        var clean = value?.Trim();
        if (string.IsNullOrEmpty(clean)) return null;
        if (clean.Length > maxLength)
        {
            throw new InvalidOperationException($"The description must be {maxLength} characters or fewer.");
        }

        return clean;
    }

    private async Task<GroupAccess> RequireGroupAsync(
        string userId,
        Guid currentOrganisationId,
        bool requireManager,
        CancellationToken cancellationToken)
    {
        var group = await db.OrganisationGroups
            .AsNoTracking()
            .Include(x => x.Companies)
            .SingleOrDefaultAsync(
                x => x.Companies.Any(company => company.Id == currentOrganisationId),
                cancellationToken)
            ?? throw new InvalidOperationException("This organisation does not belong to an organisation group.");
        var role = await db.OrganisationGroupMemberships
            .AsNoTracking()
            .Where(x => x.OrganisationGroupId == group.Id && x.UserId == userId)
            .Select(x => (OrganisationGroupRole?)x.Role)
            .SingleOrDefaultAsync(cancellationToken);
        if (role is not null)
        {
            if (requireManager && role == OrganisationGroupRole.Viewer)
            {
                throw new UnauthorizedAccessException("You do not have permission to post group eliminations.");
            }

            return new(
                group.Id,
                group.Name,
                group.PresentationCurrency,
                group.Companies.Select(x => x.Id).ToList(),
                role != OrganisationGroupRole.Viewer);
        }

        var managedIds = await db.OrganisationMemberships
            .AsNoTracking()
            .Where(x =>
                x.UserId == userId &&
                x.Organisation.OrganisationGroupId == group.Id &&
                (x.Role == OrganisationRole.Owner || x.Role == OrganisationRole.Administrator))
            .Select(x => x.OrganisationId)
            .ToListAsync(cancellationToken);
        var canManage = group.Companies.All(x => managedIds.Contains(x.Id));
        if (!canManage)
        {
            throw new UnauthorizedAccessException(
                requireManager
                    ? "You do not have permission to post group eliminations."
                    : "You do not have access to this organisation group.");
        }

        return new(
            group.Id,
            group.Name,
            group.PresentationCurrency,
            group.Companies.Select(x => x.Id).ToList(),
            true);
    }

    private sealed record GroupAccess(
        Guid Id,
        string Name,
        string PresentationCurrency,
        IReadOnlyList<Guid> CompanyIds,
        bool CanManage);
}
