namespace FijiAccounts.Web.Components.Account;

using FijiAccounts.Web.Data;

public static class AccountLockoutPolicy
{
    public const int MaxFailedAccessAttempts = 5;
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(1);

    public static int RemainingAttempts(int failedAccessCount) =>
        Math.Max(0, MaxFailedAccessAttempts - failedAccessCount);

    public static int RemainingSeconds(DateTimeOffset? lockoutEnd, DateTimeOffset now) =>
        lockoutEnd is null
            ? (int)Duration.TotalSeconds
            : Math.Clamp((int)Math.Ceiling((lockoutEnd.Value - now).TotalSeconds), 1, (int)Duration.TotalSeconds);

    public static void ClearFailureState(ApplicationUser user)
    {
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
    }
}
