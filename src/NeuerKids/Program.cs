using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using NeuerKids.Domain;
using NeuerKids.Services;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("de-DE");
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
var builder = WebApplication.CreateBuilder(args);
var demo = builder.Configuration.GetValue<bool>("Demo:Enabled");
var sqlite = builder.Configuration["Database:Provider"] == "Sqlite";
if (demo && !builder.Environment.IsDevelopment()) throw new InvalidOperationException("Der Demomodus ist ausschließlich in Development zulässig.");
if (!builder.Environment.IsDevelopment() && (sqlite || !builder.Configuration.GetValue<bool>("Privacy:Approved") ||
    !builder.Configuration.GetValue<bool>("Privacy:AnonymizationApproved") || string.IsNullOrWhiteSpace(builder.Configuration["Privacy:LegalBasisReference"]) ||
    builder.Configuration.GetValue<int>("Privacy:InactiveMonths") <= 0 || builder.Configuration.GetValue<int>("Privacy:AuditRetentionDays") <= 0))
    throw new InvalidOperationException("Produktivbetrieb erfordert SQL Server und ein dokumentiertes, freigegebenes Datenschutz- und Löschkonzept.");
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
var keyPath = builder.Configuration["DataProtection:KeyPath"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
Directory.CreateDirectory(keyPath);
builder.Services.AddDataProtection().SetApplicationName("NeuerKids").PersistKeysToFileSystem(new DirectoryInfo(keyPath));
builder.Services.AddDbContext<AppDbContext>(o => { if (sqlite) o.UseSqlite(builder.Configuration.GetConnectionString("Database")); else o.UseSqlServer(builder.Configuration.GetConnectionString("Database")); });
builder.Services.AddIdentity<AppUser, IdentityRole>(o => {
    o.Password.RequiredLength = 12; o.Password.RequireNonAlphanumeric = false;
    o.User.RequireUniqueEmail = true; o.SignIn.RequireConfirmedEmail = true;
    o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(24));
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
builder.Services.ConfigureApplicationCookie(o => {
    o.LoginPath = "/Account"; o.AccessDeniedPath = "/Account"; o.SlidingExpiration = false;
    o.Cookie.Name = "NeuerKids.Session"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.Events.OnRedirectToLogin = c => { if (c.Request.Path.StartsWithSegments("/api")) c.Response.StatusCode = 401; else c.Response.Redirect(c.RedirectUri); return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthentication().AddCookie("MnkChallenge", o => {
    o.Cookie.Name = "NeuerKids.Challenge"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromMinutes(10); o.SlidingExpiration = false;
});
builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddRazorPages(o => { o.Conventions.AuthorizeFolder("/"); o.Conventions.AllowAnonymousToPage("/Account"); o.Conventions.AllowAnonymousToPage("/Error"); });
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<BerlinClock>(); builder.Services.AddScoped<Access>(); builder.Services.AddScoped<ChildrenService>(); builder.Services.AddScoped<Reports>();
builder.Services.AddHostedService<RetentionWorker>();
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = 429;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = context.Request.Path.StartsWithSegments("/Account") ? 30 : 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.Use(async (context, next) => {
    context.Response.Headers.CacheControl = "no-store, private";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    try { await next(); }
    catch (AppError e) { context.Response.StatusCode = e.Status; await context.Response.WriteAsJsonAsync(new { error = e.Message }); }
    catch (BadHttpRequestException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "Die Eingaben sind ungültig. Bitte prüfen und erneut versuchen." }); }
    catch (DbUpdateConcurrencyException) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = "Die Daten wurden inzwischen geändert. Bitte neu laden." }); }
    catch (Exception ex) { app.Logger.LogError("Anfrage fehlgeschlagen ({Type}), Referenz {Trace}", ex.GetType().Name, context.TraceIdentifier); context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new { error = "Die Anfrage konnte nicht abgeschlossen werden. Bitte erneut versuchen." }); }
});
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.UseStaticFiles(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
app.Use(async (context, next) => {
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var users = context.RequestServices.GetRequiredService<UserManager<AppUser>>();
        var user = await users.GetUserAsync(context.User);
        if (user is null || user.IsBlocked || !demo && !user.TwoFactorEnabled) { await context.SignOutAsync(IdentityConstants.ApplicationScheme); context.Response.StatusCode = 401; return; }
    }
    if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "Sitzung abgelaufen. Bitte die Seite neu laden." }); return; }
    }
    await next();
});
app.MapRazorPages();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/session", async (HttpContext ctx, AppDbContext db, Access access, BerlinClock clock) => {
    var user = await access.User(ctx.User);
    return Results.Ok(new { user.DisplayName, user.IsAdmin, Demo = demo, Today = clock.Today, Sites = await db.Memberships.Where(m => m.UserId == user.Id).Select(m => new { m.SiteId, m.Site.Name, m.Role }).ToArrayAsync(), Countries = CountryCatalog.All.OrderBy(x => x.Value).Select(x => new { Code = x.Key, Name = x.Value }) });
});
api.MapGet("/children", (HttpContext ctx, ChildrenService service, int siteId, string? search, bool inactive = false) => service.List(ctx.User, siteId, search, inactive));
api.MapPost("/children", (HttpContext ctx, ChildrenService service, ChildInput input) => service.Save(ctx.User, null, input));
api.MapPut("/children/{id:guid}", (HttpContext ctx, ChildrenService service, Guid id, ChildInput input) => service.Save(ctx.User, id, input));
api.MapPost("/children/{id:guid}/attendance", (HttpContext ctx, ChildrenService service, Guid id, AttendanceInput input) => service.Attend(ctx.User, id, input.Day));
api.MapGet("/children/{id:guid}/attendance", (HttpContext ctx, ChildrenService service, Guid id) => service.History(ctx.User, id));
api.MapDelete("/attendance/{id:guid}", async (HttpContext ctx, ChildrenService service, Guid id) => { await service.Undo(ctx.User, id); return Results.NoContent(); });
api.MapDelete("/children/{id:guid}", async (HttpContext ctx, ChildrenService service, Guid id, bool preserve) => { await service.Delete(ctx.User, id, preserve); return Results.NoContent(); });
api.MapPost("/reports", (HttpContext ctx, Reports service, ReportFilter input) => service.Build(ctx.User, input));
api.MapPost("/reports/export", async (HttpContext ctx, Reports service, ReportFilter input) => Results.File(ExcelExport.Create(await service.Build(ctx.User, input)), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"NeuerKids-Auswertung-{input.Start:yyyy-MM-dd}-{input.End:yyyy-MM-dd}.xlsx"));
api.MapGet("/admin/users", async (HttpContext ctx, Access access, AppDbContext db) => {
    await access.Admin(ctx.User);
    var accounts = await db.Users.OrderBy(u => u.DisplayName).ToListAsync();
    var memberships = await db.Memberships.ToListAsync();
    return Results.Ok(accounts.Select(u => new { u.Id, u.Email, u.DisplayName, u.IsAdmin, u.IsBlocked, u.TwoFactorEnabled, Sites = memberships.Where(m => m.UserId == u.Id).Select(m => new { m.SiteId, m.Role }) }));
});
api.MapPost("/admin/users", async (HttpContext ctx, Access access, AppDbContext db, UserManager<AppUser> users, ChildrenService audit, InviteInput input) => {
    var actor = await access.Admin(ctx.User);
    ValidateMemberships(input.Sites);
    if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 100 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(input.Email)) throw new AppError(400, "Bitte Namen und gültige E-Mail-Adresse angeben.");
    var user = new AppUser { Email = input.Email.Trim(), UserName = input.Email.Trim(), DisplayName = input.Name.Trim(), IsAdmin = input.IsAdmin, EmailConfirmed = false };
    var result = await users.CreateAsync(user);
    if (!result.Succeeded) throw new AppError(400, "Der Zugang konnte nicht angelegt werden. Möglicherweise ist die E-Mail-Adresse bereits vergeben.");
    db.Memberships.AddRange(input.Sites.Select(s => new Membership { SiteId = s.SiteId, Role = s.Role, UserId = user.Id }));
    audit.Audit(actor.Id, null, "account.invited", null); await db.SaveChangesAsync();
    return Results.Ok(new { activationPath = await ActivationPath(users, user) });
});
api.MapPut("/admin/users/{id}", async (HttpContext ctx, Access access, AppDbContext db, UserManager<AppUser> users, ChildrenService audit, string id, PermissionsInput input) => {
    var actor = await access.Admin(ctx.User); ValidateMemberships(input.Sites);
    if (id == actor.Id) throw new AppError(400, "Die eigenen Administrationsrechte können hier nicht verändert werden.");
    var user = await users.FindByIdAsync(id) ?? throw new AppError(404, "Zugang nicht gefunden.");
    await using var transaction = await db.Database.BeginTransactionAsync();
    user.IsBlocked = input.IsBlocked; user.IsAdmin = input.IsAdmin;
    db.Memberships.RemoveRange(await db.Memberships.Where(m => m.UserId == id).ToListAsync()); await db.SaveChangesAsync();
    db.Memberships.AddRange(input.Sites.Select(s => new Membership { SiteId = s.SiteId, Role = s.Role, UserId = id }));
    var result = await users.UpdateSecurityStampAsync(user);
    if (!result.Succeeded) throw new AppError(409, "Zugang konnte nicht aktualisiert werden.");
    audit.Audit(actor.Id, null, "account.permissions.updated", null); await db.SaveChangesAsync(); await transaction.CommitAsync(); return Results.NoContent();
});
api.MapPost("/admin/users/{id}/reset", async (HttpContext ctx, Access access, UserManager<AppUser> users, ChildrenService audit, AppDbContext db, string id, ResetInput input) => {
    var actor = await access.Admin(ctx.User);
    if (id == actor.Id) throw new AppError(400, "Bitte eine andere Administration um die Rücksetzung bitten.");
    var user = await users.FindByIdAsync(id) ?? throw new AppError(404, "Zugang nicht gefunden.");
    await users.UpdateSecurityStampAsync(user);
    if (input.ResetAuthenticator) { await users.SetTwoFactorEnabledAsync(user, false); await users.ResetAuthenticatorKeyAsync(user); }
    audit.Audit(actor.Id, null, input.ResetAuthenticator ? "account.mfa.reset" : "account.password.reset", null); await db.SaveChangesAsync();
    return Results.Ok(new { activationPath = await ActivationPath(users, user) });
});
if (app.Environment.IsDevelopment() && demo)
{
    api.MapGet("/demo-info", () => Results.Ok(new { synthetic = true }));
}
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (sqlite) await db.Database.EnsureCreatedAsync();
    else if (args.Contains("--migrate")) await db.Database.MigrateAsync();
}
await Seed.Run(app.Services, demo);
if (args.Contains("--bootstrap-admin"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    if (await db.Users.AnyAsync(u => u.IsAdmin)) throw new InvalidOperationException("Es existiert bereits eine Administration.");
    var email = builder.Configuration["Bootstrap:Email"] ?? throw new InvalidOperationException("Bootstrap__Email fehlt.");
    var user = new AppUser { UserName = email, Email = email, DisplayName = "Administration", IsAdmin = true };
    var result = await users.CreateAsync(user); if (!result.Succeeded) throw new InvalidOperationException("Administration konnte nicht erstellt werden.");
    Console.WriteLine("Einmaliger Aktivierungspfad (vertraulich, 24 Stunden gültig): " + await ActivationPath(users, user));
    return;
}
app.Run();

static void ValidateMemberships(SiteGrant[] sites)
{
    if (sites is null || sites.Select(s => s.SiteId).Distinct().Count() != sites.Length || sites.Any(s => s.SiteId is not (1 or 2) || !Enum.IsDefined(s.Role))) throw new AppError(400, "Ungültige Standortrechte.");
}
static async Task<string> ActivationPath(UserManager<AppUser> users, AppUser user)
{
    var token = await users.GeneratePasswordResetTokenAsync(user);
    return "/Account?userId=" + Uri.EscapeDataString(user.Id) + "&token=" + WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
}
public record AttendanceInput(DateOnly Day);
public record SiteGrant(int SiteId, SiteRole Role);
public record InviteInput(string Email, string Name, bool IsAdmin, SiteGrant[] Sites);
public record PermissionsInput(bool IsBlocked, bool IsAdmin, SiteGrant[] Sites);
public record ResetInput(bool ResetAuthenticator);
public partial class Program { }
