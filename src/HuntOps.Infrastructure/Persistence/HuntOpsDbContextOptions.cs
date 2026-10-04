using Microsoft.EntityFrameworkCore;

namespace HuntOps.Infrastructure.Persistence;

public static class HuntOpsDbContextOptions
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseNodaTime();
                npgsql.MigrationsHistoryTable(MigrationsHistoryTable);
                npgsql.MigrationsAssembly(typeof(HuntOpsDbContext).Assembly.GetName().Name);
            })
            .UseSnakeCaseNamingConvention();
}
