using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

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

public sealed record VatWorkpaper(
    DateOnly From,
    DateOnly To,
    VatTreatmentSummary Sales,
    VatAdjustmentSummary SalesCredits,
    VatTreatmentSummary Purchases,
    VatAdjustmentSummary SupplierCredits)
{
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
                        x.VatAmount))
                .ToListAsync(ct);

        var salesVoids =
            await db.SalesInvoiceLines
                .AsNoTracking()
                .Where(x =>
                    x.SalesInvoice.OrganisationId == organisationId &&
                    db.SalesInvoiceVoids.Any(v =>
                        v.OrganisationId == organisationId &&
                        v.SalesInvoiceId == x.SalesInvoiceId &&
                        v.VoidDate >= from &&
                        v.VoidDate <= to))
                .Select(x =>
                    new VatLine(
                        x.VatTreatment,
                        -x.NetAmount,
                        -x.VatAmount))
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
                        x.VatAmount))
                .ToListAsync(ct);

        var purchaseVoids =
            await db.SupplierBillLines
                .AsNoTracking()
                .Where(x =>
                    x.SupplierBill.OrganisationId == organisationId &&
                    db.SupplierBillVoids.Any(v =>
                        v.OrganisationId == organisationId &&
                        v.SupplierBillId == x.SupplierBillId &&
                        v.VoidDate >= from &&
                        v.VoidDate <= to))
                .Select(x =>
                    new VatLine(
                        x.VatTreatment,
                        -x.NetAmount,
                        -x.VatAmount))
                .ToListAsync(ct);

        purchases.AddRange(purchaseVoids);

        var salesCredits =
            await db.SalesCreditNotes
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == organisationId &&
                    x.CreditDate >= from &&
                    x.CreditDate <= to)
                .Select(x =>
                    new VatAdjustmentSummary(
                        x.Subtotal,
                        x.VatTotal))
                .ToListAsync(ct);

        var salesCreditReversals =
            await db.SalesCreditNoteReversals
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == organisationId &&
                    x.ReversalDate >= from &&
                    x.ReversalDate <= to)
                .Select(x =>
                    new VatAdjustmentSummary(
                        x.SalesCreditNote.Subtotal,
                        x.SalesCreditNote.VatTotal))
                .ToListAsync(ct);

        var supplierCredits =
            await db.SupplierCreditNotes
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == organisationId &&
                    x.CreditDate >= from &&
                    x.CreditDate <= to)
                .Select(x =>
                    new VatAdjustmentSummary(
                        x.Subtotal,
                        x.VatTotal))
                .ToListAsync(ct);

        var supplierCreditReversals =
            await db.SupplierCreditNoteReversals
                .AsNoTracking()
                .Where(x =>
                    x.OrganisationId == organisationId &&
                    x.ReversalDate >= from &&
                    x.ReversalDate <= to)
                .Select(x =>
                    new VatAdjustmentSummary(
                        x.SupplierCreditNote.Subtotal,
                        x.SupplierCreditNote.VatTotal))
                .ToListAsync(ct);

        return new VatWorkpaper(
            from,
            to,
            Summarise(sales),
            NetAdjustments(
                salesCredits,
                salesCreditReversals),
            Summarise(purchases),
            NetAdjustments(
                supplierCredits,
                supplierCreditReversals));
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
        IEnumerable<VatAdjustmentSummary> credits,
        IEnumerable<VatAdjustmentSummary> reversals) =>
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

    private sealed record VatLine(
        VatTreatment Treatment,
        decimal Net,
        decimal Tax);
}
