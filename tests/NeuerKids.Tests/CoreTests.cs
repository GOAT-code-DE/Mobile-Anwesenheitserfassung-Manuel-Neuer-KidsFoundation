using System.IO.Compression;
using System.Security.Claims;
using System.Xml.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NeuerKids.Domain;
using NeuerKids.Services;
using Xunit;

namespace NeuerKids.Tests;

public class TestClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
public sealed class TestData : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly ServiceProvider provider;
    public AppDbContext Db { get; }
    public Reports Reports { get; }
    public ChildrenService Children { get; }
    public Access Access { get; }
    public DateOnly Today = new(2026, 9, 23);
    public ClaimsPrincipal Employee = Principal("employee");
    public ClaimsPrincipal Manager = Principal("manager");
    public ClaimsPrincipal Admin = Principal("admin");
    public static ClaimsPrincipal Principal(string id) => new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id) }, "test"));
    public TestData()
    {
        connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        var services = new ServiceCollection(); services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
        services.AddIdentityCore<AppUser>().AddEntityFrameworkStores<AppDbContext>();
        services.AddSingleton<TimeProvider>(new TestClock(new DateTimeOffset(2026, 9, 23, 8, 0, 0, TimeSpan.Zero)));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Demo:Enabled"]="true" }).Build());
        services.AddScoped<BerlinClock>(); services.AddScoped<Access>(); services.AddScoped<Reports>(); services.AddScoped<ChildrenService>();
        provider = services.BuildServiceProvider(); Db = provider.GetRequiredService<AppDbContext>(); Db.Database.EnsureCreated();
        Reports = provider.GetRequiredService<Reports>(); Children = provider.GetRequiredService<ChildrenService>(); Access = provider.GetRequiredService<Access>();
        Db.Sites.AddRange(new Site { Id=1,Name="Gelsenkirchen" },new Site { Id=2,Name="Bottrop" });
        Db.Users.AddRange(new AppUser { Id="employee",UserName="employee",DisplayName="Employee" },new AppUser { Id="manager",UserName="manager",DisplayName="Manager" },new AppUser { Id="admin",UserName="admin",DisplayName="Admin",IsAdmin=true });
        Db.Memberships.AddRange(new Membership { UserId="employee",SiteId=1,Role=SiteRole.Employee },new Membership { UserId="manager",SiteId=1,Role=SiteRole.Manager },new Membership { UserId="manager",SiteId=2,Role=SiteRole.Manager });Db.SaveChanges();
    }
    public async Task<Child> AddChild(int site = 1, DateOnly? birthday = null, string[]? countries = null, Gender gender = Gender.Female)
    {
        var c = new Child { SiteId=site,FirstName="Test",LastName=Guid.NewGuid().ToString()[..8],BirthDate=birthday??new DateOnly(2014,9,22),Gender=gender,CreatedOn=Today.AddMonths(-6) };
        c.Nationalities=(countries??["DE"]).Select(code=>new Nationality {Code=code}).ToList();Db.Children.Add(c);await Db.SaveChangesAsync();return c;
    }
    public async Task AddVisit(Child c, DateOnly date, string actor="manager") {Db.Attendances.Add(new Attendance {ChildId=c.Id,SiteId=c.SiteId,Day=date,CreatedBy=actor});await Db.SaveChangesAsync();}
    public async ValueTask DisposeAsync(){await provider.DisposeAsync();await connection.DisposeAsync();}
}

public class CoreTests
{
    [Theory]
    [InlineData("2014-09-22","2026-09-21",11)]
    [InlineData("2014-09-22","2026-09-22",12)]
    [InlineData("2012-02-29","2025-02-28",13)]
    public void AgeUsesVisitDate(string birth,string day,int expected) => Assert.Equal(expected,BerlinClock.Age(DateOnly.Parse(birth),DateOnly.Parse(day)));
    [Theory]
    [InlineData("2025-09-23",false)] [InlineData("2025-09-22",true)] [InlineData("2025-09-24",false)]
    public void ArchiveBoundaryUsesCalendarMonths(string date,bool inactive) => Assert.Equal(inactive,BerlinClock.IsInactive(DateOnly.Parse(date),new DateOnly(2026,9,23)));
    [Theory]
    [InlineData("2026-03-29T00:00:00Z","2026-03-29T22:00:00Z")]
    [InlineData("2026-10-25T00:00:00Z","2026-10-25T23:00:00Z")]
    [InlineData("2026-12-31T23:05:00Z","2027-01-01T23:00:00Z")]
    public void SessionEndsAtBerlinMidnightAcrossDstAndYear(string now,string midnight) => Assert.Equal(DateTimeOffset.Parse(midnight),new BerlinClock(new TestClock(DateTimeOffset.Parse(now))).NextMidnight);
    [Fact] public async Task DistinctChildrenAreNotAddedAcrossDays()
    {
        await using var t=new TestData();var c=await t.AddChild();foreach(var d in new[]{21,22,23})await t.AddVisit(c,new DateOnly(2026,9,d));
        var report=await t.Reports.Build(t.Employee,new(new(2026,9,21),t.Today,[1],Compare:false));
        Assert.Equal(3,report.Current.Metrics.Visits);Assert.Equal(1,report.Current.Metrics.Children);Assert.Equal(1,report.Current.Metrics.Average);
    }
    [Fact] public async Task NationalityOrFilterDoesNotDoubleCount()
    {
        await using var t=new TestData();var c=await t.AddChild(countries:["DE","TR"]);await t.AddVisit(c,t.Today);
        var r=await t.Reports.Build(t.Employee,new(t.Today,t.Today,[1],Nationalities:["DE","TR"],Compare:false));
        Assert.Equal(1,r.Current.Metrics.Visits);Assert.Equal(2,r.Current.Nationalities.Sum(x=>x.Value));
    }
    [Fact] public async Task AgeAtVisitAndCombinedFiltersAreCorrect()
    {
        await using var t=new TestData();var c=await t.AddChild();await t.AddVisit(c,new(2026,9,21));await t.AddVisit(c,new(2026,9,22));
        var r=await t.Reports.Build(t.Employee,new(new(2026,9,21),t.Today,[1],MinAge:12,MaxAge:12,Genders:[Gender.Female],Nationalities:["DE"],Weekday:2,Compare:false));
        Assert.Equal(1,r.Current.Metrics.Visits);Assert.Equal(1,r.Current.Metrics.CalendarDays);Assert.Equal(1,r.Current.Metrics.Average);
    }
    [Fact] public async Task ZeroDaysCountInAverageAndWeekUsesSameWeekdays()
    {
        await using var t=new TestData();var c=await t.AddChild();await t.AddVisit(c,t.Today);
        var r=await t.Reports.Build(t.Employee,new(new(2026,9,21),t.Today,[1],Preset:"week"));
        Assert.Equal(.33,r.Current.Metrics.Average);Assert.Equal(new DateOnly(2026,9,14),r.Comparison!.Start);Assert.Equal(new DateOnly(2026,9,16),r.Comparison.End);
    }
    [Fact] public async Task StaffCannotReadOtherSiteAndAdminHasNoImplicitDataAccess()
    {
        await using var t=new TestData();Assert.Equal(403,(await Assert.ThrowsAsync<AppError>(()=>t.Access.Site(t.Employee,2))).Status);
        await Assert.ThrowsAsync<AppError>(()=>t.Reports.Build(t.Employee,new(t.Today,t.Today,[2])));
        await Assert.ThrowsAsync<AppError>(()=>t.Children.List(t.Admin,1,null,true));
    }
    [Fact] public async Task RepeatAttendanceIsIdempotentAndDatabaseEnforcesUniqueness()
    {
        await using var t=new TestData();var c=await t.AddChild();await t.Children.Attend(t.Employee,c.Id,t.Today);await t.Children.Attend(t.Employee,c.Id,t.Today);Assert.Equal(1,await t.Db.Attendances.CountAsync());
        t.Db.Attendances.Add(new Attendance { ChildId=c.Id,SiteId=1,Day=t.Today });await Assert.ThrowsAsync<DbUpdateException>(()=>t.Db.SaveChangesAsync());
    }
    [Fact] public async Task StaffCanUndoOnlyOwnCurrentDay()
    {
        await using var t=new TestData();var c=await t.AddChild();await t.AddVisit(c,t.Today,"employee");var own=await t.Db.Attendances.SingleAsync();await t.Children.Undo(t.Employee,own.Id);Assert.Empty(t.Db.Attendances);
        await t.AddVisit(c,t.Today,"manager");await Assert.ThrowsAsync<AppError>(()=>t.Children.Undo(t.Employee,t.Db.Attendances.Single().Id));
        await t.AddVisit(c,t.Today.AddDays(-1),"employee");await Assert.ThrowsAsync<AppError>(()=>t.Children.Undo(t.Employee,t.Db.Attendances.Single(a=>a.Day!=t.Today).Id));
    }
    [Fact] public async Task StaffCannotBackfillOrDeleteAndManagerCan()
    {
        await using var t=new TestData();var c=await t.AddChild();await Assert.ThrowsAsync<AppError>(()=>t.Children.Attend(t.Employee,c.Id,t.Today.AddDays(-1)));await Assert.ThrowsAsync<AppError>(()=>t.Children.Delete(t.Employee,c.Id,false));
        await t.Children.Attend(t.Manager,c.Id,t.Today.AddDays(-1));await t.Children.Delete(t.Manager,c.Id,false);Assert.Empty(t.Db.Children);Assert.Empty(t.Db.Attendances);Assert.Empty(t.Db.MonthlyArchives);
    }
    [Fact] public async Task PurgeRetainsMonthlyTotalsButNotFalseDistinctAcrossMonths()
    {
        await using var t=new TestData();var c=await t.AddChild();await t.AddVisit(c,new(2026,7,10));await t.AddVisit(c,new(2026,8,10));await t.AddVisit(c,new(2026,8,11));await t.Children.Delete(t.Manager,c.Id,true);
        Assert.Empty(t.Db.Children);Assert.Empty(t.Db.Attendances);Assert.Empty(t.Db.Nationalities);
        var one=await t.Reports.Build(t.Manager,new(new(2026,8,1),new(2026,8,31),[1],Compare:false));Assert.Equal(2,one.Current.Metrics.Visits);Assert.Equal(1,one.Current.Metrics.Children);
        var two=await t.Reports.Build(t.Manager,new(new(2026,7,1),new(2026,8,31),[1],Compare:false));Assert.Equal(3,two.Current.Metrics.Visits);Assert.Null(two.Current.Metrics.Children);
        var partial=await t.Reports.Build(t.Manager,new(new(2026,8,10),new(2026,8,11),[1],Compare:false));Assert.Null(partial.Current.Metrics.Visits);
        var filtered=await t.Reports.Build(t.Manager,new(new(2026,8,1),new(2026,8,31),[1],MinAge:10,Compare:false));Assert.Null(filtered.Current.Metrics.Visits);Assert.DoesNotContain(t.Db.AuditEntries,a=>a.ChildId==c.Id);
    }
    [Fact] public async Task EditingRequiresCurrentRevisionAndDuplicateConfirmation()
    {
        await using var t=new TestData();var c=await t.AddChild();var input=new ChildInput(1,c.FirstName,c.LastName,c.BirthDate,c.Gender,["DE"],null,null,null,null);
        await Assert.ThrowsAsync<AppError>(()=>t.Children.Save(t.Employee,null,input));await Assert.ThrowsAsync<AppError>(()=>t.Children.Save(t.Employee,c.Id,input));
        await t.Children.Save(t.Employee,c.Id,input with {Revision=c.Revision,FirstName="Geändert"});Assert.Equal("Geändert",c.FirstName);
    }
    [Fact] public async Task ExcelContainsSameTotalsAndNoNamesOrFormulaCells()
    {
        await using var t=new TestData();var c=await t.AddChild();c.FirstName="SECRET_NAME";await t.AddVisit(c,t.Today);
        var r=await t.Reports.Build(t.Manager,new(t.Today,t.Today,[1],Compare:false));var bytes=ExcelExport.Create(r);using var stream=new MemoryStream(bytes);using var zip=new ZipArchive(stream);
        Assert.NotNull(zip.GetEntry("[Content_Types].xml"));var all="";foreach(var entry in zip.Entries){using var reader=new StreamReader(entry.Open());all+=await reader.ReadToEndAsync();}
        Assert.DoesNotContain("SECRET_NAME",all);Assert.DoesNotContain(c.LastName,all);Assert.DoesNotContain("<f>",all);Assert.Contains("Besuche",all);Assert.Contains("<v>1</v>",all);
    }
    [Fact] public async Task InvalidFiltersAreRejected()
    {
        await using var t=new TestData();await Assert.ThrowsAsync<AppError>(()=>t.Reports.Build(t.Employee,new(t.Today,t.Today.AddDays(-1),[1])));
        await Assert.ThrowsAsync<AppError>(()=>t.Reports.Build(t.Employee,new(t.Today,t.Today,[1],MinAge:15,MaxAge:10)));
        await Assert.ThrowsAsync<AppError>(()=>t.Reports.Build(t.Employee,new(t.Today,t.Today,[1],Nationalities:["BAD"])));
    }
}
