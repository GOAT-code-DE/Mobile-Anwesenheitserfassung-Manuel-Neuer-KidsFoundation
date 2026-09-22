using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NeuerKids.Domain;

namespace NeuerKids.Services;

public class AppError(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
public class Access(AppDbContext db, UserManager<AppUser> users)
{
    public async Task<AppUser> User(ClaimsPrincipal principal)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null || user.IsBlocked) throw new AppError(401, "Bitte erneut anmelden.");
        return user;
    }
    public async Task<Membership> Site(ClaimsPrincipal principal, int site, bool manager = false)
    {
        var user = await User(principal);
        if (user.IsAdmin)
        {
            if (!await db.Sites.AnyAsync(x => x.Id == site))
                throw new AppError(403, "Dieser Standort ist nicht verfügbar.");
            return new Membership { UserId = user.Id, SiteId = site, Role = SiteRole.Manager };
        }
        var membership = await db.Memberships.SingleOrDefaultAsync(x => x.UserId == user.Id && x.SiteId == site);
        if (membership is null || manager && membership.Role != SiteRole.Manager)
            throw new AppError(403, "Für diesen Standort oder diese Aktion fehlt die Berechtigung.");
        return membership;
    }
    public async Task<int[]> Sites(ClaimsPrincipal principal, int[] requested, bool manager = false)
    {
        var user = await User(principal);
        int[] allowed;
        if (user.IsAdmin)
            allowed = await db.Sites.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        else
        {
            var memberships = db.Memberships.Where(x => x.UserId == user.Id);
            if (manager) memberships = memberships.Where(x => x.Role == SiteRole.Manager);
            allowed = await memberships.OrderBy(x => x.SiteId).Select(x => x.SiteId).ToArrayAsync();
        }
        if (requested.Except(allowed).Any() || allowed.Length == 0) throw new AppError(403, manager ? "Auswertungen stehen der Hausleitung zur Verfügung." : "Kein Zugriff auf diesen Standort.");
        return requested.Length == 0 ? allowed : requested.Distinct().ToArray();
    }
    public async Task<AppUser> Admin(ClaimsPrincipal principal)
    {
        var user = await User(principal);
        if (!user.IsAdmin) throw new AppError(403, "Nur die zentrale Administration darf Zugänge verwalten.");
        return user;
    }
}
