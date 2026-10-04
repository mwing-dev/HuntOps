using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HuntOps.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c>. Creating migrations does not connect to the database;
/// <c>dotnet ef database update</c> uses HUNTOPS_DESIGN_CONNECTION when set.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HuntOpsDbContext>
{
    public HuntOpsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("HUNTOPS_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=huntops;Username=huntops;Password=huntops";

        var options = new DbContextOptionsBuilder<HuntOpsDbContext>();
        HuntOpsDbContextOptions.Configure(options, connectionString);
        return new HuntOpsDbContext(options.Options);
    }
}
