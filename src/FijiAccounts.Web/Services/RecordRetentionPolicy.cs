namespace FijiAccounts.Web.Services;

public static class RecordRetentionPolicy
{
    public const int RequiredYears = 7;

    public static DateOnly RetainUntil(
        DateOnly recordDate,
        int financialYearEndMonth,
        int financialYearEndDay)
    {
        var day = Math.Min(
            financialYearEndDay,
            DateTime.DaysInMonth(recordDate.Year, financialYearEndMonth));
        var periodEnd = new DateOnly(recordDate.Year, financialYearEndMonth, day);

        if (recordDate > periodEnd)
        {
            day = Math.Min(
                financialYearEndDay,
                DateTime.DaysInMonth(recordDate.Year + 1, financialYearEndMonth));
            periodEnd = new DateOnly(recordDate.Year + 1, financialYearEndMonth, day);
        }

        return periodEnd.AddYears(RequiredYears);
    }

    public static bool IsProtected(DateOnly retainUntil, DateOnly? today = null) =>
        (today ?? DateOnly.FromDateTime(DateTime.UtcNow)) <= retainUntil;

    public static string ProtectedMessage(DateOnly retainUntil) =>
        $"This record is protected by Fiji's seven-year record-retention requirement until {retainUntil:dd MMM yyyy}.";
}
