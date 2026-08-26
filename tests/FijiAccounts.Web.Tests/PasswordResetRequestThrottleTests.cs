using FijiAccounts.Web.Services;
using Microsoft.Extensions.Caching.Memory;

namespace FijiAccounts.Web.Tests;

public sealed class PasswordResetRequestThrottleTests
{
    [Fact]
    public void TryAcquire_LimitsEquivalentEmailAddresses()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var throttle = new PasswordResetRequestThrottle(cache);

        Assert.True(throttle.TryAcquire("person@example.com"));
        Assert.True(throttle.TryAcquire(" PERSON@example.com "));
        Assert.True(throttle.TryAcquire("person@EXAMPLE.com"));
        Assert.False(throttle.TryAcquire("person@example.com"));
        Assert.True(throttle.TryAcquire("another@example.com"));
    }
}
