using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

internal static class FiscalAccountingPeriodGuard
{
    internal const string LockedPeriodMessage =
        "The accounting period is locked. Unlock it before submitting or posting this fiscal document.";

    internal static async Task EnsureOpenAsync(
        ApplicationDbContext db,
        Guid organisationId,
        DateOnly documentDate,
        CancellationToken cancellationToken)
    {
        var isLocked =
            await db.AccountingPeriods
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.OrganisationId == organisationId &&
                        x.IsLocked &&
                        documentDate >= x.StartsOn &&
                        documentDate <= x.EndsOn,
                    cancellationToken);

        if (isLocked)
        {
            throw new InvalidOperationException(LockedPeriodMessage);
        }
    }
}
