namespace FijiAccounts.Web.Data;

public enum PaymentTermType
{
    DaysAfterDocumentDate = 0,
    DayOfFollowingMonth = 1,
    EndOfCurrentMonth = 2,
    EndOfFollowingMonth = 3
}

public static class PaymentTermCalculator
{
    public static DateOnly CalculateDueDate(
        DateOnly documentDate,
        PaymentTermType type,
        int value)
    {
        return type switch
        {
            PaymentTermType.DaysAfterDocumentDate =>
                documentDate.AddDays(
                    Math.Max(0, value)),

            PaymentTermType.DayOfFollowingMonth =>
                DayOfMonth(
                    documentDate.AddMonths(1),
                    value),

            PaymentTermType.EndOfCurrentMonth =>
                EndOfMonth(documentDate),

            PaymentTermType.EndOfFollowingMonth =>
                EndOfMonth(
                    documentDate.AddMonths(1)),

            _ =>
                documentDate.AddDays(
                    Math.Max(0, value))
        };
    }

    private static DateOnly DayOfMonth(
        DateOnly date,
        int requestedDay)
    {
        var lastDay =
            DateTime.DaysInMonth(
                date.Year,
                date.Month);

        var day =
            Math.Clamp(
                requestedDay,
                1,
                lastDay);

        return new DateOnly(
            date.Year,
            date.Month,
            day);
    }

    private static DateOnly EndOfMonth(
        DateOnly date)
    {
        return new DateOnly(
            date.Year,
            date.Month,
            DateTime.DaysInMonth(
                date.Year,
                date.Month));
    }
}