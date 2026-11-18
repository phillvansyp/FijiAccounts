using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace FijiAccounts.Web.Services;

public sealed record VatTreatmentSummary(
    decimal StandardNet,
    decimal StandardTax,
    decimal ZeroRatedNet,
    decimal ExemptNet,
    decimal OutOfScopeNet)
{
    public decimal TotalTax => StandardTax;
}

public sealed record VatAdjustmentSummary(
    decimal Net,
    decimal Tax);

public sealed record VatBillAdjustment(Guid BillId, string BillNumber, DateOnly BillDate,
    DateOnly Date, string Kind, string Reason, decimal Net, decimal Tax);

public sealed record VatWorkpaper(
    DateOnly From,
    DateOnly To,
    VatTreatmentSummary Sales,
    VatAdjustmentSummary SalesCredits,
    VatTreatmentSummary Purchases,
    VatAdjustmentSummary SupplierCredits)
{
    public VatTreatmentSummary? PurchasesBeforeAdjustments { get; init; }
    public IReadOnlyList<VatBillAdjustment> BillAdjustments { get; init; } = [];

    public IReadOnlyList<FijiVatScheduleLine> LocalSchedule { get; init; } = [];
    public FijiVatFigures? FilingFigures { get; init; }
    public FijiVatReconciliation? Reconciliation { get; init; }

    public decimal OutputTax =>
        Sales.TotalTax - SalesCredits.Tax;

    public decimal InputTax =>
        Purchases.TotalTax - SupplierCredits.Tax;

    public decimal NetTax =>
        OutputTax - InputTax;
}

public sealed class VatWorkpaperService(
    ApplicationDbContext db)
{
    public async Task<VatWorkpaper> GetAsync(
        Guid organisationId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        if (from > to)
        {
            throw new ArgumentException(
                "The start date must be before the end date.");
        }

        // Read documents, bank evidence and control journals from the same SQLite snapshot.
        if (db.Database.CurrentTransaction is not null || !db.Database.IsSqlite())
            return await ReadAsync(organisationId, from, to, ct);

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var connection = (SqliteConnection)db.Database.GetDbConnection();
            await using var snapshot = connection.BeginTransaction(deferred: true);
            await using var transaction = await db.Database.UseTransactionAsync(snapshot, ct);
            var report = await ReadAsync(organisationId, from, to, ct);
            await snapshot.CommitAsync(ct);
            return report;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<VatWorkpaper> ReadAsync(Guid organisationId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var sales =
            await db.SalesInvoiceLines
                .AsNoTracking()
                .Where(x =>
                    x.SalesInvoice.OrganisationId == organisationId &&
                    x.SalesInvoice.IssueDate >= from &&
                    x.SalesInvoice.IssueDate <= to &&
                    x.SalesInvoice.Status != InvoiceStatus.Draft)
                .Select(x =>
                    new VatLine(
                        x.VatTreatment,
                        x.NetAmount,
                        x.VatAmount,
                        new VatSource(x.SalesInvoice.PostedJournalId, x.SalesInvoiceId, x.SalesInvoice.IssueDate, x.SalesInvoice.InvoiceNumber, false, 0m) { Party = x.SalesInvoice.RecipientNameSnapshot ?? x.SalesInvoice.Customer.Name, Tin = x.SalesInvoice.RecipientTinSnapshot ?? x.SalesInvoice.Customer.Tin }))
                .ToListAsync(ct);

        var salesVoids =
            await db.SalesInvoiceVoids.AsNoTracking()
                .Where(v => v.OrganisationId == organisationId && v.Status == SalesInvoiceVoidStatus.Posted &&
                    v.VoidDate >= from && v.VoidDate <= to)
                .Join(db.SalesInvoiceLines, v => v.SalesInvoiceId, x => x.SalesInvoiceId, (v, x) => new VatLine(
                    x.VatTreatment, -x.NetAmount, -x.VatAmount,
                    new VatSource(v.PostedJournalId, v.Id, v.VoidDate, v.SalesInvoice.InvoiceNumber, false, 0m) { Party = v.SalesInvoice.RecipientNameSnapshot ?? v.SalesInvoice.Customer.Name, Tin = v.SalesInvoice.RecipientTinSnapshot ?? v.SalesInvoice.Customer.Tin }))
                .ToListAsync(ct);

        sales.AddRange(salesVoids);

        var purchases =
            await db.SupplierBillLines
                .AsNoTracking()
                .Where(x =>
                    x.SupplierBill.OrganisationId == organisationId &&
                    x.SupplierBill.BillDate >= from &&
                    x.SupplierBill.BillDate <= to)
                .Select(x =>
                    new VatLine(
                        x.VatTreatment,
                        x.NetAmount,
                        x.VatAmount,
                        new VatSource(x.SupplierBill.PostedJournalId, x.SupplierBillId, x.SupplierBill.BillDate, x.SupplierBill.BillNumber, true, 0m) { Party = x.SupplierBill.Supplier.Name, Tin = x.SupplierBill.Supplier.Tin }))
                .ToListAsync(ct);

        var originalPurchases = purchases.ToList();
        var adjustments = await db.SupplierBillVoids.AsNoTracking()
            .Where(v => v.OrganisationId == organisationId && v.VoidDate >= from && v.VoidDate <= to)
            .Select(v => new VatBillAdjustment(v.SupplierBillId, v.SupplierBill.BillNumber,
                v.SupplierBill.BillDate, v.VoidDate, "Bill reversal", v.Reason,
                -v.SupplierBill.Subtotal, -v.SupplierBill.VatTotal)).ToListAsync(ct);
        adjustments.AddRange(await db.SupplierBillReinstatements.AsNoTracking()
            .Where(v => v.OrganisationId == organisationId && v.ReinstatementDate >= from && v.ReinstatementDate <= to)
            .Select(v => new VatBillAdjustment(v.SupplierBillId, v.SupplierBill.BillNumber,
                v.SupplierBill.BillDate, v.ReinstatementDate, "Bill reinstatement", v.Reason,
                v.SupplierBill.Subtotal, v.SupplierBill.VatTotal)).ToListAsync(ct));

        var purchaseVoids = await db.SupplierBillVoids.AsNoTracking()
            .Where(v => v.OrganisationId == organisationId && v.VoidDate >= from && v.VoidDate <= to)
            .Join(db.SupplierBillLines, v => v.SupplierBillId, x => x.SupplierBillId, (v, x) => new VatLine(x.VatTreatment, -x.NetAmount, -x.VatAmount,
                new VatSource(v.PostedJournalId, v.Id, v.VoidDate, v.SupplierBill.BillNumber, true, 0m) { Party = v.SupplierBill.Supplier.Name, Tin = v.SupplierBill.Supplier.Tin }))
            .ToListAsync(ct);
        purchases.AddRange(purchaseVoids);

        var purchaseReinstatements = await db.SupplierBillReinstatements.AsNoTracking()
            .Where(r => r.OrganisationId == organisationId && r.ReinstatementDate >= from && r.ReinstatementDate <= to)
            .Join(db.SupplierBillLines, r => r.SupplierBillId, x => x.SupplierBillId, (r, x) => new VatLine(x.VatTreatment, x.NetAmount, x.VatAmount,
                new VatSource(r.PostedJournalId, r.Id, r.ReinstatementDate, r.SupplierBill.BillNumber, true, 0m) { Party = r.SupplierBill.Supplier.Name, Tin = r.SupplierBill.Supplier.Tin }))
            .ToListAsync(ct);
        purchases.AddRange(purchaseReinstatements);

        var salesCredits =
            await db.SalesCreditNotes
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == organisationId &&
                    x.Status == SalesCreditNoteStatus.Posted &&
                    x.CreditDate >= from &&
                    x.CreditDate <= to)
                .Select(x =>
                    new CreditEntry(x.Subtotal, x.VatTotal,
                        new VatSource(x.PostedJournalId, x.Id, x.CreditDate, x.CreditNoteNumber, false, 0m)))
                .ToListAsync(ct);

        var salesCreditReversals =
            await db.SalesCreditNoteReversals
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == organisationId &&
                    x.Status == SalesCreditNoteReversalStatus.Posted &&
                    x.ReversalDate >= from &&
                    x.ReversalDate <= to)
                .Select(x =>
                    new CreditEntry(x.SalesCreditNote.Subtotal, x.SalesCreditNote.VatTotal,
                        new VatSource(x.PostedJournalId, x.Id, x.ReversalDate, x.SalesCreditNote.CreditNoteNumber, false, 0m)))
                .ToListAsync(ct);

        var supplierCredits =
            await db.SupplierCreditNotes
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == organisationId &&
                    x.CreditDate >= from &&
                    x.CreditDate <= to)
                .Select(x =>
                    new CreditEntry(x.Subtotal, x.VatTotal,
                        new VatSource(x.PostedJournalId, x.Id, x.CreditDate, x.CreditNoteNumber, true, 0m)))
                .ToListAsync(ct);

        var supplierCreditReversals =
            await db.SupplierCreditNoteReversals
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == organisationId &&
                    x.ReversalDate >= from &&
                    x.ReversalDate <= to)
                .Select(x =>
                    new CreditEntry(x.SupplierCreditNote.Subtotal, x.SupplierCreditNote.VatTotal,
                        new VatSource(x.PostedJournalId, x.Id, x.ReversalDate, x.SupplierCreditNote.CreditNoteNumber, true, 0m)))
                .ToListAsync(ct);

        var isFiji = await db.Organisations.AnyAsync(x => x.Id == organisationId && x.CountryCode == "FJ", ct);
        FijiVatReconciliation? reconciliation = null;
        if (isFiji)
        {
            var documents = sales.Concat(purchases).Where(x => x.Treatment == VatTreatment.Standard && x.Tax != 0m)
                .Select(x => x.Source with { Tax = x.Tax });
            var credits = salesCredits.Concat(supplierCredits).Select(x => x.Source with { Tax = -x.Tax });
            var reversals = salesCreditReversals.Concat(supplierCreditReversals).Select(x => x.Source with { Tax = x.Tax });
            reconciliation = await FijiVatReconciliation.ReadAsync(db, organisationId, from, to,
                documents.Concat(credits).Concat(reversals), ct);
            foreach (var bank in reconciliation.BankEntries)
            {
                var lines = bank.Source.IsInput ? purchases : sales;
                var line = new VatLine(bank.Treatment, bank.Net, bank.Source.Tax, bank.Source);
                lines.Add(line);
                if (bank.Source.IsInput) originalPurchases.Add(line);
            }
        }

        var summarisedSales = Summarise(sales);
        var salesCreditTotals = NetAdjustments(salesCredits, salesCreditReversals);
        var supplierCreditTotals = NetAdjustments(supplierCredits, supplierCreditReversals);
        var summarisedPurchases = Summarise(purchases);
        FijiVatFigures? filing = null;
        if (isFiji)
        {
            var creditLines = await db.SalesCreditNoteLines.AsNoTracking()
                .Where(x => x.SalesCreditNote.OrganisationId == organisationId && x.SalesCreditNote.Status == SalesCreditNoteStatus.Posted &&
                    x.SalesCreditNote.CreditDate >= from && x.SalesCreditNote.CreditDate <= to)
                .Select(x => new { x.VatTreatment, Net = -x.NetAmount }).ToListAsync(ct);
            creditLines.AddRange(await db.SalesCreditNoteReversals.AsNoTracking()
                .Where(r => r.OrganisationId == organisationId && r.Status == SalesCreditNoteReversalStatus.Posted && r.ReversalDate >= from && r.ReversalDate <= to)
                .Join(db.SalesCreditNoteLines, r => r.SalesCreditNoteId, x => x.SalesCreditNoteId,
                    (r,x) => new { x.VatTreatment, Net = x.NetAmount }).ToListAsync(ct));
            var output = summarisedSales.TotalTax - salesCreditTotals.Tax;
            filing = new(summarisedSales.StandardNet + output + creditLines.Where(x => x.VatTreatment == VatTreatment.Standard).Sum(x => x.Net),
                summarisedSales.ZeroRatedNet + creditLines.Where(x => x.VatTreatment == VatTreatment.ZeroRated).Sum(x => x.Net),
                summarisedSales.ExemptNet + creditLines.Where(x => x.VatTreatment == VatTreatment.Exempt).Sum(x => x.Net),
                output, summarisedPurchases.TotalTax - supplierCreditTotals.Tax);
        }

        return new VatWorkpaper(
            from,
            to,
            summarisedSales, salesCreditTotals, summarisedPurchases, supplierCreditTotals)
        {
            LocalSchedule = isFiji ? sales.Concat(purchases).Select(x => new FijiVatScheduleLine(x.Source, x.Treatment.ToString(), x.Net, x.Tax))
                .Concat(salesCredits.Concat(supplierCredits).Select(x => new FijiVatScheduleLine(x.Source, "Credit", -x.Net, -x.Tax)))
                .Concat(salesCreditReversals.Concat(supplierCreditReversals).Select(x => new FijiVatScheduleLine(x.Source, "Credit reversal", x.Net, x.Tax)))
                .OrderBy(x => x.Source.Date).ThenBy(x => x.Source.Reference).ThenBy(x => x.Source.SourceId)
                .ThenBy(x => x.Treatment).ThenBy(x => x.Net).ThenBy(x => x.Vat).ToArray() : [],
            FilingFigures = filing,
            Reconciliation = reconciliation,
            PurchasesBeforeAdjustments = Summarise(originalPurchases),
            BillAdjustments = adjustments.OrderBy(x => x.Date).ThenBy(x => x.BillNumber).ToArray()
        };
    }

    private static VatTreatmentSummary Summarise(
        IEnumerable<VatLine> lines) =>
        new(
            SumNet(lines, VatTreatment.Standard),
            SumTax(lines, VatTreatment.Standard),
            SumNet(lines, VatTreatment.ZeroRated),
            SumNet(lines, VatTreatment.Exempt),
            SumNet(lines, VatTreatment.OutOfScope));

    private static VatAdjustmentSummary NetAdjustments(
        IEnumerable<CreditEntry> credits,
        IEnumerable<CreditEntry> reversals) =>
        new(
            credits.Sum(x => x.Net) -
                reversals.Sum(x => x.Net),
            credits.Sum(x => x.Tax) -
                reversals.Sum(x => x.Tax));

    private static decimal SumNet(
        IEnumerable<VatLine> lines,
        VatTreatment treatment) =>
        lines
            .Where(x => x.Treatment == treatment)
            .Sum(x => x.Net);

    private static decimal SumTax(
        IEnumerable<VatLine> lines,
        VatTreatment treatment) =>
        lines
            .Where(x => x.Treatment == treatment)
            .Sum(x => x.Tax);

    private sealed record CreditEntry(decimal Net, decimal Tax, VatSource Source);

    private sealed record VatLine(
        VatTreatment Treatment,
        decimal Net,
        decimal Tax,
        VatSource Source);
}
