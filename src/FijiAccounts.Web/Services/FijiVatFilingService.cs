using System.Globalization;
using System.Text;
using System.Text.Json;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record FijiVatScheduleLine(VatSource Source, string Treatment, decimal Net, decimal Vat)
{
    public static byte[] Csv(IEnumerable<FijiVatScheduleLine> rows, string currency)
    {
        var csv = new StringBuilder("Date,Side,Reference,Party,TIN,Treatment,Currency,Net,VAT,Gross,Journal ID,Source ID\r\n");
        foreach (var row in rows)
        {
            var s = row.Source;
            csv.AppendLine(string.Join(",", s.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), s.IsInput ? "Purchase" : "Sale",
                Field(s.Reference), Field(s.Party), Field(s.Tin), Field(row.Treatment), Field(currency),
                Amount(row.Net), Amount(row.Vat), Amount(row.Net + row.Vat), s.JournalId, s.SourceId));
        }
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Field(string? value)
    {
        var text = value ?? "";
        if (text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' || text.StartsWith('\t') || text.StartsWith('\r'))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}

public sealed record FijiVatFigures(decimal StandardSalesIncludingVat, decimal ZeroRatedSales, decimal ExemptSales,
    decimal OutputVat, decimal InputVat);

public sealed class FijiVatFilingForm
{
    public bool PaymentsBasis { get; set; }
    public string ApprovalReference { get; set; } = "";
    public decimal? StandardSalesIncludingVat { get; set; }
    public decimal? ZeroRatedSales { get; set; }
    public decimal? ExemptSales { get; set; }
    public decimal? OutputVat { get; set; }
    public decimal? InputVat { get; set; }
    public decimal OutputAdjustment { get; set; }
    public decimal InputAdjustment { get; set; }
    public decimal CustomsVat { get; set; }
    public decimal ReverseChargePaid { get; set; }
    public decimal? ReverseChargeOnPortal { get; set; }
    public decimal NonBusinessVat { get; set; }
    public string PaymentEvidence { get; set; } = "";
    public string Notes { get; set; } = "";
    public string FilingReference { get; set; } = "";
    public DateOnly? FiledOn { get; set; }
    public decimal? NetVat => OutputVat + OutputAdjustment - (InputVat + CustomsVat + ReverseChargePaid + InputAdjustment - NonBusinessVat);

    public static FijiVatFilingForm FromLocal(FijiVatFigures local) => new()
    {
        StandardSalesIncludingVat = local.StandardSalesIncludingVat, ZeroRatedSales = local.ZeroRatedSales,
        ExemptSales = local.ExemptSales, OutputVat = local.OutputVat, InputVat = local.InputVat
    };
}

public sealed record FijiVatFilingSnapshot(DateOnly From, DateOnly To, string Currency, FijiVatFigures Local,
    FijiVatFilingForm Return, bool Filed, bool XeroReview, int LedgerDifferences, Guid? LocalScheduleId = null);
public sealed record FijiVatFilingRevision(long Id, DateTimeOffset SavedAt, FijiVatFilingSnapshot Snapshot);

public sealed class FijiVatFilingService(ApplicationDbContext db, TenantAccessService access, VatWorkpaperService workpapers, IImmutableDocumentStore storage)
{
    private const string Event = "FijiVatFilingSaved";
    private static string Key(DateOnly from, DateOnly to) => $"{from:yyyy-MM-dd}/{to:yyyy-MM-dd}";

    private async Task<Organisation> AuthoriseAsync(string user, Guid org, CancellationToken ct)
    {
        var company = (await access.FindAsync(user, org))?.Organisation;
        if (company?.CountryCode != "FJ" || await access.GetReportDivisionScopeAsync(user, org, ct) is not null)
            throw new UnauthorizedAccessException("Organisation-wide Fiji reporting access is required.");
        return company;
    }

    public async Task<IReadOnlyList<FijiVatFilingRevision>> HistoryAsync(string user, Guid org, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await AuthoriseAsync(user, org, ct);
        var rows = await db.AuditEvents.AsNoTracking().Where(x => x.OrganisationId == org && x.EventType == Event && x.EntityId == Key(from, to))
            .OrderByDescending(x => x.Id).Select(x => new { x.Id, x.OccurredAt, x.JsonData }).ToListAsync(ct);
        return rows.Select(x => new FijiVatFilingRevision(x.Id, x.OccurredAt, JsonSerializer.Deserialize<FijiVatFilingSnapshot>(x.JsonData)!)).ToList();
    }

    public async Task<byte[]?> ReadScheduleAsync(string user, Guid org, long revisionId, CancellationToken ct)
    {
        await AuthoriseAsync(user, org, ct);
        var json = await db.AuditEvents.AsNoTracking().Where(x => x.OrganisationId == org && x.EventType == Event && x.Id == revisionId)
            .Select(x => x.JsonData).SingleOrDefaultAsync(ct);
        var id = json is null ? null : JsonSerializer.Deserialize<FijiVatFilingSnapshot>(json)?.LocalScheduleId;
        return id.HasValue ? await storage.ReadVerifiedAsync(org, id.Value, ct) : null;
    }

    public async Task SaveAsync(string user, Guid org, DateOnly from, DateOnly to, FijiVatFigures reviewedLocal,
        FijiVatFilingForm form, bool filed, long expectedRevision, bool reviewed, CancellationToken ct = default)
    {
        var company = await AuthoriseAsync(user, org, ct);
        if (!await access.CanPostJournalsAsync(user, org))
            throw new UnauthorizedAccessException("Posting access is required to save a filing pack.");
        if (!string.Equals(company.BaseCurrency, "FJD", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This filing pack requires FJD base figures.");
        if (from > to) throw new ArgumentException("Check the filing period.");
        if (form.PaymentsBasis && string.IsNullOrWhiteSpace(form.ApprovalReference))
            throw new InvalidOperationException("Enter the FRCS payments-basis approval reference.");
        if (form.CustomsVat < 0 || form.ReverseChargePaid < 0 || form.ReverseChargeOnPortal < 0 || form.NonBusinessVat < 0)
            throw new InvalidOperationException("Enter non-negative customs, reverse-charge and non-business VAT amounts.");
        if ((form.CustomsVat != 0 || form.ReverseChargePaid != 0) && string.IsNullOrWhiteSpace(form.PaymentEvidence))
            throw new InvalidOperationException("Enter the customs or reverse-charge payment reference.");
        if ((form.InputAdjustment != 0 || form.OutputAdjustment != 0) && string.IsNullOrWhiteSpace(form.Notes))
            throw new InvalidOperationException("Explain the return adjustments.");
        if (filed && (!reviewed || string.IsNullOrWhiteSpace(form.FilingReference) || form.FiledOn is null ||
            form.FiledOn < to || form.FiledOn > DateOnly.FromDateTime(OrganisationTime.Convert(DateTimeOffset.UtcNow, company).DateTime)))
            throw new InvalidOperationException("Confirm the figures and enter the external filing reference and a valid filing date.");
        if (filed && (form.StandardSalesIncludingVat is null || form.ExemptSales is null || form.ZeroRatedSales is null ||
            form.OutputVat is null || form.InputVat is null))
            throw new InvalidOperationException("Complete all return figures, including zero amounts, before recording filing.");
        if (filed && (form.ReverseChargeOnPortal ?? 0) != form.ReverseChargePaid)
            throw new InvalidOperationException("Reconcile reverse-charge VAT paid with the TPOS amount first.");
        decimal?[] amounts = [form.StandardSalesIncludingVat, form.ZeroRatedSales, form.ExemptSales, form.OutputVat, form.InputVat,
            form.OutputAdjustment, form.InputAdjustment, form.CustomsVat, form.ReverseChargePaid, form.ReverseChargeOnPortal, form.NonBusinessVat];
        if (amounts.Any(x => x.HasValue && decimal.Round(x.Value, 2) != x.Value))
            throw new ArgumentException("Enter amounts to the nearest cent.");
        if (filed && !form.PaymentsBasis && string.IsNullOrWhiteSpace(form.Notes) &&
            (form.StandardSalesIncludingVat != reviewedLocal.StandardSalesIncludingVat || form.ZeroRatedSales != reviewedLocal.ZeroRatedSales ||
             form.ExemptSales != reviewedLocal.ExemptSales || form.InputVat != reviewedLocal.InputVat || form.OutputVat != reviewedLocal.OutputVat))
            throw new InvalidOperationException("Explain the difference between the reviewed return and local figures in the notes.");
        if (form.Notes.Length > 2000 || form.FilingReference.Length > 160 || form.ApprovalReference.Length > 160 || form.PaymentEvidence.Length > 500)
            throw new ArgumentException("Shorten the filing reference or notes.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var key = Key(from, to);
        var last = await db.AuditEvents.AsNoTracking().Where(x => x.OrganisationId == org && x.EntityId == key && x.EventType == Event)
            .MaxAsync(x => (long?)x.Id, ct) ?? 0L;
        if (last != expectedRevision) throw new InvalidOperationException("Another revision was saved. Reload the period before saving.");
        var report = await workpapers.GetAsync(org, from, to, ct);
        if (report.FilingFigures != reviewedLocal) throw new InvalidOperationException("The local VAT figures changed. Refresh and review the period again.");
        var hasXero = report.Reconciliation!.HasXeroEvidence || await db.XeroConnections.AnyAsync(x => x.OrganisationId == org && x.IsActive && x.XeroTenantId != null, ct);
        var schedule = storage.Stage(org, user, FijiVatScheduleLine.Csv(report.LocalSchedule, company.BaseCurrency));
        var snapshot = new FijiVatFilingSnapshot(from, to, company.BaseCurrency, reviewedLocal, form, filed, hasXero, report.Reconciliation.Differences.Count, schedule.Id);
        db.AuditEvents.Add(new AuditEvent { OrganisationId = org, EventType = Event, EntityType = "FijiVatFiling", EntityId = key,
            UserId = user, JsonData = JsonSerializer.Serialize(snapshot) });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
