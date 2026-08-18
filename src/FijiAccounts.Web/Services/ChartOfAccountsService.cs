using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record LedgerAccountRequest(
    Guid OrganisationId,
    string Code,
    string Name,
    AccountType Type,
    bool IsBankAccount,
    BankAccountKind BankAccountKind,
    string? BankAccountNumber);

public sealed record UpdateLedgerAccountRequest(
    Guid OrganisationId,
    Guid AccountId,
    string Name,
    AccountType Type,
    bool IsBankAccount,
    BankAccountKind BankAccountKind,
    string? BankAccountNumber);

public sealed class ChartOfAccountsService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    public async Task<LedgerAccount> CreateAsync(
        string userId,
        LedgerAccountRequest request,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                request.OrganisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot maintain accounts for this organisation.");
        }

        var code = request.Code.Trim().ToUpperInvariant();
        var name = request.Name.Trim();

        if (code.Length is < 2 or > 20 ||
            name.Length is < 2 or > 160)
        {
            throw new InvalidOperationException(
                "Enter a valid account code and name.");
        }

        if (await db.LedgerAccounts.AnyAsync(
                x =>
                    x.OrganisationId == request.OrganisationId &&
                    x.Code == code,
                ct))
        {
            throw new InvalidOperationException(
                $"Account code {code} already exists.");
        }

        var accountKind = request.IsBankAccount
            ? request.BankAccountKind
            : BankAccountKind.Bank;

        ValidateClassification(
            request.Type,
            request.IsBankAccount,
            accountKind);

        var account = new LedgerAccount
        {
            OrganisationId = request.OrganisationId,
            Code = code,
            Name = name,
            Type = request.Type,
            IsBankAccount = request.IsBankAccount,
            BankAccountKind = accountKind,
            BankAccountNumber =
                string.IsNullOrWhiteSpace(request.BankAccountNumber)
                    ? null
                    : request.BankAccountNumber.Trim(),
            IsActive = true
        };

        db.LedgerAccounts.Add(account);

        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = request.OrganisationId,
            UserId = userId,
            EventType = "LedgerAccountCreated",
            EntityType = nameof(LedgerAccount),
            EntityId = account.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                account.Code,
                account.Name,
                account.Type,
                account.IsBankAccount,
                account.BankAccountKind,
                account.BankAccountNumber
            })
        });

        await db.SaveChangesAsync(ct);

        return account;
    }

    public async Task<LedgerAccount> UpdateAsync(
        string userId,
        UpdateLedgerAccountRequest request,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                request.OrganisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot maintain accounts for this organisation.");
        }

        var account = await db.LedgerAccounts
            .SingleOrDefaultAsync(
                x =>
                    x.Id == request.AccountId &&
                    x.OrganisationId == request.OrganisationId,
                ct)
            ?? throw new InvalidOperationException(
                "Account not found.");

        if (account.IsSystemAccount)
        {
            throw new InvalidOperationException(
                "Core system accounts cannot be edited here.");
        }

        var name = request.Name.Trim();

        if (name.Length is < 2 or > 160)
        {
            throw new InvalidOperationException(
                "Enter a valid account name.");
        }

        var accountKind = request.IsBankAccount
            ? request.BankAccountKind
            : BankAccountKind.Bank;

        ValidateClassification(
            request.Type,
            request.IsBankAccount,
            accountKind);

        var oldValues = new
        {
            account.Name,
            account.Type,
            account.IsBankAccount,
            account.BankAccountKind,
            account.BankAccountNumber
        };

        account.Name = name;
        account.Type = request.Type;
        account.IsBankAccount = request.IsBankAccount;
        account.BankAccountKind = accountKind;
        account.BankAccountNumber =
            request.IsBankAccount &&
            !string.IsNullOrWhiteSpace(request.BankAccountNumber)
                ? request.BankAccountNumber.Trim()
                : null;

        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = request.OrganisationId,
            UserId = userId,
            EventType = "LedgerAccountUpdated",
            EntityType = nameof(LedgerAccount),
            EntityId = account.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                account.Code,
                Old = oldValues,
                New = new
                {
                    account.Name,
                    account.Type,
                    account.IsBankAccount,
                    account.BankAccountKind,
                    account.BankAccountNumber
                }
            })
        });

        await db.SaveChangesAsync(ct);

        return account;
    }

    public async Task SetActiveAsync(
        string userId,
        Guid organisationId,
        Guid accountId,
        bool active,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot maintain accounts for this organisation.");
        }

        var account = await db.LedgerAccounts
            .SingleOrDefaultAsync(
                x =>
                    x.Id == accountId &&
                    x.OrganisationId == organisationId,
                ct)
            ?? throw new InvalidOperationException(
                "Account not found.");

        if (!active && account.IsSystemAccount)
        {
            throw new InvalidOperationException(
                "Core system accounts cannot be archived.");
        }

        account.IsActive = active;

        db.AuditEvents.Add(new AuditEvent
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = active
                ? "LedgerAccountReactivated"
                : "LedgerAccountArchived",
            EntityType = nameof(LedgerAccount),
            EntityId = account.Id.ToString(),
            JsonData = JsonSerializer.Serialize(new
            {
                account.Code
            })
        });

        await db.SaveChangesAsync(ct);
    }

    private static void ValidateClassification(
        AccountType type,
        bool isBankAccount,
        BankAccountKind accountKind)
    {
        if (!isBankAccount)
        {
            return;
        }

        if (accountKind is BankAccountKind.Bank or BankAccountKind.DebitCard)
        {
            if (type != AccountType.Asset)
            {
                throw new InvalidOperationException(
                    "Bank and debit/EFTPOS accounts must use the Asset account type.");
            }

            return;
        }

        if (accountKind == BankAccountKind.CreditCard &&
            type != AccountType.Liability)
        {
            throw new InvalidOperationException(
                "Credit card accounts must use the Liability account type.");
        }
    }
}