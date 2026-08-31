using FijiAccounts.Web.Data;
using FijiAccounts.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FijiAccounts.Web.Tests;

public sealed class YearEndReviewServiceTests
{
    [Fact]
    public async Task StartAsync_CreatesCompleteDurableChecklistAndAudit()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var period = await CreatePeriodAsync(test);
        var service = new YearEndReviewService(test.Db, test.Access);

        var review = await service.StartAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id);

        Assert.Equal(Enum.GetValues<YearEndReviewArea>().Length, review.Items.Count);
        Assert.All(review.Items, x => Assert.Equal(YearEndReviewStatus.Pending, x.Status));
        Assert.True(await test.Db.AuditEvents.AnyAsync(x =>
            x.EntityId == period.Id.ToString() &&
            x.EventType == "YearEndReviewStarted"));
    }

    [Fact]
    public async Task UpdateItemAsync_QueryRaisedRequiresNotes()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var period = await CreatePeriodAsync(test);
        var service = new YearEndReviewService(test.Db, test.Access);
        await service.StartAsync(test.UserId, test.Organisation.Id, period.Id);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateItemAsync(
                test.UserId,
                test.Organisation.Id,
                period.Id,
                YearEndReviewArea.AgedReceivables,
                YearEndReviewStatus.QueryRaised,
                "  "));

        Assert.Contains("query", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApproveAsync_RequiresEveryScheduleToBeReviewed()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var period = await CreatePeriodAsync(test);
        var service = new YearEndReviewService(test.Db, test.Access);
        await service.StartAsync(test.UserId, test.Organisation.Id, period.Id);
        await service.UpdateItemAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id,
            YearEndReviewArea.TrialBalance,
            YearEndReviewStatus.Reviewed,
            "Agrees to ledger");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveAsync(
                test.UserId,
                test.Organisation.Id,
                period.Id,
                "PARTNER-YE-2026"));

        Assert.Contains("every", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartedReview_MustBeApprovedBeforeLock_AndReopeningInvalidatesApproval()
    {
        await using var test = await AccountingTestDatabase.CreateAsync();
        var period = await CreatePeriodAsync(test);
        var reviews = new YearEndReviewService(test.Db, test.Access);
        await reviews.StartAsync(test.UserId, test.Organisation.Id, period.Id);
        var periods = new AccountingPeriodService(test.Db, test.Access);

        var incompleteError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            periods.SetLockedAsync(
                test.UserId,
                test.Organisation.Id,
                period.Id,
                true));
        Assert.Contains("year-end review", incompleteError.Message, StringComparison.OrdinalIgnoreCase);

        foreach (var area in Enum.GetValues<YearEndReviewArea>())
        {
            await reviews.UpdateItemAsync(
                test.UserId,
                test.Organisation.Id,
                period.Id,
                area,
                YearEndReviewStatus.Reviewed,
                $"Reviewed {area}");
        }

        await reviews.ApproveAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id,
            "PARTNER-YE-2026");
        await periods.SetLockedAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id,
            true);

        Assert.True((await test.Db.AccountingPeriods.AsNoTracking()
            .SingleAsync(x => x.Id == period.Id)).IsLocked);

        await periods.SetLockedAsync(
            test.UserId,
            test.Organisation.Id,
            period.Id,
            false,
            reopeningReason: "Post approved adjustment AJE-05");

        var reopenedReview = await test.Db.YearEndReviews.AsNoTracking()
            .Include(x => x.Items)
            .SingleAsync(x => x.AccountingPeriodId == period.Id);
        Assert.Null(reopenedReview.ApprovedAt);
        Assert.Null(reopenedReview.ApprovalReference);
        Assert.All(reopenedReview.Items, x => Assert.Equal(YearEndReviewStatus.Pending, x.Status));
        Assert.True(await test.Db.AuditEvents.AnyAsync(x =>
            x.EntityId == period.Id.ToString() &&
            x.EventType == "YearEndReviewApprovalInvalidated"));
    }

    private static async Task<AccountingPeriod> CreatePeriodAsync(
        AccountingTestDatabase test)
    {
        var period = new AccountingPeriod
        {
            OrganisationId = test.Organisation.Id,
            Name = "Year ended 31 July 2026",
            StartsOn = new DateOnly(2025, 8, 1),
            EndsOn = new DateOnly(2026, 7, 31)
        };
        test.Db.AccountingPeriods.Add(period);
        await test.Db.SaveChangesAsync();
        return period;
    }
}
