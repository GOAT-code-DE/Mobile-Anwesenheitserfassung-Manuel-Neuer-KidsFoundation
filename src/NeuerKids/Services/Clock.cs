using System.Globalization;

namespace NeuerKids.Services;

public class BerlinClock(TimeProvider time)
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    public DateTime UtcNow => time.GetUtcNow().UtcDateTime;
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Zone).DateTime);
    public DateTimeOffset NextMidnight => new(TimeZoneInfo.ConvertTimeToUtc(Today.AddDays(1).ToDateTime(TimeOnly.MinValue), Zone));
    public static int Age(DateOnly birth, DateOnly on) => on.Year - birth.Year - (birth.AddYears(on.Year - birth.Year) > on ? 1 : 0);
    public static DateOnly Monday(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
    public static bool IsInactive(DateOnly lastVisitOrCreated, DateOnly today) => lastVisitOrCreated < today.AddMonths(-12);
    public static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
}
