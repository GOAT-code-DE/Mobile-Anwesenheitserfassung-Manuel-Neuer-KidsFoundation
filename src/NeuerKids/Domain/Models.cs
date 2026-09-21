using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace NeuerKids.Domain;

public enum SiteRole { Employee, Manager }
public enum Gender { Female, Male, Diverse, Unspecified }
public class AppUser : IdentityUser
{
    [MaxLength(100)] public string DisplayName { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool IsBlocked { get; set; }
}
public class Site
{
    public int Id { get; set; }
    [MaxLength(80)] public string Name { get; set; } = "";
}
public class Membership
{
    public string UserId { get; set; } = "";
    public int SiteId { get; set; }
    public SiteRole Role { get; set; }
    public AppUser User { get; set; } = null!;
    public Site Site { get; set; } = null!;
}
public class Child
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int SiteId { get; set; }
    public Site Site { get; set; } = null!;
    [MaxLength(80)] public string FirstName { get; set; } = "";
    [MaxLength(80)] public string LastName { get; set; } = "";
    public DateOnly BirthDate { get; set; }
    public DateOnly CreatedOn { get; set; }
    public Gender Gender { get; set; }
    [MaxLength(100)] public string? ContactName { get; set; }
    [MaxLength(40)] public string? ContactPhone { get; set; }
    [MaxLength(80)] public string? ContactRelationship { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public List<Nationality> Nationalities { get; set; } = [];
    public List<Attendance> Attendances { get; set; } = [];
}
public class Nationality
{
    public Guid ChildId { get; set; }
    [MaxLength(2)] public string Code { get; set; } = "";
    public Child Child { get; set; } = null!;
}
public class Attendance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChildId { get; set; }
    public int SiteId { get; set; }
    public Child Child { get; set; } = null!;
    public DateOnly Day { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    [MaxLength(450)] public string CreatedBy { get; set; } = "";
}
// Only independent, coarse monthly counts survive a personal-data purge.
// No profile IDs, demographic attributes or cross-month linkages are retained here.
public class MonthlyArchive
{
    public int SiteId { get; set; }
    public DateOnly Month { get; set; }
    public int Visits { get; set; }
    public int Profiles { get; set; }
}
public class AuditEntry
{
    public long Id { get; set; }
    public DateTime AtUtc { get; set; }
    [MaxLength(450)] public string ActorId { get; set; } = "";
    public int? SiteId { get; set; }
    [MaxLength(64)] public string Action { get; set; } = "";
    public Guid? ChildId { get; set; }
}
public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<Child> Children => Set<Child>();
    public DbSet<Nationality> Nationalities => Set<Nationality>();
    public DbSet<Attendance> Attendances => Set<Attendance>();
    public DbSet<MonthlyArchive> MonthlyArchives => Set<MonthlyArchive>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Site>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Membership>().HasKey(x => new { x.UserId, x.SiteId });
        b.Entity<Nationality>().HasKey(x => new { x.ChildId, x.Code });
        b.Entity<Child>().HasAlternateKey(x => new { x.Id, x.SiteId });
        b.Entity<Child>().Property(x => x.Revision).IsConcurrencyToken();
        b.Entity<Attendance>().HasOne(x => x.Child).WithMany(x => x.Attendances)
            .HasForeignKey(x => new { x.ChildId, x.SiteId }).HasPrincipalKey(x => new { x.Id, x.SiteId });
        b.Entity<Attendance>().HasIndex(x => new { x.ChildId, x.SiteId, x.Day }).IsUnique();
        b.Entity<Attendance>().HasIndex(x => new { x.SiteId, x.Day });
        b.Entity<Child>().HasIndex(x => new { x.SiteId, x.LastName, x.FirstName });
        b.Entity<MonthlyArchive>().HasKey(x => new { x.SiteId, x.Month });
        b.Entity<AuditEntry>().HasIndex(x => x.AtUtc);
    }
}
