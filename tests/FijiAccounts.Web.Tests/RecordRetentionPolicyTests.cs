using FijiAccounts.Web.Services;

namespace FijiAccounts.Web.Tests;

public sealed class RecordRetentionPolicyTests
{
    [Theory]
    [InlineData(2026, 3, 31, 12, 31, 2033, 12, 31)]
    [InlineData(2026, 7, 1, 6, 30, 2034, 6, 30)]
    [InlineData(2026, 6, 30, 6, 30, 2033, 6, 30)]
    public void RetainUntil_UsesEndOfContainingFinancialYearPlusSevenYears(
        int year,
        int month,
        int day,
        int yearEndMonth,
        int yearEndDay,
        int expectedYear,
        int expectedMonth,
        int expectedDay)
    {
        var result = RecordRetentionPolicy.RetainUntil(
            new DateOnly(year, month, day),
            yearEndMonth,
            yearEndDay);

        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), result);
    }

    [Fact]
    public void IsProtected_IncludesTheFinalRetentionDay()
    {
        var retainUntil = new DateOnly(2033, 12, 31);

        Assert.True(RecordRetentionPolicy.IsProtected(
            retainUntil,
            new DateOnly(2033, 12, 31)));
        Assert.False(RecordRetentionPolicy.IsProtected(
            retainUntil,
            new DateOnly(2034, 1, 1)));
    }
}
