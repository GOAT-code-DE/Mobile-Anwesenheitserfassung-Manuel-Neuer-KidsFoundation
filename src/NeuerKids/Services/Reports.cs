using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using NeuerKids.Domain;

namespace NeuerKids.Services;

public record ReportFilter(DateOnly Start, DateOnly End, int[]? SiteIds = null, int? MinAge = null, int? MaxAge = null,
    Gender[]? Genders = null, string[]? Nationalities = null, int? Weekday = null, string Grouping = "day",
    bool Compare = true, DateOnly? CompareStart = null, DateOnly? CompareEnd = null, string Preset = "custom");
public record Bucket(string Key, string Label, int Value);
public record Metrics(int? Visits, int? Children, double? Average, int CalendarDays);
public record ArchiveBucket(int SiteId, string Site, DateOnly Month, int Visits, int Profiles);
public record ReportSlice(DateOnly Start, DateOnly End, Metrics Metrics, bool HasArchive, string? Notice,
    Bucket[] Timeline, Bucket[] Weekdays, Bucket[] Ages, Bucket[] Genders, Bucket[] Nationalities, Bucket[] Sites, ArchiveBucket[] Archive);
public record DashboardReport(ReportFilter Filter, string[] FilterLabels, bool CrossSite, ReportSlice Current, ReportSlice? Comparison);

public class Reports(AppDbContext db, Access access, BerlinClock clock)
{
    public async Task<DashboardReport> Build(ClaimsPrincipal user, ReportFilter filter)
    {
        Validate(filter);
        var sites = await access.Sites(user, filter.SiteIds ?? [], manager: true);
        filter = filter with { SiteIds = sites };
        var current = await Slice(filter, filter.Start, filter.End);
        ReportSlice? comparison = null;
        if (filter.Compare)
        {
            var length = filter.End.DayNumber - filter.Start.DayNumber + 1;
            var isCurrentWeek = filter.Preset == "week" && filter.Start == BerlinClock.Monday(clock.Today) && filter.End == clock.Today;
            var start = filter.CompareStart ?? filter.Start.AddDays(isCurrentWeek ? -7 : -length);
            var end = filter.CompareEnd ?? filter.End.AddDays(isCurrentWeek ? -7 : -length);
            if ((filter.CompareStart is null) != (filter.CompareEnd is null)) throw new AppError(400, "Bitte beide Vergleichsdaten angeben.");
            if (start > end || end > clock.Today || end.DayNumber - start.DayNumber > 3660) throw new AppError(400, "Der Vergleichszeitraum ist ungültig.");
            comparison = await Slice(filter, start, end);
        }
        var names = await db.Sites.Where(s => sites.Contains(s.Id)).Select(s => s.Name).ToArrayAsync();
        var labels = new List<string> { $"{filter.Start:dd.MM.yyyy} – {filter.End:dd.MM.yyyy}", string.Join(", ", names) };
        if (filter.MinAge is not null || filter.MaxAge is not null) labels.Add($"Alter: {filter.MinAge ?? 0}–{filter.MaxAge ?? 100} Jahre am Besuchstag");
        if (filter.Genders?.Length > 0) labels.Add("Geschlecht: " + string.Join(", ", filter.Genders.Select(GenderLabel)));
        if (filter.Nationalities?.Length > 0) labels.Add("Staatsangehörigkeit: " + string.Join(", ", filter.Nationalities.Select(c => CountryCatalog.All[c])));
        if (filter.Weekday is not null) labels.Add("Wochentag: " + BerlinClock.German.DateTimeFormat.GetDayName((DayOfWeek)filter.Weekday));
        if (comparison is not null) labels.Add($"Vergleich: {comparison.Start:dd.MM.yyyy} – {comparison.End:dd.MM.yyyy}");
        return new DashboardReport(filter, labels.ToArray(), sites.Length > 1, current, comparison);
    }
    private async Task<ReportSlice> Slice(ReportFilter f, DateOnly start, DateOnly end)
    {
        var raw = await db.Attendances.AsNoTracking().Include(x => x.Child).ThenInclude(c => c.Nationalities)
            .Where(x => f.SiteIds!.Contains(x.SiteId) && x.Day >= start && x.Day <= end).ToListAsync();
        var entries = raw.Where(a => Matches(a, f)).ToArray();
        var siteNames = await db.Sites.ToDictionaryAsync(s => s.Id, s => s.Name);
        var firstMonth = new DateOnly(start.Year, start.Month, 1);
        var lastMonth = new DateOnly(end.Year, end.Month, 1);
        var archive = await db.MonthlyArchives.AsNoTracking().Where(a => f.SiteIds!.Contains(a.SiteId) && a.Month >= firstMonth && a.Month <= lastMonth).OrderBy(a => a.Month).ToListAsync();
        var hasArchive = archive.Count > 0;
        var hasDetailedFilter = f.MinAge is not null || f.MaxAge is not null || f.Weekday is not null || f.Genders?.Length > 0 || f.Nationalities?.Length > 0;
        var fullArchivedMonths = archive.All(a => start <= a.Month && end >= a.Month.AddMonths(1).AddDays(-1));
        var visitsComplete = !hasArchive || !hasDetailedFilter && fullArchivedMonths;
        var singleFullMonth = start == firstMonth && end == firstMonth.AddMonths(1).AddDays(-1);
        var childrenComplete = !hasArchive || !hasDetailedFilter && singleFullMonth;
        var days = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).Select(start.AddDays).ToArray();
        var denominator = days.Count(d => f.Weekday is null || (int)d.DayOfWeek == f.Weekday);
        int? visits = visitsComplete ? entries.Length + archive.Sum(a => a.Visits) : null;
        int? children = childrenComplete ? entries.Select(a => a.ChildId).Distinct().Count() + archive.Sum(a => a.Profiles) : null;
        var metrics = new Metrics(visits, children, visits is not null && denominator > 0 ? Math.Round((double)visits / denominator, 2) : null, denominator);
        string TimeKey(DateOnly d) => f.Grouping switch { "week" => BerlinClock.Monday(d).ToString("yyyy-MM-dd"), "month" => d.ToString("yyyy-MM-01"), _ => d.ToString("yyyy-MM-dd") };
        var timeline = days.GroupBy(TimeKey).Select(g => new Bucket(g.Key,
            f.Grouping == "month" ? g.First().ToString("MMM yy", BerlinClock.German) : DateOnly.Parse(g.Key).ToString("dd.MM.", BerlinClock.German),
            entries.Count(a => TimeKey(a.Day) == g.Key))).ToArray();
        var weekdays = new[] { 1, 2, 3, 4, 5, 6, 0 }.Select(d => new Bucket(d.ToString(), BerlinClock.German.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)d), entries.Count(a => (int)a.Day.DayOfWeek == d))).ToArray();
        var ages = new[] { (0,5), (6,9), (10,14), (15,17), (18,100) }.Select(r => new Bucket($"{r.Item1}:{r.Item2}", r.Item2 == 100 ? "18+ Jahre" : $"{r.Item1}–{r.Item2} Jahre", entries.Count(a => BerlinClock.Age(a.Child.BirthDate, a.Day) >= r.Item1 && BerlinClock.Age(a.Child.BirthDate, a.Day) <= r.Item2))).ToArray();
        var genders = Enum.GetValues<Gender>().Select(g => new Bucket(g.ToString(), GenderLabel(g), entries.Count(a => a.Child.Gender == g))).ToArray();
        var countries = entries.SelectMany(a => a.Child.Nationalities.Select(n => n.Code)).Distinct().Select(code => new Bucket(code, CountryCatalog.All.GetValueOrDefault(code, code), entries.Count(a => a.Child.Nationalities.Any(n => n.Code == code)))).OrderByDescending(b => b.Value).ThenBy(b => b.Label).ToArray();
        var sites = f.SiteIds!.Select(id => new Bucket(id.ToString(), siteNames[id], entries.Count(a => a.SiteId == id))).ToArray();
        return new ReportSlice(start, end, metrics, hasArchive,
            hasArchive ? "Nach Profillöschungen liegen zusätzlich anonyme Monatssummen vor. Diagramme zeigen nur erhaltene Einzelanwesenheiten. Nicht vollständig berechenbare Kennzahlen werden nicht ausgewiesen." : null,
            timeline, weekdays, ages, genders, countries, sites,
            archive.Select(a => new ArchiveBucket(a.SiteId, siteNames[a.SiteId], a.Month, a.Visits, a.Profiles)).ToArray());
    }
    private static bool Matches(Attendance a, ReportFilter f)
    {
        var age = BerlinClock.Age(a.Child.BirthDate, a.Day);
        return (f.MinAge is null || age >= f.MinAge) && (f.MaxAge is null || age <= f.MaxAge)
            && (f.Genders is null || f.Genders.Length == 0 || f.Genders.Contains(a.Child.Gender))
            && (f.Nationalities is null || f.Nationalities.Length == 0 || a.Child.Nationalities.Any(n => f.Nationalities.Contains(n.Code)))
            && (f.Weekday is null || (int)a.Day.DayOfWeek == f.Weekday);
    }
    private void Validate(ReportFilter f)
    {
        if (f.Start > f.End || f.Start.Year < 1900 || f.End > clock.Today || f.End.DayNumber - f.Start.DayNumber > 3660) throw new AppError(400, "Bitte einen gültigen Zeitraum bis heute auswählen (höchstens zehn Jahre).");
        if (f.MinAge is < 0 or > 100 || f.MaxAge is < 0 or > 100 || f.MinAge > f.MaxAge || f.Weekday is < 0 or > 6) throw new AppError(400, "Bitte Altersbereich und Wochentag prüfen.");
        if (f.Grouping is not ("day" or "week" or "month") || f.Genders?.Any(g => !Enum.IsDefined(g)) == true || f.Nationalities?.Any(c => !CountryCatalog.All.ContainsKey(c)) == true) throw new AppError(400, "Ungültiger Filter.");
    }
    public static string GenderLabel(Gender g) => g switch { Gender.Female => "Weiblich", Gender.Male => "Männlich", Gender.Diverse => "Divers", _ => "Keine Angabe" };
}
