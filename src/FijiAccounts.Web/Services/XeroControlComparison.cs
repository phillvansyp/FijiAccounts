using System.Globalization;
using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

internal sealed record XeroControlRow(string Label, string? Code, bool Bank, bool Payable,
    decimal? Ledger, decimal? Xero, decimal? Statement, int? Unmatched, DateOnly? LastReconciliation);

internal static class XeroControlComparison
{
    // The trial balance includes month movement and closing (YTD) columns. Only the
    // latter can be compared to the ledger balance as of the requested date.
    internal static Dictionary<Guid, decimal> ClosingAmounts(byte[]? payload)
    {
        var balances = new Dictionary<Guid, decimal>();
        if (payload is null) return balances;
        using var document = JsonDocument.Parse(payload);
        var rows = document.RootElement.GetProperty("Reports")[0].GetProperty("Rows");
        var header = rows.EnumerateArray().FirstOrDefault(r => Field(r, "RowType") == "Header");
        if (header.ValueKind == JsonValueKind.Undefined) return balances;
        var columns = header.GetProperty("Cells").EnumerateArray().Select(c => Field(c, "Value")).ToArray();
        var debit = Array.FindIndex(columns, c => c.Equals("YTD Debit", StringComparison.OrdinalIgnoreCase));
        var credit = Array.FindIndex(columns, c => c.Equals("YTD Credit", StringComparison.OrdinalIgnoreCase));
        if (debit < 0 || credit < 0) return balances;
        var ambiguous = new HashSet<Guid>();
        void Read(JsonElement group)
        {
            foreach (var row in group.EnumerateArray())
            {
                if (row.TryGetProperty("Rows", out var inner)) Read(inner);
                if (Field(row, "RowType") != "Row" || !row.TryGetProperty("Cells", out var cells) ||
                    cells.GetArrayLength() <= Math.Max(debit, credit) ||
                    !cells[0].TryGetProperty("Attributes", out var attrs)) continue;
                foreach (var attribute in attrs.EnumerateArray())
                {
                    if (!Field(attribute, "Id").Equals("account", StringComparison.OrdinalIgnoreCase) ||
                        !Guid.TryParse(Field(attribute, "Value"), out var id)) continue;
                    var dr = Money(cells[debit]);
                    var cr = Money(cells[credit]);
                    if (!dr.HasValue || !cr.HasValue || !balances.TryAdd(id, dr.Value - cr.Value)) ambiguous.Add(id);
                }
            }
        }
        Read(rows);
        foreach (var id in ambiguous) balances.Remove(id);
        return balances;
    }

    private static decimal? Money(JsonElement cell)
    {
        if (!cell.TryGetProperty("Value", out var field)) return null;
        var value = field.ToString().Trim();
        if (value.Length == 0) return 0m;
        return decimal.TryParse(value, NumberStyles.Number | NumberStyles.AllowParentheses,
            CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    private static string Field(JsonElement item, string field) =>
        item.TryGetProperty(field, out var value) ? value.ToString() : "";

    internal static async Task<List<XeroControlRow>> ReadAsync(ApplicationDbContext db, Guid org, Guid connection,
        DateOnly date, byte[]? payload)
    {
        var xero = ClosingAmounts(payload);
        var sources = await db.XeroMirrorRecords.AsNoTracking().Where(r => r.OrganisationId == org &&
            r.XeroConnectionId == connection && r.DatasetKey == "accounts" && !r.IsArchived)
            .Select(r => new { r.ExternalId, r.SourceJson }).ToListAsync();
        var links = await db.XeroMirrorEntityLinks.AsNoTracking().Where(l => l.OrganisationId == org &&
            l.XeroConnectionId == connection && l.DatasetKey == "accounts" && l.TargetEntityType == nameof(LedgerAccount))
            .ToDictionaryAsync(l => l.ExternalId, l => l.TargetEntityId, StringComparer.OrdinalIgnoreCase);
        var local = await db.LedgerAccounts.AsNoTracking().Where(a => a.OrganisationId == org).ToDictionaryAsync(a => a.Id);
        var amounts = await db.PostedJournalLines.AsNoTracking().Where(l => l.PostedJournal.OrganisationId == org &&
            l.PostedJournal.EntryDate <= date).GroupBy(l => l.LedgerAccountId)
            .Select(g => new { Id = g.Key, Amount = g.Sum(l => l.Debit) - g.Sum(l => l.Credit) })
            .ToDictionaryAsync(a => a.Id, a => a.Amount);
        var statements = await db.BankReconciliationSessions.AsNoTracking().Where(s => s.OrganisationId == org &&
            s.StatementEndDate <= date).Select(s => new { s.BankAccountId,s.StatementEndDate,s.ClosingStatementBalance,s.IsCompleted,s.CreatedAt })
            .ToListAsync();
        var unmatched = await db.BankStatementLines.AsNoTracking().Where(l => l.OrganisationId == org &&
            l.TransactionDate <= date && l.ReconciledAt == null).GroupBy(l => l.BankAccountId)
            .Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count);
        var rows = new List<XeroControlRow>();
        var mappedBanks = new HashSet<Guid>();
        void Add(string label, Guid? external, LedgerAccount? account, bool bank, bool payable)
        {
            var sessions = statements.Where(s => s.BankAccountId == account?.Id).ToList();
            var sameDate = sessions.Where(s => s.StatementEndDate == date).OrderByDescending(s => s.CreatedAt).FirstOrDefault();
            var last = sessions.Where(s => s.IsCompleted).Max(s => (DateOnly?)s.StatementEndDate);
            var sign = payable ? -1 : 1;
            rows.Add(new(label, account?.Code, bank, payable,
                account != null && amounts.TryGetValue(account.Id, out var balance) ? sign * balance : null,
                external.HasValue && xero.TryGetValue(external.Value, out var reported) ? sign * reported : null,
                sameDate?.ClosingStatementBalance, account != null && sessions.Count > 0 ? unmatched.GetValueOrDefault(account.Id) : null, last));
        }
        foreach (var source in sources)
        {
            using var doc = JsonDocument.Parse(source.SourceJson);
            var account = doc.RootElement;
            var system = Field(account, "SystemAccount");
            var bank = Field(account, "Type") == "BANK";
            if (!bank && system is not ("DEBTORS" or "CREDITORS")) continue;
            links.TryGetValue(source.ExternalId, out var target);
            var mapped = local.GetValueOrDefault(target);
            if (bank && mapped != null) mappedBanks.Add(target);
            Add(bank ? Field(account, "Name") : system == "DEBTORS" ? "Customers owing" : "Suppliers owing",
                Guid.TryParse(source.ExternalId, out var id) ? id : null, mapped, bank, system == "CREDITORS");
        }
        foreach (var bank in local.Values.Where(a => a.IsBankAccount && a.IsActive && !mappedBanks.Contains(a.Id)))
            Add(bank.Name, null, bank, true, false);
        if (!rows.Any(r => !r.Bank && !r.Payable)) Add("Customers owing", null, null, false, false);
        if (!rows.Any(r => !r.Bank && r.Payable)) Add("Suppliers owing", null, null, false, true);
        return rows.OrderBy(r => r.Bank).ThenBy(r => r.Label).ToList();
    }
}
