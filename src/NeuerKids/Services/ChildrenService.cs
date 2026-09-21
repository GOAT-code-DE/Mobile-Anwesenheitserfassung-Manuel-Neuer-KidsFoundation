using System.Data;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NeuerKids.Domain;

namespace NeuerKids.Services;

public record ChildInput(int SiteId, string FirstName, string LastName, DateOnly BirthDate, Gender? Gender,
    string[] Nationalities, string? ContactName, string? ContactPhone, string? ContactRelationship,
    Guid? Revision, bool ConfirmDuplicate = false);

public class ChildrenService(AppDbContext db, Access access, BerlinClock clock, IConfiguration config)
{
    public async Task<object> List(ClaimsPrincipal user, int siteId, string? search, bool inactive)
    {
        var membership = await access.Site(user, siteId);
        var children = await db.Children.AsNoTracking().Include(x => x.Nationalities).Include(x => x.Attendances)
            .Where(x => x.SiteId == siteId).ToListAsync();
        return children.Where(c => inactive || !BerlinClock.IsInactive(c.Attendances.Select(a => a.Day).DefaultIfEmpty(c.CreatedOn).Max(), clock.Today))
            .Where(c => string.IsNullOrWhiteSpace(search) || $"{c.FirstName} {c.LastName}".Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName).Select(c => View(c, membership.UserId, membership.Role)).ToArray();
    }
    private object View(Child c, string actor, SiteRole role)
    {
        var last = c.Attendances.Select(a => a.Day).DefaultIfEmpty(c.CreatedOn).Max();
        var today = c.Attendances.SingleOrDefault(a => a.Day == clock.Today);
        return new { c.Id, c.SiteId, c.FirstName, c.LastName, c.BirthDate, c.Gender, c.Revision,
            Nationalities = c.Nationalities.Select(n => n.Code), c.ContactName, c.ContactPhone, c.ContactRelationship,
            Age = BerlinClock.Age(c.BirthDate, clock.Today), LastVisit = c.Attendances.Count == 0 ? (DateOnly?)null : last,
            Inactive = BerlinClock.IsInactive(last, clock.Today), Present = today is not null,
            CanUndo = today is not null && (today.CreatedBy == actor || role == SiteRole.Manager),
            AttendanceId = today?.Id };
    }
    public async Task<object> Save(ClaimsPrincipal user, Guid? id, ChildInput input)
    {
        var member = await access.Site(user, input.SiteId);
        Validate(input);
        var first = input.FirstName.Trim(); var last = input.LastName.Trim();
        var duplicates = await db.Children.Where(x => x.SiteId == input.SiteId && x.Id != id && x.BirthDate == input.BirthDate).ToListAsync();
        if (!input.ConfirmDuplicate && duplicates.Any(x => string.Equals(x.FirstName, first, StringComparison.OrdinalIgnoreCase) && string.Equals(x.LastName, last, StringComparison.OrdinalIgnoreCase)))
            throw new AppError(409, "Ein Kind mit diesem Namen und Geburtsdatum existiert bereits. Bitte prüfen und gegebenenfalls als anderes Kind bestätigen.");
        Child child;
        if (id is not null)
        {
            child = await db.Children.Include(x => x.Nationalities).SingleOrDefaultAsync(x => x.Id == id && x.SiteId == input.SiteId)
                ?? throw new AppError(404, "Kind nicht gefunden.");
            if (child.Revision != input.Revision) throw new AppError(409, "Die Daten wurden inzwischen geändert. Bitte neu laden.");
            db.Nationalities.RemoveRange(child.Nationalities.Where(n => !input.Nationalities.Contains(n.Code)));
        }
        else { child = new Child { SiteId = input.SiteId, CreatedOn = clock.Today }; db.Children.Add(child); }
        child.FirstName = first; child.LastName = last; child.BirthDate = input.BirthDate; child.Gender = input.Gender!.Value;
        child.ContactName = Clean(input.ContactName); child.ContactPhone = Clean(input.ContactPhone); child.ContactRelationship = Clean(input.ContactRelationship);
        child.Revision = Guid.NewGuid();
        foreach (var code in input.Nationalities.Distinct())
            if (!child.Nationalities.Any(n => n.Code == code)) child.Nationalities.Add(new Nationality { Code = code });
        Audit(member.UserId, input.SiteId, id is null ? "child.created" : "child.updated", child.Id);
        await db.SaveChangesAsync();
        return new { child.Id, child.Revision };
    }
    public async Task<object> Attend(ClaimsPrincipal user, Guid id, DateOnly day)
    {
        var child = await db.Children.SingleOrDefaultAsync(x => x.Id == id) ?? throw new AppError(404, "Kind nicht gefunden.");
        var member = await access.Site(user, child.SiteId, day != clock.Today);
        if (day > clock.Today || day < child.BirthDate) throw new AppError(400, "Dieses Anwesenheitsdatum ist nicht gültig.");
        var existing = await db.Attendances.SingleOrDefaultAsync(x => x.ChildId == id && x.Day == day);
        if (existing is not null) return new { existing.Id, AlreadyPresent = true };
        var attendance = new Attendance { ChildId = id, SiteId = child.SiteId, Day = day, CreatedAtUtc = clock.UtcNow, CreatedBy = member.UserId };
        db.Attendances.Add(attendance); child.Revision = Guid.NewGuid();
        Audit(member.UserId, child.SiteId, "attendance.created", id);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            existing = await db.Attendances.SingleOrDefaultAsync(x => x.ChildId == id && x.Day == day);
            if (existing is null) throw;
            return new { existing.Id, AlreadyPresent = true };
        }
        return new { attendance.Id, AlreadyPresent = false };
    }
    public async Task Undo(ClaimsPrincipal user, Guid id)
    {
        var entry = await db.Attendances.SingleOrDefaultAsync(x => x.Id == id) ?? throw new AppError(404, "Anwesenheit nicht gefunden.");
        var membership = await access.Site(user, entry.SiteId);
        if (membership.Role != SiteRole.Manager && (entry.Day != clock.Today || entry.CreatedBy != membership.UserId))
            throw new AppError(403, "Nur eigene heutige Einträge können zurückgenommen werden.");
        db.Attendances.Remove(entry); Audit(membership.UserId, entry.SiteId, "attendance.removed", entry.ChildId);
        await db.SaveChangesAsync();
    }
    public async Task<object> History(ClaimsPrincipal user, Guid id)
    {
        var child = await db.Children.SingleOrDefaultAsync(x => x.Id == id) ?? throw new AppError(404, "Kind nicht gefunden.");
        await access.Site(user, child.SiteId, true);
        return await db.Attendances.Where(x => x.ChildId == id).OrderByDescending(x => x.Day).Select(x => new { x.Id, x.Day }).ToArrayAsync();
    }
    public async Task Delete(ClaimsPrincipal user, Guid id, bool preserve)
    {
        var child = await db.Children.SingleOrDefaultAsync(x => x.Id == id) ?? throw new AppError(404, "Kind nicht gefunden.");
        var member = await access.Site(user, child.SiteId, true);
        if (preserve && !config.GetValue<bool>("Demo:Enabled") && !config.GetValue<bool>("Privacy:AnonymizationApproved"))
            throw new AppError(409, "Die Anonymisierungsregeln müssen vor dieser Aktion freigegeben werden.");
        await Purge(child, member.UserId, preserve);
    }
    public async Task Purge(Child child, string actor, bool preserve)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var entries = await db.Attendances.Where(a => a.ChildId == child.Id).ToListAsync();
        if (preserve)
        {
            foreach (var group in entries.GroupBy(a => new DateOnly(a.Day.Year, a.Day.Month, 1)))
            {
                var aggregate = await db.MonthlyArchives.FindAsync(child.SiteId, group.Key);
                if (aggregate is null) { aggregate = new MonthlyArchive { SiteId = child.SiteId, Month = group.Key }; db.MonthlyArchives.Add(aggregate); }
                aggregate.Visits += group.Count(); aggregate.Profiles++;
            }
        }
        db.AuditEntries.RemoveRange(await db.AuditEntries.Where(x => x.ChildId == child.Id).ToListAsync());
        db.Children.Remove(child);
        // Do not retain a profile identifier or its deleted values in the deletion log.
        Audit(actor, child.SiteId, preserve ? "profile.purged.statistics-retained" : "profile.invalid.removed", null);
        await db.SaveChangesAsync(); await transaction.CommitAsync();
    }
    private void Validate(ChildInput input)
    {
        if (string.IsNullOrWhiteSpace(input.FirstName) || input.FirstName.Trim().Length > 80 || string.IsNullOrWhiteSpace(input.LastName) || input.LastName.Trim().Length > 80)
            throw new AppError(400, "Vor- und Nachname sind Pflicht (je maximal 80 Zeichen).");
        if (input.BirthDate > clock.Today || input.BirthDate < clock.Today.AddYears(-100)) throw new AppError(400, "Bitte ein gültiges Geburtsdatum eingeben.");
        if (input.Gender is null || !Enum.IsDefined(input.Gender.Value)) throw new AppError(400, "Bitte ein gültiges Geschlecht auswählen.");
        if (input.Nationalities is null || input.Nationalities.Length == 0 || input.Nationalities.Length > 10 || input.Nationalities.Any(n => !CountryCatalog.All.ContainsKey(n)))
            throw new AppError(400, "Bitte mindestens eine gültige Staatsangehörigkeit auswählen.");
        var anyContact = new[] { input.ContactName, input.ContactPhone, input.ContactRelationship }.Any(s => !string.IsNullOrWhiteSpace(s));
        if (anyContact && (string.IsNullOrWhiteSpace(input.ContactName) || string.IsNullOrWhiteSpace(input.ContactPhone) || string.IsNullOrWhiteSpace(input.ContactRelationship)))
            throw new AppError(400, "Bitte den Notfallkontakt vollständig ausfüllen oder alle drei Felder leer lassen.");
        if (input.ContactName?.Length > 100 || input.ContactRelationship?.Length > 80 || input.ContactPhone?.Length > 40 ||
            !string.IsNullOrWhiteSpace(input.ContactPhone) && !Regex.IsMatch(input.ContactPhone, @"^[+0-9 ()/.-]{5,40}$"))
            throw new AppError(400, "Bitte die Angaben zum Notfallkontakt prüfen.");
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    public void Audit(string actor, int? site, string action, Guid? child) => db.AuditEntries.Add(new AuditEntry { ActorId = actor, SiteId = site, Action = action, ChildId = child, AtUtc = clock.UtcNow });
}
