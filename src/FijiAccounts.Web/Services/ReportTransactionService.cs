using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record ReportTransaction(Guid JournalId, DateOnly Date, long JournalNumber, string Reference, string Description,
    decimal Amount, string SourceLabel, string SourceUrl, string? Contact, IReadOnlyList<ReportAttachment> Attachments);
public sealed record ReportAttachment(string FileName, string Url, string ContentType);
public sealed record ReportTransactionResult(string AccountCode, string AccountName, string Currency, string Scope,
    IReadOnlyList<ReportTransaction> Transactions)
{
    public decimal Total => Transactions.Sum(x => x.Amount);
}

public sealed class ReportTransactionService(ApplicationDbContext db, TenantAccessService access)
{
    public async Task<ReportTransactionResult> GetAsync(string userId, Guid organisationId, string accountCode, DateOnly from, DateOnly to, string tracking = "all", CancellationToken ct = default)
    {
        var organisation = await access.FindAsync(userId, organisationId) ?? throw new UnauthorizedAccessException("You cannot access this organisation.");
        if (from > to) throw new InvalidOperationException("The From date must be on or before the To date.");
        var account = await db.LedgerAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.OrganisationId == organisationId && x.Code == accountCode && (x.Type == AccountType.Revenue || x.Type == AccountType.Expense), ct)
            ?? throw new InvalidOperationException("Income or expense account not found.");
        var scope = "All accessible transactions";
        var divisions = await access.GetReportDivisionScopeAsync(userId, organisationId, ct);
        if (tracking != "all")
        {
            var branches = await access.ListAccessibleBranchesAsync(userId, organisationId);
            if (tracking.StartsWith("branch:") && Guid.TryParse(tracking[7..], out var branchId))
            {
                var branch = branches.SingleOrDefault(x => x.Id == branchId) ?? throw new UnauthorizedAccessException("Branch access denied.");
                divisions = branch.Divisions.Select(x => x.Id).ToArray(); scope = $"{branch.Code} - {branch.Name}";
            }
            else if (tracking.StartsWith("division:") && Guid.TryParse(tracking[9..], out var divisionId))
            {
                var branch = branches.SingleOrDefault(x => x.Divisions.Any(d => d.Id == divisionId)) ?? throw new UnauthorizedAccessException("Division access denied.");
                var division = branch.Divisions.Single(x => x.Id == divisionId);
                divisions = [divisionId]; scope = $"{branch.Code} / {division.Code} - {division.Name}";
            }
            else throw new InvalidOperationException("Invalid tracking selection.");
        }
        var query = db.PostedJournalLines.AsNoTracking().Where(x => x.PostedJournal.OrganisationId == organisationId && x.LedgerAccountId == account.Id && x.PostedJournal.EntryDate >= from && x.PostedJournal.EntryDate <= to);
        if (divisions is not null) query = query.Where(x => x.DivisionId != null && divisions.Contains(x.DivisionId.Value));
        var lines = await query.OrderBy(x => x.PostedJournal.EntryDate).ThenBy(x => x.PostedJournal.SequenceNumber).ThenBy(x => x.Id)
            .Select(x => new { JournalId = x.PostedJournalId, x.PostedJournal.EntryDate, x.PostedJournal.SequenceNumber, x.PostedJournal.Reference, x.Description, JournalDescription = x.PostedJournal.Description, x.Debit, x.Credit }).ToListAsync(ct);
        var ids = lines.Select(x => x.JournalId).Distinct().ToArray();
        var sources = await Sources(organisationId, ids, ct);
        var attachments = await Attachments(organisationId, ids, ct);
        return new(account.Code, account.Name, organisation.Organisation.BaseCurrency, scope, lines.Select(x =>
        {
            sources.TryGetValue(x.JournalId, out var source);
            return new ReportTransaction(x.JournalId, x.EntryDate, x.SequenceNumber, x.Reference,
                string.IsNullOrWhiteSpace(x.Description) || x.Description == x.Reference ? x.JournalDescription ?? x.Description : x.Description,
                account.Type == AccountType.Revenue ? x.Credit - x.Debit : x.Debit - x.Credit,
                source?.Label ?? "Journal entry", source?.Url ?? $"/o/{organisationId}/journals/{x.JournalId}", source?.Contact,
                attachments.GetValueOrDefault(x.JournalId) ?? []);
        }).ToArray());
    }

    private sealed record Source(Guid JournalId, string Label, string Url, string? Contact);
    private async Task<Dictionary<Guid, IReadOnlyList<ReportAttachment>>> Attachments(Guid organisationId, Guid[] ids, CancellationToken ct)
    {
        if (ids.Length == 0) return [];
        var billLinks = await db.SupplierBills.AsNoTracking().Where(x => x.OrganisationId == organisationId && ids.Contains(x.PostedJournalId))
            .Select(x => new { JournalId = x.PostedJournalId, BillId = x.Id }).ToListAsync(ct);
        billLinks.AddRange(await db.SupplierBillVoids.AsNoTracking().Where(x => x.OrganisationId == organisationId && ids.Contains(x.PostedJournalId))
            .Select(x => new { JournalId = x.PostedJournalId, BillId = x.SupplierBillId }).ToListAsync(ct));
        billLinks.AddRange(await db.SupplierBillReinstatements.AsNoTracking().Where(x => x.OrganisationId == organisationId && ids.Contains(x.PostedJournalId))
            .Select(x => new { JournalId = x.PostedJournalId, BillId = x.SupplierBillId }).ToListAsync(ct));
        billLinks.AddRange(await db.SupplierCreditNotes.AsNoTracking().Where(x => x.OrganisationId == organisationId && ids.Contains(x.PostedJournalId))
            .Select(x => new { JournalId = x.PostedJournalId, BillId = x.SupplierBillId }).ToListAsync(ct));
        var billIds = billLinks.Select(x => x.BillId).Distinct().ToArray();
        var files = await db.SupplierBillAttachments.AsNoTracking().Where(x => x.OrganisationId == organisationId && billIds.Contains(x.SupplierBillId))
            .Select(x => new { x.SupplierBillId, x.Id, x.FileName, x.ContentType }).ToListAsync(ct);
        var result = new Dictionary<Guid, List<ReportAttachment>>();
        void Add(Guid journalId, ReportAttachment file)
        {
            if (!result.TryGetValue(journalId, out var list)) result[journalId] = list = [];
            if (!list.Any(x => x.Url == file.Url)) list.Add(file);
        }
        foreach (var link in billLinks)
            foreach (var file in files.Where(x => x.SupplierBillId == link.BillId))
                Add(link.JournalId, new(file.FileName, $"/api/o/{organisationId}/purchases/{file.SupplierBillId}/attachments/{file.Id}", file.ContentType));
        var bankLinks = await db.BankStatementLines.AsNoTracking().Where(x => x.OrganisationId == organisationId && x.MatchedPostedJournalLine != null && ids.Contains(x.MatchedPostedJournalLine.PostedJournalId) && x.ImportBatchId != null)
            .Select(x => new { JournalId = x.MatchedPostedJournalLine!.PostedJournalId, BatchId = x.ImportBatchId!.Value }).Distinct().ToListAsync(ct);
        var batchIds = bankLinks.Select(x => x.BatchId).Distinct().ToArray();
        var statements = await db.BankStatementImportDocuments.AsNoTracking().Where(x => x.OrganisationId == organisationId && batchIds.Contains(x.ImportBatchId))
            .Select(x => new { x.ImportBatchId, x.FileName, x.ContentType }).ToListAsync(ct);
        foreach (var link in bankLinks)
            foreach (var file in statements.Where(x => x.ImportBatchId == link.BatchId))
                Add(link.JournalId, new(file.FileName, $"/api/o/{organisationId}/banking/imports/{file.ImportBatchId}/statement", file.ContentType));
        return result.ToDictionary(x => x.Key, x => (IReadOnlyList<ReportAttachment>)x.Value.OrderBy(file => file.FileName).ToArray());
    }
    private async Task<Dictionary<Guid, Source>> Sources(Guid organisationId, Guid[] ids, CancellationToken ct)
    {
        var sources = new Dictionary<Guid, Source>();
        if (ids.Length == 0) return sources;
        var prefix = $"/o/{organisationId}";
        var payroll = await db.PayrollIslandPayRunImports.AsNoTracking()
            .Where(x => x.OrganisationId == organisationId && x.PostedJournalId != null)
            .OrderByDescending(x => x.Revision).ToListAsync(ct);
        var correctionRefs = await db.PostedJournals.AsNoTracking().Where(x => x.OrganisationId == organisationId && ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Reference }).ToListAsync(ct);
        foreach (var match in await db.PayrollBankMatches.AsNoTracking().Where(x => x.OrganisationId == organisationId && ids.Contains(x.PostedJournalId)).ToListAsync(ct))
            sources.TryAdd(match.PostedJournalId, new(match.PostedJournalId, "Payroll payment", prefix + "/payroll/" + match.PayRunImportId, null));
        foreach (var run in payroll)
        {
            var source = new Source(run.PostedJournalId!.Value, "Payroll " + run.PayRunNumber, prefix + "/payroll/" + run.Id, null);
            if (ids.Contains(source.JournalId)) sources.TryAdd(source.JournalId, source);
            foreach (var correction in correctionRefs.Where(x => x.Reference == $"PAY-SPLIT-{run.PostedJournalId.Value:N}"))
                sources.TryAdd(correction.Id, source with { JournalId = correction.Id });
        }
        void Add(IEnumerable<Source> entries) { foreach (var source in entries) sources.TryAdd(source.JournalId, source); }
        Add(await db.SalesInvoices.AsNoTracking().Where(x => x.OrganisationId == organisationId && x.PostedJournalId != null && ids.Contains(x.PostedJournalId.Value))
            .Select(x => new Source(x.PostedJournalId!.Value, "Invoice " + x.InvoiceNumber, prefix + "/sales/" + x.Id, x.Customer.Name)).ToListAsync(ct));
        Add(await db.SupplierBills.AsNoTracking().Where(x => x.OrganisationId == organisationId && ids.Contains(x.PostedJournalId))
            .Select(x => new Source(x.PostedJournalId, "Bill " + x.SupplierReference, prefix + "/purchases/" + x.Id, x.Supplier.Name)).ToListAsync(ct));
        Add(await db.SalesInvoiceVoids.AsNoTracking().Where(x => x.OrganisationId == organisationId && x.PostedJournalId != null && ids.Contains(x.PostedJournalId.Value))
            .Select(x => new Source(x.PostedJournalId!.Value, "Invoice reversal " + x.SalesInvoice.InvoiceNumber, prefix + "/sales/" + x.SalesInvoiceId, x.SalesInvoice.Customer.Name)).ToListAsync(ct));
        Add(await db.SupplierBillVoids.AsNoTracking().Where(x => x.OrganisationId == organisationId && ids.Contains(x.PostedJournalId))
            .Select(x => new Source(x.PostedJournalId, "Bill reversal " + x.SupplierBill.BillNumber, prefix + "/purchases/" + x.SupplierBillId, x.SupplierBill.Supplier.Name)).ToListAsync(ct));
        Add(await db.SupplierBillReinstatements.AsNoTracking().Where(x => x.OrganisationId == organisationId && ids.Contains(x.PostedJournalId))
            .Select(x => new Source(x.PostedJournalId, "Bill reinstatement " + x.SupplierBill.BillNumber, prefix + "/purchases/" + x.SupplierBillId, x.SupplierBill.Supplier.Name)).ToListAsync(ct));
        Add(await db.SalesCreditNotes.AsNoTracking().Where(x => x.OrganisationId == organisationId && x.PostedJournalId != null && ids.Contains(x.PostedJournalId.Value))
            .Select(x => new Source(x.PostedJournalId!.Value, "Credit note " + x.CreditNoteNumber, prefix + "/sales/credits/" + x.Id, x.SalesInvoice.Customer.Name)).ToListAsync(ct));
        Add(await db.SupplierCreditNotes.AsNoTracking().Where(x => x.OrganisationId == organisationId && ids.Contains(x.PostedJournalId))
            .Select(x => new Source(x.PostedJournalId, "Supplier credit " + x.CreditNoteNumber, prefix + "/purchases/" + x.SupplierBillId, x.SupplierBill.Supplier.Name)).ToListAsync(ct));
        Add(await db.BankStatementLines.AsNoTracking().Where(x => x.OrganisationId == organisationId && x.MatchedPostedJournalLine != null && ids.Contains(x.MatchedPostedJournalLine.PostedJournalId) && x.ImportBatchId != null && db.BankStatementImportDocuments.Any(document => document.OrganisationId == organisationId && document.ImportBatchId == x.ImportBatchId))
            .Select(x => new Source(x.MatchedPostedJournalLine!.PostedJournalId, "Bank statement", "/api/o/" + organisationId + "/banking/imports/" + x.ImportBatchId + "/statement", null)).ToListAsync(ct));
        return sources;
    }
}
