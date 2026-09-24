using System.Net.Http.Headers;
using System.Net.Http.Json;
using FijiAccounts.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

// A bank payment is reported only after reconciliation to the correct liability account.
public sealed class PayrollGovernmentPaymentSyncService(
    ApplicationDbContext db, IHttpClientFactory httpFactory, IDataProtectionProvider protection)
{
    private readonly IDataProtector tokenProtector =
        protection.CreateProtector("AccountIsland.PayrollIsland.AccessToken.v1");

    public async Task<int> SyncAsync(Guid? organisationId = null, CancellationToken ct = default)
    {
        var connections = await db.PayrollIslandConnections.AsNoTracking()
            .Where(x => x.IsActive && (organisationId == null || x.OrganisationId == organisationId))
            .ToArrayAsync(ct);
        var sent = 0;
        foreach (var connection in connections)
            sent += await SyncConnectionAsync(connection, ct);
        return sent;
    }

    private async Task<int> SyncConnectionAsync(PayrollIslandConnection connection, CancellationToken ct)
    {
        var imports = await db.PayrollIslandPayRunImports.AsNoTracking().Include(x => x.Payments)
            .Where(x => x.ConnectionId == connection.Id).ToArrayAsync(ct);
        var latest = imports.GroupBy(x => x.ExternalPayRunId)
            .Select(group => group.MaxBy(x => x.Revision)!).ToArray();
        var months = latest.GroupBy(x => new DateOnly(x.PaymentDate.Year, x.PaymentDate.Month, 1))
            .Where(group => group.Key >= DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-24))
            .ToArray();
        if (months.Length == 0) return 0;

        var statements = await db.BankStatementLines.AsNoTracking()
            .Include(x => x.MatchedPostedJournalLine).ThenInclude(x => x!.PostedJournal).ThenInclude(x => x.Lines)
            .Where(x => x.OrganisationId == connection.OrganisationId && x.Amount < 0 &&
                x.ReconciledAt != null && x.MatchedPostedJournalLineId != null)
            .ToArrayAsync(ct);
        var excluded = await BankCodingHistory.UnmatchableJournalIdsAsync(db, connection.OrganisationId, ct);
        var token = tokenProtector.Unprotect(connection.ProtectedAccessToken);
        using var http = httpFactory.CreateClient();
        var sent = 0;
        foreach (var month in months)
        {
            var posted = month.All(x => x.Status == PayrollIslandImportStatus.Posted && x.PostedJournalId != null);
            foreach (var (key, kind, accountId) in new[] {
                ("paye-monthly", PayrollPaymentKind.Paye, connection.PayePayableAccountId),
                ("fnpf-payment", PayrollPaymentKind.Fnpf, connection.FnpfPayableAccountId) })
            {
                var amount = decimal.Round(month.SelectMany(x => x.Payments).Where(x => x.Kind == kind)
                    .Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero);
                if (amount <= 0) continue;
                var candidates = posted ? statements.Where(x => IsVerified(x, amount, accountId, key, month.Key, excluded)).ToArray() : [];
                var match = candidates.Length == 1 ? candidates[0] : null;
                var update = new
                {
                    matched = match is not null, amount,
                    bankStatementLineId = match?.Id,
                    bankPaymentDate = match?.TransactionDate,
                    matchedAtUtc = match?.ReconciledAt
                };
                var root = new Uri(connection.BaseUrl.EndsWith('/') ? connection.BaseUrl : connection.BaseUrl + "/");
                var path = $"api/account-island/v1/organisations/{Uri.EscapeDataString(connection.PayrollOrganisationId)}/government-payments/{key}/{month.Key:yyyy-MM-dd}";
                using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(root, path))
                {
                    Content = JsonContent.Create(update)
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Add("X-Account-Island-Contract", "2026-09-01");
                using var response = await http.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();
                sent++;
            }
        }
        return sent;
    }

    public static bool IsVerified(BankStatementLine statement, decimal amount, Guid liabilityAccountId,
        string key, DateOnly periodStart,
        IReadOnlySet<Guid> excludedJournalIds)
    {
        var bankLine = statement.MatchedPostedJournalLine;
        var journal = bankLine?.PostedJournal;
        var evidence = $"{statement.Description} {statement.Reference} {journal?.Description} {journal?.Reference}".ToUpperInvariant();
        var agency = key == "paye-monthly" ? "PAYE" : "FNPF";
        var otherAgency = key == "paye-monthly" ? "FNPF" : "PAYE";
        return statement.ReconciledAt is not null && statement.Amount == -amount &&
            statement.TransactionDate >= periodStart && evidence.Contains(agency, StringComparison.Ordinal) &&
            !evidence.Contains(otherAgency, StringComparison.Ordinal) &&
            evidence.Contains(periodStart.ToString("yyyy-MM"), StringComparison.Ordinal) &&
            bankLine is not null && bankLine.Id == statement.MatchedPostedJournalLineId &&
            bankLine.LedgerAccountId == statement.BankAccountId && bankLine.Credit - bankLine.Debit == amount &&
            journal is not null && journal.OrganisationId == statement.OrganisationId &&
            journal.Currency == "FJD" && journal.Lines.Count == 2 &&
            !excludedJournalIds.Contains(journal.Id) &&
            !journal.Reference.StartsWith("REV-BANK-", StringComparison.Ordinal) &&
            journal.Lines.Any(x => x.LedgerAccountId == liabilityAccountId && x.Debit == amount && x.Credit == 0);
    }
}
