using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NeuerKids.Domain;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>();
        // Migrations target Azure SQL. Local SQLite is intentionally a disposable demo store.
        options.UseSqlServer(Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? "Server=localhost;Database=NeuerKids;Integrated Security=true;Encrypt=true");
        return new AppDbContext(options.Options);
    }
}
