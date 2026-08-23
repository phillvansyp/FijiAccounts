using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record BankReconciliationSessionRequest(
    Guid OrganisationId,
    Guid BankAccountId,
    DateOnly StatementStartDate,
    DateOnly StatementEndDate,
    decimal OpeningStatementBalance,
    decimal ClosingStatementBalance);

public sealed class BankReconciliationSessionService(
    ApplicationDbContext db,
    TenantAccessService access)
{
    private const decimal AmountTolerance = 0.01m;

    public async Task<BankReconciliationSession> CreateAsync(
        string userId,
        BankReconciliationSessionRequest request,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                request.OrganisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot reconcile bank accounts for this organisation.");
        }

        if (request.StatementStartDate >
            request.StatementEndDate)
        {
            throw new InvalidOperationException(
                "Statement start date cannot be after the end date.");
        }

        var bank =
            await db.LedgerAccounts
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == request.BankAccountId &&
                        x.OrganisationId ==
                            request.OrganisationId &&
                        x.IsActive &&
                        x.IsBankAccount,
                    ct)
            ?? throw new InvalidOperationException(
                "Select an active bank account.");

        var overlaps =
            await db.BankReconciliationSessions
                .AnyAsync(
                    x =>
                        x.OrganisationId ==
                            request.OrganisationId &&
                        x.BankAccountId ==
                            request.BankAccountId &&
                        x.StatementStartDate <=
                            request.StatementEndDate &&
                        x.StatementEndDate >=
                            request.StatementStartDate,
                    ct);

        if (overlaps)
        {
            throw new InvalidOperationException(
                "A reconciliation already exists for part of this statement period.");
        }

        var ledgerBalance =
            await LedgerBalanceAsync(
                request.OrganisationId,
                request.BankAccountId,
                request.StatementEndDate,
                ct);

        var difference =
            Math.Round(
                request.ClosingStatementBalance -
                ledgerBalance,
                2,
                MidpointRounding.AwayFromZero);

        var session =
            new BankReconciliationSession
            {
                OrganisationId =
                    request.OrganisationId,
                BankAccountId =
                    bank.Id,
                StatementStartDate =
                    request.StatementStartDate,
                StatementEndDate =
                    request.StatementEndDate,
                OpeningStatementBalance =
                    request.OpeningStatementBalance,
                ClosingStatementBalance =
                    request.ClosingStatementBalance,
                LedgerBalance =
                    ledgerBalance,
                Difference =
                    difference,
                IsCompleted =
                    false,
                CreatedByUserId =
                    userId
            };

        db.BankReconciliationSessions.Add(session);
        db.AuditEvents.Add(Audit(
            request.OrganisationId,
            userId,
            "BankReconciliationSessionCreated",
            session,
            new
            {
                BankAccountCode = bank.Code,
                New = Evidence(session)
            }));

        await db.SaveChangesAsync(ct);

        return session;
    }

    public async Task<BankReconciliationSession> RefreshAsync(
        string userId,
        Guid organisationId,
        Guid sessionId,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot reconcile bank accounts for this organisation.");
        }

        var session =
            await db.BankReconciliationSessions
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == sessionId &&
                        x.OrganisationId ==
                            organisationId,
                    ct)
            ?? throw new InvalidOperationException(
                "Reconciliation session not found.");

        if (session.IsCompleted)
        {
            throw new InvalidOperationException(
                "A completed reconciliation cannot be changed.");
        }

        var ledgerBalance =
            await LedgerBalanceAsync(
                organisationId,
                session.BankAccountId,
                session.StatementEndDate,
                ct);

        var difference =
            Math.Round(
                session.ClosingStatementBalance -
                ledgerBalance,
                2,
                MidpointRounding.AwayFromZero);

        if (session.LedgerBalance == ledgerBalance &&
            session.Difference == difference)
        {
            return session;
        }

        var previous = Evidence(session);
        session.LedgerBalance = ledgerBalance;
        session.Difference = difference;
        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "BankReconciliationSessionRefreshed",
            session,
            new { Old = previous, New = Evidence(session) }));

        await db.SaveChangesAsync(ct);

        return session;
    }

    public async Task<BankReconciliationSession> CompleteAsync(
        string userId,
        Guid organisationId,
        Guid sessionId,
        CancellationToken ct = default)
    {
        if (!await access.CanPostJournalsAsync(
                userId,
                organisationId))
        {
            throw new UnauthorizedAccessException(
                "You cannot reconcile bank accounts for this organisation.");
        }

        var session =
            await db.BankReconciliationSessions
                .SingleOrDefaultAsync(
                    x =>
                        x.Id == sessionId &&
                        x.OrganisationId ==
                            organisationId,
                    ct)
            ?? throw new InvalidOperationException(
                "Reconciliation session not found.");

        if (session.IsCompleted)
        {
            throw new InvalidOperationException(
                "This reconciliation is already completed.");
        }

        var ledgerBalance =
            await LedgerBalanceAsync(
                organisationId,
                session.BankAccountId,
                session.StatementEndDate,
                ct);

        var difference =
            Math.Round(
                session.ClosingStatementBalance -
                ledgerBalance,
                2,
                MidpointRounding.AwayFromZero);

        if (Math.Abs(difference) >
            AmountTolerance)
        {
            throw new InvalidOperationException(
                $"The reconciliation difference must be zero before completion. Current difference: {difference:N2}.");
        }

        var unreconciledStatementLines =
            await db.BankStatementLines
                .AnyAsync(
                    x =>
                        x.OrganisationId ==
                            organisationId &&
                        x.BankAccountId ==
                            session.BankAccountId &&
                        x.TransactionDate >=
                            session.StatementStartDate &&
                        x.TransactionDate <=
                            session.StatementEndDate &&
                        x.ReconciledAt == null,
                    ct);

        if (unreconciledStatementLines)
        {
            throw new InvalidOperationException(
                "Reconcile all statement lines in this period before completing the reconciliation.");
        }

        var previous = Evidence(session);
        session.LedgerBalance = ledgerBalance;
        session.Difference = difference;
        session.IsCompleted = true;
        session.CompletedAt =
            DateTimeOffset.UtcNow;
        session.CompletedByUserId =
            userId;

        db.AuditEvents.Add(Audit(
            organisationId,
            userId,
            "BankReconciliationSessionCompleted",
            session,
            new { Old = previous, New = Evidence(session) }));

        await db.SaveChangesAsync(ct);

        return session;
    }

    private async Task<decimal> LedgerBalanceAsync(
        Guid organisationId,
        Guid bankAccountId,
        DateOnly asAt,
        CancellationToken ct)
    {
        return await db.PostedJournalLines
            .AsNoTracking()
            .Where(x =>
                x.PostedJournal.OrganisationId ==
                    organisationId &&
                x.LedgerAccountId ==
                    bankAccountId &&
                x.PostedJournal.EntryDate <=
                    asAt)
            .SumAsync(
                x => x.Debit - x.Credit,
                ct);
    }

    private static object Evidence(BankReconciliationSession session) =>
        new
        {
            session.BankAccountId,
            session.StatementStartDate,
            session.StatementEndDate,
            session.OpeningStatementBalance,
            session.ClosingStatementBalance,
            session.LedgerBalance,
            session.Difference,
            session.IsCompleted,
            session.CompletedAt,
            session.CompletedByUserId
        };

    private static AuditEvent Audit(
        Guid organisationId,
        string userId,
        string eventType,
        BankReconciliationSession session,
        object evidence) =>
        new()
        {
            OrganisationId = organisationId,
            UserId = userId,
            EventType = eventType,
            EntityType = nameof(BankReconciliationSession),
            EntityId = session.Id.ToString(),
            JsonData = JsonSerializer.Serialize(evidence)
        };
}
