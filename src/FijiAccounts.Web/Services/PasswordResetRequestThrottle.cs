using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace FijiAccounts.Web.Services;

public sealed class PasswordResetRequestThrottle(IMemoryCache cache)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private const int MaximumRequestsPerWindow = 3;
    private readonly object gate = new();

    public bool TryAcquire(string email)
    {
        var key = "password-reset:" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant())));
        var now = DateTimeOffset.UtcNow;

        lock (gate)
        {
            if (!cache.TryGetValue(key, out RequestWindow? current) ||
                current is null ||
                now - current.StartedAt >= Window)
            {
                Store(key, new RequestWindow(now, 1));
                return true;
            }

            if (current.Count >= MaximumRequestsPerWindow)
            {
                return false;
            }

            Store(key, current with { Count = current.Count + 1 });
            return true;
        }
    }

    private void Store(string key, RequestWindow value) =>
        cache.Set(
            key,
            value,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = Window,
                Size = 1
            });

    private sealed record RequestWindow(DateTimeOffset StartedAt, int Count);
}
