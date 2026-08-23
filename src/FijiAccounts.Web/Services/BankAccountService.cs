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
    DateOnly OpeningBalanceDate);

public sealed class BankAccountService(
    ApplicationDbContext db,
    TenantAccessService access,
    JournalPostingService posting)
{
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
                Type = AccountType.Asset,
                IsBankAccount = true
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

            IReadOnlyList<JournalLineInput> lines =
                request.OpeningBalance > 0m
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
