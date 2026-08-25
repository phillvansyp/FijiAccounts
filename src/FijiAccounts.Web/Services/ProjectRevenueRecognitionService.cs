using FijiAccounts.Domain.Accounting;
using FijiAccounts.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Services;

public sealed record ProjectRevenueRecognition(
    Guid ProjectId,
    DateOnly AsAt,
    ProjectRevenueRecognitionMethod Method,
    decimal RevisedContractValue,
    decimal ActualCost,
    decimal ForecastCost,
    decimal? CompletionPercent,
    decimal? RecognizedRevenue,
    decimal PostedRevenue,
    decimal? RevenueAdjustment,
    decimal ContractAsset,
    decimal ContractLiability,
    decimal? ExpectedGrossProfitToDate,
    decimal ForecastMargin,
    decimal? RemainingRevenue,
    decimal CertifiedWorkValue,
    decimal OutstandingRetention);

public sealed class ProjectRevenueRecognitionService(
    ApplicationDbContext db,
    ProjectService projects)
{
    public async Task<IReadOnlyList<ProjectRevenueRecognition>> GetAsync(
        string userId,
        Guid organisationId,
        DateOnly asAt,
        CancellationToken cancellationToken = default)
    {
        var accessibleProjects = await projects.ListAsync(
            userId, organisationId, cancellationToken);
        if (accessibleProjects.Count == 0)
        {
            return [];
        }

        var projectIds = accessibleProjects.Select(x => x.Id).ToArray();
        var actuals = await db.PostedJournalLines.AsNoTracking()
            .Where(x =>
                x.ProjectId != null &&
                projectIds.Contains(x.ProjectId.Value) &&
                x.PostedJournal.EntryDate <= asAt &&
                (x.LedgerAccount.Type == AccountType.Revenue ||
                 x.LedgerAccount.Type == AccountType.Expense))
            .GroupBy(x => new { ProjectId = x.ProjectId!.Value, x.LedgerAccount.Type })
            .Select(x => new ActualAmount(
                x.Key.ProjectId,
                x.Key.Type,
                x.Key.Type == AccountType.Revenue
                    ? x.Sum(line => line.Credit - line.Debit)
                    : x.Sum(line => line.Debit - line.Credit)))
            .ToListAsync(cancellationToken);

        var organisation = await db.Organisations.AsNoTracking()
            .SingleAsync(x => x.Id == organisationId, cancellationToken);
        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(organisation.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            timeZone = TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            timeZone = TimeZoneInfo.Utc;
        }

        return accessibleProjects.Select(project =>
        {
            var projectActuals = actuals.Where(x => x.ProjectId == project.Id).ToList();
            var actualCost = projectActuals
                .Where(x => x.Type == AccountType.Expense).Sum(x => x.Amount);
            var postedRevenue = projectActuals
                .Where(x => x.Type == AccountType.Revenue).Sum(x => x.Amount);
            var approvedVariations = project.Variations.Where(x =>
                x.Status == ProjectVariationStatus.Approved &&
                x.DecidedAt is DateTimeOffset decidedAt &&
                LocalDate(decidedAt, timeZone) <= asAt).Sum(x => x.Amount);
            var revisedContract = project.OriginalContractValue +
                project.OpeningApprovedVariationValue + approvedVariations;
            var certifiedClaims = project.ProgressClaims.Where(x =>
                x.Status is ProjectProgressClaimStatus.Approved or
                    ProjectProgressClaimStatus.Invoiced &&
                x.DecidedAt is DateTimeOffset decidedAt &&
                LocalDate(decidedAt, timeZone) <= asAt).ToList();
            var certifiedWork = certifiedClaims.Sum(x => x.WorkCompletedAmount);
            var outstandingRetention = certifiedClaims.Sum(x =>
                x.RetentionHeldAmount - x.RetentionReleasedAmount);
            var completionPercent = project.RevenueRecognitionMethod switch
            {
                ProjectRevenueRecognitionMethod.CostToCost when project.ForecastCost > 0 =>
                    ClampPercent(actualCost / project.ForecastCost * 100m),
                ProjectRevenueRecognitionMethod.CertifiedClaims when revisedContract > 0 =>
                    ClampPercent(certifiedWork / revisedContract * 100m),
                _ => (decimal?)null
            };
            var recognizedRevenue = completionPercent is decimal percent
                ? Currency(revisedContract * percent / 100m)
                : (decimal?)null;
            var revenueAdjustment = recognizedRevenue is decimal recognized
                ? Currency(recognized - postedRevenue)
                : (decimal?)null;

            return new ProjectRevenueRecognition(
                project.Id,
                asAt,
                project.RevenueRecognitionMethod,
                Currency(revisedContract),
                Currency(actualCost),
                Currency(project.ForecastCost),
                completionPercent,
                recognizedRevenue,
                Currency(postedRevenue),
                revenueAdjustment,
                Math.Max(0m, revenueAdjustment ?? 0m),
                Math.Max(0m, -(revenueAdjustment ?? 0m)),
                recognizedRevenue is decimal earned ? Currency(earned - actualCost) : null,
                Currency(revisedContract - project.ForecastCost),
                recognizedRevenue is decimal earnedRevenue
                    ? Currency(revisedContract - earnedRevenue)
                    : null,
                Currency(certifiedWork),
                Currency(outstandingRetention));
        }).ToList();
    }

    private static DateOnly LocalDate(DateTimeOffset value, TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, timeZone).DateTime);

    private static decimal ClampPercent(decimal value) =>
        Math.Round(Math.Clamp(value, 0m, 100m), 4, MidpointRounding.AwayFromZero);

    private static decimal Currency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record ActualAmount(Guid ProjectId, AccountType Type, decimal Amount);
}
