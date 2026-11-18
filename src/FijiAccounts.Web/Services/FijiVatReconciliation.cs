using System.Text.Json;
using FijiAccounts.Domain.Accounting;
using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record VatSource(Guid? JournalId, Guid SourceId, DateOnly Date, string Reference, bool IsInput, decimal Tax)
{
    public string? Party { get; init; }
    public string? Tin { get; init; }
}
public sealed record BankVatEntry(VatSource Source, decimal Net, VatTreatment Treatment);
public sealed record VatDifference(VatSource Source, decimal Workpaper, decimal Ledger)
{
    public decimal Difference => Ledger - Workpaper;
}

public sealed record FijiVatReconciliation(decimal LedgerInput, decimal LedgerOutput,
    IReadOnlyList<BankVatEntry> BankEntries, IReadOnlyList<VatDifference> Differences, bool HasXeroEvidence)
{
    internal static async Task<FijiVatReconciliation> ReadAsync(ApplicationDbContext db, Guid organisationId,
        DateOnly from, DateOnly to, IEnumerable<VatSource> documents, CancellationToken ct)
    {
        var ledger = await db.PostedJournalLines.AsNoTracking()
            .Where(x => x.PostedJournal.OrganisationId == organisationId &&
                x.PostedJournal.EntryDate >= from && x.PostedJournal.EntryDate <= to &&
                (x.LedgerAccount.Code == "1150" || x.LedgerAccount.Code == "2100"))
            .Select(x => new VatSource(x.PostedJournalId, x.PostedJournalId, x.PostedJournal.EntryDate,
                x.PostedJournal.Reference, x.LedgerAccount.Code == "1150",
                x.LedgerAccount.Code == "1150" ? x.Debit - x.Credit : x.Credit - x.Debit))
            .ToListAsync(ct);

        var bank = await ReadBankEntriesAsync(db, organisationId, from, to, ct);
        var expected = documents.Concat(bank.Select(x => x.Source)).ToLookup(Key);
        var actual = ledger.ToLookup(Key);
        var differences = new List<VatDifference>();
        foreach (var key in expected.Select(g => g.Key).Union(actual.Select(g => g.Key)))
        {
            var workpaperTax = expected[key].Sum(x => x.Tax);
            var ledgerTax = actual[key].Sum(x => x.Tax);
            if (workpaperTax != ledgerTax)
            {
                var source = expected[key].FirstOrDefault() ?? actual[key].First();
                differences.Add(new(source, workpaperTax, ledgerTax));
            }
        }

        var hasXeroEvidence = await db.XeroMirrorRecords.AnyAsync(x => x.OrganisationId == organisationId &&
            (x.DatasetKey == "sales-invoices" || x.DatasetKey == "bills" || x.DatasetKey == "bank-transactions" ||
             x.DatasetKey == "manual-journals" || x.DatasetKey == "sales-credits" || x.DatasetKey == "supplier-credits" ||
             x.DatasetKey == "prepayments" || x.DatasetKey == "overpayments"), ct);
        return new(ledger.Where(x => x.IsInput).Sum(x => x.Tax), ledger.Where(x => !x.IsInput).Sum(x => x.Tax),
            bank, differences.OrderBy(x => x.Source.Date).ThenBy(x => x.Source.Reference).ToList(), hasXeroEvidence);
    }

    private static (Guid, bool) Key(VatSource source) => (source.JournalId ?? source.SourceId, source.IsInput);

    // The append-only audit records retain the generated VAT separately from the account coded by the user.
    // Matching a bill payment produces no coding event. A reopened coding has a separate reversal journal.
    private static async Task<List<BankVatEntry>> ReadBankEntriesAsync(ApplicationDbContext db,
        Guid organisationId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<BankAuditRow>($"""
            WITH codings AS (
                SELECT EntityId, JsonData, upper(json_extract(JsonData, '$.JournalId')) AS OriginalId
                FROM AuditEvents WHERE OrganisationId = {organisationId} AND EventType = 'BankTransactionCoded'
            ), movements AS (
                SELECT c.JsonData, c.OriginalId, c.OriginalId AS JournalId, 1 AS Direction FROM codings c
                UNION ALL
                SELECT c.JsonData, c.OriginalId, upper(json_extract(r.JsonData, '$.ReversalJournalId')), -1
                FROM AuditEvents r JOIN codings c ON c.EntityId = r.EntityId
                    AND c.OriginalId = upper(json_extract(r.JsonData, '$.OriginalJournalId'))
                WHERE r.OrganisationId = {organisationId} AND r.EventType = 'BankTransactionCodingReopened'
            )
            SELECT j.Id AS JournalId, j.Reference, j.EntryDate AS Date, m.JsonData, m.Direction, target.Type AS TargetType,
                EXISTS(SELECT 1 FROM PostedJournalLines l JOIN LedgerAccounts a ON a.Id = l.LedgerAccountId
                       WHERE l.PostedJournalId = m.OriginalId AND a.Code = '1150'
                         AND a.Id <> upper(json_extract(m.JsonData, '$.TargetAccountId'))) AS HasGeneratedInput
            FROM movements m JOIN PostedJournals j ON j.Id = m.JournalId
            JOIN LedgerAccounts target ON target.Id = upper(json_extract(m.JsonData, '$.TargetAccountId')) AND target.OrganisationId = j.OrganisationId
            WHERE j.OrganisationId = {organisationId} AND j.EntryDate BETWEEN {from} AND {to}
                AND (json_extract(m.JsonData, '$.VatAmount') > 0 OR
                     json_extract(m.JsonData, '$.VatTreatment') IN ({(int)VatTreatment.ZeroRated}, {(int)VatTreatment.Exempt}))
            ORDER BY j.EntryDate, j.SequenceNumber
            """).ToListAsync(ct);
        var entries = new List<BankVatEntry>();
        foreach (var row in rows)
        {
            using var audit = JsonDocument.Parse(row.JsonData);
            var amount = audit.RootElement.GetProperty("Amount").GetDecimal();
            var tax = audit.RootElement.GetProperty("VatAmount").GetDecimal();
            var treatment = (VatTreatment)audit.RootElement.GetProperty("VatTreatment").GetInt32();
            var isInput = tax != 0m ? amount < 0 || row.HasGeneratedInput : row.TargetType switch
            {
                AccountType.Expense => true,
                AccountType.Revenue => false,
                _ => amount < 0
            };
            var sign = row.Direction * (isInput ? -Math.Sign(amount) : Math.Sign(amount));
            entries.Add(new(new(row.JournalId, row.JournalId, row.Date, row.Reference, isInput, sign * tax),
                sign * (Math.Abs(amount) - tax), treatment));
        }
        return entries;
    }

    private sealed class BankAuditRow
    {
        public Guid JournalId { get; set; }
        public required string Reference { get; set; }
        public DateOnly Date { get; set; }
        public required string JsonData { get; set; }
        public int Direction { get; set; }
        public AccountType TargetType { get; set; }
        public bool HasGeneratedInput { get; set; }
    }
}
