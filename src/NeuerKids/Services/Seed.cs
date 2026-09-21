using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NeuerKids.Domain;

namespace NeuerKids.Services;

public static class Seed
{
    public static async Task Run(IServiceProvider services, bool demo)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var clock = scope.ServiceProvider.GetRequiredService<BerlinClock>();
        if (!await db.Sites.AnyAsync()) { db.Sites.AddRange(new Site { Id = 1, Name = "Gelsenkirchen" }, new Site { Id = 2, Name = "Bottrop" }); await db.SaveChangesAsync(); }
        if (!demo) return;
        foreach (var (id, name, admin) in new[] { ("demo-manager", "Alex · Hausleitung", false), ("demo-employee", "Sam · Mitarbeitende", false), ("demo-admin", "Zentrale Administration", true) })
        {
            if (await users.FindByIdAsync(id) is not null) continue;
            var account = new AppUser { Id = id, DisplayName = name, Email = $"{id}@example.invalid", UserName = $"{id}@example.invalid", EmailConfirmed = true, IsAdmin = admin };
            var result = await users.CreateAsync(account);
            if (!result.Succeeded) throw new InvalidOperationException("Demozugang konnte nicht erstellt werden.");
            if (!admin) { db.Memberships.Add(new Membership { UserId = id, SiteId = 1, Role = id == "demo-manager" ? SiteRole.Manager : SiteRole.Employee }); if (id == "demo-manager") db.Memberships.Add(new Membership { UserId = id, SiteId = 2, Role = SiteRole.Manager }); }
        }
        await db.SaveChangesAsync();
        if (await db.Children.AnyAsync()) return;
        var firstNames = new[] { "Emma", "Noah", "Mila", "Elias", "Amira", "Leon", "Sofia", "Ben", "Lina", "Finn", "Maya", "Emil", "Elif", "Adam", "Nora", "Yusuf", "Mia", "Theo", "Hanna", "Omar", "Lea", "Paul", "Sara", "Liam" };
        var lastNames = new[] { "Beispiel", "Muster", "Demokind", "Testmann", "Beispielfall", "Musterdaten" };
        var rng = new Random(1904);
        for (var site = 1; site <= 2; site++)
        for (var i = 0; i < firstNames.Length; i++)
        {
            var child = new Child { SiteId = site, FirstName = firstNames[i], LastName = lastNames[i % lastNames.Length], BirthDate = clock.Today.AddYears(-rng.Next(6, 18)).AddDays(-rng.Next(0, 250)), Gender = i % 2 == 0 ? Gender.Female : Gender.Male, CreatedOn = clock.Today.AddMonths(-8) };
            child.Nationalities.Add(new Nationality { Code = new[] { "DE", "DE", "TR", "SY", "UA", "PL" }[i % 6] });
            if (i % 7 == 0) child.Nationalities.Add(new Nationality { Code = "IQ" });
            for (var offset = -95; offset <= 0; offset++)
            {
                var day = clock.Today.AddDays(offset);
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || rng.NextDouble() > (site == 1 ? .58 : .44)) continue;
                child.Attendances.Add(new Attendance { SiteId = site, Day = day, CreatedAtUtc = day.ToDateTime(new TimeOnly(12, 0)), CreatedBy = "demo-manager" });
            }
            db.Children.Add(child);
        }
        var archived = new Child { FirstName = "Robin", LastName = "Archivbeispiel", SiteId = 1, BirthDate = clock.Today.AddYears(-13), CreatedOn = clock.Today.AddMonths(-16), Gender = Gender.Unspecified };
        archived.Nationalities.Add(new Nationality { Code = "DE" }); db.Children.Add(archived);
        await db.SaveChangesAsync();
    }
}
