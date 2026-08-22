using Microsoft.EntityFrameworkCore;
using FijiAccounts.Web.Data;

namespace FijiAccounts.Web.Services;

public sealed record CashflowPeriodSummary(
    decimal ExpectedReceipts,
    decimal ExpectedPayments)
{
    public decimal NetMovement =>
        ExpectedReceipts - ExpectedPayments;
}


public sealed record CashflowForecast(
    CashflowPeriodSummary Next7Days,
    CashflowPeriodSummary Next30Days);


public sealed class CashflowForecastService(
    ApplicationDbContext db)
{
    public async Task<CashflowForecast> GetAsync(
        Guid organisationId,
        CancellationToken ct = default)
    {
        var today =
            DateOnly.FromDateTime(
                DateTime.Today);

        var sevenDays =
            today.AddDays(7);

        var thirtyDays =
            today.AddDays(30);


        var invoices =
            await db.SalesInvoices
                .AsNoTracking()
                .Where(
                    x =>
                        x.OrganisationId == organisationId &&
                        x.DueDate > today &&
                        x.DueDate <= thirtyDays &&
                        x.AmountPaid + x.AmountCredited < x.Total &&
                        x.Status != InvoiceStatus.Voided)
                .ToListAsync(ct);


        var bills =
            await db.SupplierBills
                .AsNoTracking()
                .Where(
                    x =>
                        x.OrganisationId == organisationId &&
                        x.DueDate > today &&
                        x.DueDate <= thirtyDays &&
                        x.AmountPaid + x.AmountCredited < x.Total &&
                        x.Status != BillStatus.Voided)
                .ToListAsync(ct);


        return new CashflowForecast(
            CreateSummary(
                invoices,
                bills,
                sevenDays),
            CreateSummary(
                invoices,
                bills,
                thirtyDays));
    }


    private static CashflowPeriodSummary CreateSummary(
        IEnumerable<SalesInvoice> invoices,
        IEnumerable<SupplierBill> bills,
        DateOnly limit)
    {
        var receipts =
            invoices
                .Where(x => x.DueDate <= limit)
                .Sum(
                    x =>
                        x.Total -
                        x.AmountPaid -
                        x.AmountCredited);


        var payments =
            bills
                .Where(x => x.DueDate <= limit)
                .Sum(
                    x =>
                        x.Total -
                        x.AmountPaid -
                        x.AmountCredited);


        return new(
            receipts,
            payments);
    }
}
