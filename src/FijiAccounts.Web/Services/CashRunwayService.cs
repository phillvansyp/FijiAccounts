using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record CashRunwayForecast(
    DateOnly AsAt,
    decimal OpeningCash,
    decimal ProjectedBalance30Days,
    decimal ProjectedBalance90Days,
    decimal LowestProjectedBalance,
    DateOnly? FirstShortfallDate)
{
    public int? DaysUntilShortfall =>
        FirstShortfallDate is null
            ? null
            : FirstShortfallDate.Value.DayNumber - AsAt.DayNumber;
}

public sealed class CashRunwayService(
    ApplicationDbContext db)
{
    private const int HorizonDays = 90;

    public async Task<CashRunwayForecast> GetAsync(
        Guid organisationId,
        CancellationToken ct = default)
    {
        var today =
            DateOnly.FromDateTime(DateTime.Today);

        var horizon =
            today.AddDays(HorizonDays);

        var openingCash =
            await db.PostedJournalLines
                .AsNoTracking()
                .Where(
                    x =>
                        x.PostedJournal.OrganisationId == organisationId &&
                        x.LedgerAccount.IsBankAccount)
                .SumAsync(
                    x => (decimal?)(x.Debit - x.Credit),
                    ct) ?? 0;

        var receipts =
            await db.SalesInvoices
                .AsNoTracking()
                .Where(
                    x =>
                        x.OrganisationId == organisationId &&
                        x.DueDate <= horizon &&
                        x.Status != InvoiceStatus.Draft &&
                        x.Status != InvoiceStatus.Voided &&
                        x.AmountPaid + x.AmountCredited < x.Total)
                .Select(
                    x =>
                        new CashRunwayEvent(
                            x.DueDate,
                            x.Total - x.AmountPaid - x.AmountCredited))
                .ToListAsync(ct);

        var payments =
            await db.SupplierBills
                .AsNoTracking()
                .Where(
                    x =>
                        x.OrganisationId == organisationId &&
                        x.DueDate <= horizon &&
                        x.Status != BillStatus.Voided &&
                        x.AmountPaid + x.AmountCredited < x.Total)
                .Select(
                    x =>
                        new CashRunwayEvent(
                            x.DueDate,
                            -(x.Total - x.AmountPaid - x.AmountCredited)))
                .ToListAsync(ct);

        var movements =
            receipts
                .Concat(payments)
                .GroupBy(
                    x => x.Date < today
                        ? today
                        : x.Date)
                .ToDictionary(
                    x => x.Key,
                    x => x.Sum(y => y.Amount));

        var balance = openingCash;
        var lowestBalance = openingCash;
        DateOnly? firstShortfallDate =
            openingCash < 0
                ? today
                : null;
        var projectedBalance30Days = openingCash;

        for (var date = today; date <= horizon; date = date.AddDays(1))
        {
            balance += movements.GetValueOrDefault(date);
            lowestBalance = Math.Min(lowestBalance, balance);

            if (firstShortfallDate is null && balance < 0)
            {
                firstShortfallDate = date;
            }

            if (date == today.AddDays(30))
            {
                projectedBalance30Days = balance;
            }
        }

        return new(
            today,
            openingCash,
            projectedBalance30Days,
            balance,
            lowestBalance,
            firstShortfallDate);
    }

    private sealed record CashRunwayEvent(
        DateOnly Date,
        decimal Amount);
}
