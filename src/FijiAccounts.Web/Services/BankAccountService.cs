using System.Text.Json;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record CreateBankAccountRequest(
    Guid OrganisationId,
    string Code,
    string Name,
    string? AccountNumber,
    decimal OpeningBalance,
    DateOnly OpeningBalanceDate,
    BankAccountKind AccountKind = BankAccountKind.Bank);

public sealed class BankAccountService(
    ApplicationDbContext db,
    TenantAccessService access,
    JournalPostingService posting)
{
    public async Task<IReadOnlyDictionary<Guid, decimal>> GetBalancesAsync(
        string userId,
        Guid organisationId,
        CancellationToken ct = default)
    {
        if (await access.FindAsync(userId, organisationId) is null)
        {
            throw new UnauthorizedAccessException(
                "You do not have access to these bank balances.");
        }

        var lines = await db.PostedJournalLines
            .AsNoTracking()
            .Where(x =>
                x.LedgerAccount.OrganisationId == organisationId &&
                x.LedgerAccount.IsActive &&
                x.LedgerAccount.IsBankAccount)
            .Select(x => new
            {
                x.LedgerAccountId,
                x.Debit,
                x.Credit
            })
            .ToListAsync(ct);

        return lines
            .GroupBy(x => x.LedgerAccountId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(x => x.Debit - x.Credit));
    }

    public async Task<LedgerAccount> CreateAsync(
        string userId,
        CreateBankAccountRequest request,
        CancellationToken ct = default)
    {
        if (!await access.CanManageTeamAsync(
                userId,
                request.OrganisationId))
        {
            throw new UnauthorizedAccessException(
                "You do not have permission to add bank accounts.");
        }

        var code = request.Code.Trim();
        var name = request.Name.Trim();
        var accountNumber = string.IsNullOrWhiteSpace(request.AccountNumber)
            ? null
            : request.AccountNumber.Trim();

        if (string.IsNullOrWhiteSpace(code) ||
            code.Length > 20 ||
            string.IsNullOrWhiteSpace(name) ||
            name.Length > 160 ||
            accountNumber?.Length > 80)
        {
            throw new InvalidOperationException(
                "Enter a valid account code, name and account number.");
        }

        if (!Enum.IsDefined(request.AccountKind))
        {
            throw new InvalidOperationException(
                "Select a valid bank account type.");
        }

        if (await db.LedgerAccounts.AnyAsync(
                x =>
                    x.OrganisationId == request.OrganisationId &&
                    x.Code == code,
                ct))
        {
            throw new InvalidOperationException(
                $"The account code '{code}' is already in use.");
        }

        await using var transaction =
            await db.Database.BeginTransactionAsync(ct);

        var bank =
            new LedgerAccount
            {
                OrganisationId = request.OrganisationId,
                Code = code,
                Name = name,
                BankAccountNumber = accountNumber,
                Type = request.AccountKind is
                    BankAccountKind.CreditCard or BankAccountKind.Loan
                        ? AccountType.Liability
                        : AccountType.Asset,
                IsBankAccount = true,
                BankAccountKind = request.AccountKind
            };

        db.LedgerAccounts.Add(bank);

        await db.SaveChangesAsync(ct);

        PostedJournal? openingJournal = null;
        if (request.OpeningBalance != 0m)
        {
            var openingEquity =
                await db.LedgerAccounts
                    .SingleOrDefaultAsync(
                        x =>
                            x.OrganisationId ==
                                request.OrganisationId &&
                            x.Code == "3200" &&
                            x.IsActive,
                        ct);

            if (openingEquity is null ||
                openingEquity.Type != AccountType.Equity)
            {
                throw new InvalidOperationException(
                    "Opening Balance Equity (3200) must be an active Equity account.");
            }

            var amount =
                Math.Abs(request.OpeningBalance);

            var accountHasDebitBalance =
                request.AccountKind is
                    BankAccountKind.CreditCard or BankAccountKind.Loan
                    ? request.OpeningBalance < 0m
                    : request.OpeningBalance > 0m;

            IReadOnlyList<JournalLineInput> lines =
                accountHasDebitBalance
                    ?
                    [
                        new(
                            bank.Id,
                            $"Opening balance · {name}",
                            amount,
                            0m),

                        new(
                            openingEquity.Id,
                            $"Opening balance · {name}",
                            0m,
                            amount)
                    ]
                    :
                    [
                        new(
                            openingEquity.Id,
                            $"Opening balance · {name}",
                            amount,
                            0m),

                        new(
                            bank.Id,
                            $"Opening balance · {name}",
                            0m,
                            amount)
                    ];

            openingJournal = await posting.PostAsync(
                userId,
                new JournalPostRequest(
                    request.OrganisationId,
                    request.OpeningBalanceDate,
                    $"OPEN-{code}",
                    $"Opening balance for {name}",
                    lines),
                ct);
        }

        db.AuditEvents.Add(
            new AuditEvent
            {
                OrganisationId = request.OrganisationId,
                UserId = userId,
                EventType = "BankAccountCreated",
                EntityType = nameof(LedgerAccount),
                EntityId = bank.Id.ToString(),
                JsonData = JsonSerializer.Serialize(
                    new
                    {
                        bank.Code,
                        bank.Name,
                        HasAccountNumber = bank.BankAccountNumber is not null,
                        AccountNumberLast4 = bank.BankAccountNumber is { Length: > 4 }
                            ? bank.BankAccountNumber[^4..]
                            : bank.BankAccountNumber,
                        Type = bank.Type.ToString(),
                        bank.IsBankAccount,
                        bank.BankAccountKind,
                        request.OpeningBalance,
                        request.OpeningBalanceDate,
                        OpeningJournalId = openingJournal?.Id
                    })
            });

        await db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);

        return bank;
    }
}
