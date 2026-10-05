using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HuntOps.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c>. Creating migrations never connects to a database, so no credentials are needed.
/// Commands that do connect (<c>dotnet ef database update</c>) read the full connection string from
/// HUNTOPS_DESIGN_CONNECTION. There is deliberately no built-in default password.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HuntOpsDbContext>
{
    private const string MigrationsOnlyConnectionString = "Host=localhost;Database=huntops";

    public HuntOpsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("HUNTOPS_DESIGN_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = MigrationsOnlyConnectionString;
        }

        var options = new DbContextOptionsBuilder<HuntOpsDbContext>();
        HuntOpsDbContextOptions.Configure(options, connectionString);
        return new HuntOpsDbContext(options.Options);
    }
}
