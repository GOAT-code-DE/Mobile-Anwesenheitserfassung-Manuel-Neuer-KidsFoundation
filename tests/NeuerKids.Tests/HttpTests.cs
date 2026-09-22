using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NeuerKids.Domain;
using NeuerKids.Services;
using Xunit;

namespace NeuerKids.Tests;

public class AppFactory : WebApplicationFactory<Program>
{
    private readonly string path = Path.Combine(Path.GetTempPath(),"mnk-tests-"+Guid.NewGuid());
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(path);
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?> {
            ["Database:Provider"]="Sqlite", ["ConnectionStrings:Database"]=$"Data Source={path}/test.db",
            ["DataProtection:KeyPath"]=$"{path}/keys",["Demo:Enabled"]="true"
        }));
    }
    protected override void Dispose(bool disposing) {base.Dispose(disposing);SqliteCleanup();}
    private void SqliteCleanup() {Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();try{Directory.Delete(path,true);}catch(IOException){} }
}

public class HttpTests : IClassFixture<AppFactory>
{
    private readonly AppFactory factory;
    public HttpTests(AppFactory factory) {this.factory=factory;}
    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect=false });
    private static async Task<string> Token(HttpClient client,string path="/Account")
    {
        var html=await client.GetStringAsync(path);var match=Regex.Match(html,"name=\"csrf-token\" content=\"([^\"]+)\"");Assert.True(match.Success);return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private static Task<HttpResponseMessage> Form(HttpClient client,string handler,string csrf,params (string,string)[] fields)
        => client.PostAsync("/Account?handler="+handler,new FormUrlEncodedContent(fields.Append(("__RequestVerificationToken",csrf)).Select(f=>new KeyValuePair<string,string>(f.Item1,f.Item2))));
    private async Task<HttpClient> Login(string role)
    {
        var client=Client();var csrf=await Token(client);var response=await Form(client,"Demo",csrf,("role",role));Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        csrf=await Token(client,role=="admin"?"/Admin":"/");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",csrf);return client;
    }
    [Fact] public async Task UnauthenticatedApiDeniedAndPostRequiresAntiforgery()
    {
        using var client=Client();Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/session")).StatusCode);
        using var auth=await Login("manager");auth.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        var response=await auth.PostAsJsonAsync("/api/reports",new {start="2026-08-01",end="2026-08-31",siteIds=new[]{1}});Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
    }
    [Fact] public async Task ApiAndExcelDenyUnauthorizedSiteAndAdministration()
    {
        using var client=await Login("employee");
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/api/children?siteId=2")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/Dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync("/api/reports",new {start="2026-08-01",end="2026-08-31",siteIds=new[]{1}})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync("/api/reports/export",new {start="2026-08-01",end="2026-08-31",siteIds=new[]{1}})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync("/api/reports/export",new {start="2026-08-01",end="2026-08-31",siteIds=new[]{2}})).StatusCode);
    }
    [Fact] public async Task AdministrationHasFullRightsAtAllSites()
    {
        using var client=await Login("admin");
        var session=await client.GetFromJsonAsync<JsonElement>("/api/session");
        Assert.Equal(2,session.GetProperty("sites").GetArrayLength());
        Assert.All(session.GetProperty("sites").EnumerateArray(),site=>Assert.Equal("Manager",site.GetProperty("role").GetString()));
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/children?siteId=1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/children?siteId=2")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/Dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/reports",new {start="2026-08-01",end="2026-08-31",siteIds=new[]{1,2},compare=false})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/reports/export",new {start="2026-08-01",end="2026-08-31",siteIds=new[]{1,2},compare=false})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/admin/users")).StatusCode);
    }
    [Fact] public async Task PersonalDataResponsesAreNotCachedAndExportIsXlsx()
    {
        using var client=await Login("manager");var response=await client.GetAsync("/api/children?siteId=1");Assert.True(response.Headers.CacheControl!.NoStore);
        var export=await client.PostAsJsonAsync("/api/reports/export",new {start="2026-08-01",end="2026-08-31",siteIds=new[]{1},compare=false});Assert.Equal(HttpStatusCode.OK,export.StatusCode);Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",export.Content.Headers.ContentType!.MediaType);
    }
    [Fact] public async Task FullAccountActivationRequiresAuthenticatorAndRevocationEndsSession()
    {
        using var admin=await Login("admin");var email=$"test-{Guid.NewGuid():N}@example.invalid";
        var invited=await admin.PostAsJsonAsync("/api/admin/users",new {name="Testzugang",email,isAdmin=false,sites=new[]{new{siteId=1,role="Employee"}}});Assert.Equal(HttpStatusCode.OK,invited.StatusCode);
        var json=await invited.Content.ReadFromJsonAsync<JsonElement>();var path=json.GetProperty("activationPath").GetString()!;
        var query=Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri("http://localhost"+path).Query);var userId=query["userId"].ToString();var token=query["token"].ToString();
        using var client=Client();var csrf=await Token(client,path);
        var activated=await Form(client,"Activate",csrf,("UserId",userId),("Token",token),("Password","TestPassword2026!"));Assert.Equal(HttpStatusCode.Redirect,activated.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/session")).StatusCode);
        var setup=await client.GetStringAsync("/Account");Assert.Contains("Zugang absichern",setup);csrf=WebUtility.HtmlDecode(Regex.Match(setup,"name=\"csrf-token\" content=\"([^\"]+)\"").Groups[1].Value);
        string secret;
        using(var scope=factory.Services.CreateScope()){var users=scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();var user=await users.FindByIdAsync(userId);Assert.False(user!.TwoFactorEnabled);secret=(await users.GetAuthenticatorKeyAsync(user))!;}
        var verified=await Form(client,"Verify",csrf,("Code",Totp(secret)));Assert.Equal(HttpStatusCode.OK,verified.StatusCode);Assert.Contains("Wiederherstellungscodes",await verified.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/session")).StatusCode);
        var updated=await admin.PutAsJsonAsync("/api/admin/users/"+userId,new {isBlocked=true,isAdmin=false,sites=new[]{new{siteId=1,role="Employee"}}});Assert.Equal(HttpStatusCode.NoContent,updated.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/session")).StatusCode);
        using var reuse=Client();var repeatCsrf=await Token(reuse,path);var repeated=await Form(reuse,"Activate",repeatCsrf,("UserId",userId),("Token",token),("Password","NewPassword2026!"));Assert.NotEqual(HttpStatusCode.Redirect,repeated.StatusCode);
    }
    [Fact] public async Task ParallelCheckInProducesOnlyOneAttendance()
    {
        using var client=await Login("manager");var me=await client.GetFromJsonAsync<JsonElement>("/api/session");var today=me.GetProperty("today").GetString();
        var created=await client.PostAsJsonAsync("/api/children",new {siteId=1,firstName="Parallel",lastName=Guid.NewGuid().ToString()[..8],birthDate="2014-01-01",gender="Male",nationalities=new[]{"DE"},contactName=(string?)null,contactPhone=(string?)null,contactRelationship=(string?)null,revision=(string?)null});Assert.Equal(HttpStatusCode.OK,created.StatusCode);
        var id=(await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var responses=await Task.WhenAll(Enumerable.Range(0,3).Select(_=>client.PostAsJsonAsync($"/api/children/{id}/attendance",new {day=today})));
        Assert.All(responses,r=>Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict));
        using var scope=factory.Services.CreateScope();Assert.Equal(1,await scope.ServiceProvider.GetRequiredService<AppDbContext>().Attendances.CountAsync(a=>a.ChildId==id));
    }
    public static string Totp(string base32)
    {
        const string alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";var bytes=new List<byte>();var buffer=0;var bits=0;
        foreach(var c in base32.ToUpperInvariant()){if(c=='=')break;buffer=(buffer<<5)|alphabet.IndexOf(c);bits+=5;if(bits>=8){bits-=8;bytes.Add((byte)(buffer>>bits));}}
        var counter=BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds()/30);if(BitConverter.IsLittleEndian)Array.Reverse(counter);
        var hash=HMACSHA1.HashData(bytes.ToArray(),counter);var offset=hash[^1]&15;var binary=((hash[offset]&127)<<24)|(hash[offset+1]<<16)|(hash[offset+2]<<8)|hash[offset+3];return (binary%1000000).ToString("D6");
    }
}
