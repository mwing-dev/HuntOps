using HuntOps.Infrastructure;
using HuntOps.Infrastructure.Persistence;
using HuntOps.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HuntOps.IntegrationTests.Persistence;

public sealed class DataProtectionPersistenceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Keys_are_stored_in_postgres_and_survive_a_new_process()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();

        string protectedValue;
        await using (var first = TestServices.Build(connectionString, s => s.AddHuntOpsDataProtection()))
        {
            protectedValue = first.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("HuntOps.Tests")
                .Protect("ntfy-token-value");
        }

        // A second, independent service provider stands in for a recreated container.
        await using var second = TestServices.Build(connectionString, s => s.AddHuntOpsDataProtection());
        var unprotected = second.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("HuntOps.Tests")
            .Unprotect(protectedValue);

        Assert.Equal("ntfy-token-value", unprotected);

        await using var scope = second.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HuntOpsDbContext>();
        Assert.True(await db.DataProtectionKeys.AnyAsync(TestContext.Current.CancellationToken));
    }
}
