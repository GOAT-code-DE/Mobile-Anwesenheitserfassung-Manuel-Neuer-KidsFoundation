using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using NeuerKids.Domain;
using NeuerKids.Services;
using QRCoder;

namespace NeuerKids.Pages;

[AllowAnonymous]
public class AccountModel(UserManager<AppUser> users, SignInManager<AppUser> signIn, BerlinClock clock, IConfiguration config, IWebHostEnvironment env) : PageModel
{
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string Code { get; set; } = "";
    [BindProperty] public bool UseRecovery { get; set; }
    [BindProperty(SupportsGet = true)] public string? UserId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Token { get; set; }
    public string Mode { get; set; } = "login";
    public string? Error { get; set; }
    public string? Secret { get; set; }
    public string? QrData { get; set; }
    public IEnumerable<string>? RecoveryCodes { get; set; }
    public bool Demo => env.IsDevelopment() && config.GetValue<bool>("Demo:Enabled");
    public async Task<IActionResult> OnGetAsync()
    {
        if (UserId is not null && Token is not null) { Mode = "activate"; return Page(); }
        if (User.Identity?.IsAuthenticated == true) return RedirectToPage("/Index");
        var user = await ChallengeUser();
        if (user is not null) await PrepareChallenge(user);
        return Page();
    }
    public async Task<IActionResult> OnPostLoginAsync()
    {
        var user = await users.FindByEmailAsync(Email.Trim());
        if (user is null || user.IsBlocked || !(await signIn.CheckPasswordSignInAsync(user, Password, true)).Succeeded)
        { Error = "Die Anmeldung war nicht möglich. Bitte Zugangsdaten prüfen oder später erneut versuchen."; return Page(); }
        await StartChallenge(user); return RedirectToPage("/Account");
    }
    public async Task<IActionResult> OnPostActivateAsync()
    {
        Mode = "activate";
        var user = UserId is null ? null : await users.FindByIdAsync(UserId);
        if (user is null || user.IsBlocked || Token is null) { Error = "Dieser Aktivierungslink ist ungültig."; return Page(); }
        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Token)); }
        catch (FormatException) { Error = "Dieser Aktivierungslink ist ungültig."; return Page(); }
        var result = await users.ResetPasswordAsync(user, token, Password);
        if (!result.Succeeded) { Error = "Der Link ist abgelaufen oder das Passwort erfüllt die Anforderungen nicht: mindestens 12 Zeichen, Groß- und Kleinbuchstaben und eine Ziffer."; return Page(); }
        user.EmailConfirmed = true; await users.UpdateAsync(user); await users.ResetAccessFailedCountAsync(user); await users.SetLockoutEndDateAsync(user, null);
        await StartChallenge(user); return RedirectToPage("/Account");
    }
    public async Task<IActionResult> OnPostVerifyAsync()
    {
        var user = await ChallengeUser();
        if (user is null) { Error = "Die Anmeldung ist abgelaufen. Bitte erneut anmelden."; return Page(); }
        var code = Code.Replace(" ", "").Replace("-", "");
        var valid = UseRecovery && user.TwoFactorEnabled
            ? (await users.RedeemTwoFactorRecoveryCodeAsync(user, Code.Trim())).Succeeded
            : await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code);
        if (!valid)
        {
            await users.AccessFailedAsync(user);
            if (await users.IsLockedOutAsync(user)) { await HttpContext.SignOutAsync("MnkChallenge"); Error = "Zu viele Versuche. Bitte in 15 Minuten erneut anmelden."; return Page(); }
            await PrepareChallenge(user); Error = "Der Code ist ungültig. Bitte den aktuellen Code aus der App eingeben."; return Page();
        }
        var firstSetup = !user.TwoFactorEnabled;
        if (firstSetup) { await users.SetTwoFactorEnabledAsync(user, true); RecoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10); }
        await users.ResetAccessFailedCountAsync(user); await HttpContext.SignOutAsync("MnkChallenge");
        await signIn.SignInAsync(user, new AuthenticationProperties { IsPersistent = true, ExpiresUtc = clock.NextMidnight, AllowRefresh = false }, "mfa");
        if (firstSetup) { Mode = "recovery"; return Page(); }
        return RedirectToPage("/Index");
    }
    public async Task<IActionResult> OnPostLogoutAsync() { await signIn.SignOutAsync(); await HttpContext.SignOutAsync("MnkChallenge"); return RedirectToPage("/Account"); }
    public async Task<IActionResult> OnPostDemoAsync(string role)
    {
        if (!Demo) return NotFound();
        var id = role switch { "employee" => "demo-employee", "admin" => "demo-admin", _ => "demo-manager" };
        var user = await users.FindByIdAsync(id);
        if (user is null || user.IsBlocked) return NotFound();
        await signIn.SignInAsync(user, new AuthenticationProperties { IsPersistent = true, ExpiresUtc = clock.NextMidnight, AllowRefresh = false });
        return RedirectToPage(role == "admin" ? "/Admin" : "/Index");
    }
    private async Task<AppUser?> ChallengeUser()
    {
        var auth = await HttpContext.AuthenticateAsync("MnkChallenge");
        var id = auth.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!auth.Succeeded || id is null) return null;
        var user = await users.FindByIdAsync(id);
        if (user is null || user.IsBlocked || await users.IsLockedOutAsync(user) || user.SecurityStamp != auth.Principal?.FindFirstValue("stamp")) return null;
        return user;
    }
    private async Task StartChallenge(AppUser user)
    {
        // ResetAuthenticatorKey changes the security stamp. Do it before issuing the challenge.
        if (!user.TwoFactorEnabled && string.IsNullOrWhiteSpace(await users.GetAuthenticatorKeyAsync(user)))
            await users.ResetAuthenticatorKeyAsync(user);
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim("stamp", user.SecurityStamp ?? "") }, "MnkChallenge");
        await HttpContext.SignInAsync("MnkChallenge", new ClaimsPrincipal(identity), new AuthenticationProperties { ExpiresUtc = new DateTimeOffset(clock.UtcNow).AddMinutes(10), AllowRefresh = false });
    }
    private async Task PrepareChallenge(AppUser user)
    {
        Mode = user.TwoFactorEnabled ? "verify" : "setup";
        if (user.TwoFactorEnabled) return;
        Secret = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(Secret)) throw new InvalidOperationException("Authenticator-Schlüssel fehlt.");
        var uri = $"otpauth://totp/NeuerKids:{Uri.EscapeDataString(user.Email!)}?secret={Secret}&issuer=NeuerKids&digits=6";
        using var data = QRCodeGenerator.GenerateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        using var qr = new PngByteQRCode(data); QrData = "data:image/png;base64," + Convert.ToBase64String(qr.GetGraphic(5));
    }
}
