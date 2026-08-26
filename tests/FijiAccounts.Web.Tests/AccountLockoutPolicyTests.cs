using FijiAccounts.Web.Components.Account;

namespace FijiAccounts.Web.Tests;

public sealed class AccountLockoutPolicyTests
{
    [Fact]
    public void PolicyLocksForOneMinuteAfterFiveFailedAttempts()
    {
        Assert.Equal(5, AccountLockoutPolicy.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(1), AccountLockoutPolicy.Duration);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 4)]
    [InlineData(4, 1)]
    [InlineData(5, 0)]
    [InlineData(8, 0)]
    public void RemainingAttemptsNeverDropsBelowZero(int failures, int expected)
    {
        Assert.Equal(expected, AccountLockoutPolicy.RemainingAttempts(failures));
    }

    [Fact]
    public void RemainingSecondsUsesActualLockoutEndAndPolicyBounds()
    {
        var now = new DateTimeOffset(2026, 8, 26, 7, 0, 0, TimeSpan.Zero);

        Assert.Equal(42, AccountLockoutPolicy.RemainingSeconds(now.AddSeconds(41.2), now));
        Assert.Equal(1, AccountLockoutPolicy.RemainingSeconds(now.AddSeconds(-1), now));
        Assert.Equal(60, AccountLockoutPolicy.RemainingSeconds(now.AddMinutes(5), now));
        Assert.Equal(60, AccountLockoutPolicy.RemainingSeconds(null, now));
    }
}
