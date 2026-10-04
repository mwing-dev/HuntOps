using HuntOps.Application.Access;
using HuntOps.Application.Common;
using HuntOps.Domain.Actions;
using HuntOps.Domain.Users;
using HuntOps.Infrastructure;
using HuntOps.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Events;

namespace HuntOps.Worker.Commands;

/// <summary>
/// <c>HuntOps.Worker apikey create|list|revoke</c>: manages REST/MCP API keys until the dashboard (Phase 3) does.
/// Run it with <c>docker compose exec huntops-worker dotnet HuntOps.Worker.dll apikey ...</c>; exec output is not
/// captured in container logs. The plaintext key is printed exactly once, at creation.
/// </summary>
internal static class ApiKeyCommand
{
    public const string Name = "apikey";

    private const string Usage = """
        Usage:
          apikey create --name <name> --scope read|write [--expires-days <days>]
          apikey list
          apikey revoke <prefix-or-id>
        """;

    public static bool IsRequested(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Name, StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null)
    {
        var sub = args.Length > 1 ? args[1].ToLowerInvariant() : "";
        if (sub is not ("create" or "list" or "revoke"))
        {
            await error.WriteLineAsync(Usage);
            return 2;
        }

        var builder = Host.CreateApplicationBuilder([]);
        if (configurationOverrides is not null)
        {
            builder.Configuration.AddInMemoryCollection(configurationOverrides);
        }

        builder.AddHuntOpsLogging("huntops-cli", logger => logger.MinimumLevel.Is(LogEventLevel.Warning));
        builder.Services.AddHuntOpsInfrastructure();
        builder.Services.Replace(ServiceDescriptor.Scoped<ICurrentActor, CliActor>());

        using var host = builder.Build();
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ApiKeyService>();

        try
        {
            switch (sub)
            {
                case "create":
                    var created = await service.CreateAsync(
                        new ApiKeyCreateInput(Option(args, "--name"), Option(args, "--scope"), IntOption(args, "--expires-days")),
                        CancellationToken.None);
                    await output.WriteLineAsync($"Created API key '{created.Key.Name}' ({created.Key.Prefix}, scopes: {string.Join(",", created.Key.Scopes)}).");
                    await output.WriteLineAsync("Copy it now; it cannot be shown again:");
                    await output.WriteLineAsync();
                    await output.WriteLineAsync(created.Secret);
                    return 0;

                case "list":
                    foreach (var key in await service.ListAsync(CancellationToken.None))
                    {
                        var state = key.RevokedAt is not null ? "revoked" : key.IsActive ? "active" : "expired";
                        await output.WriteLineAsync(
                            $"{key.Prefix}  {state,-7}  {string.Join(",", key.Scopes),-10}  {key.Name}  (created {key.CreatedAt}, last used {key.LastUsedAt ?? "never"})");
                    }

                    return 0;

                default:
                    if (args.Length < 3)
                    {
                        await error.WriteLineAsync(Usage);
                        return 2;
                    }

                    var revoked = await service.RevokeAsync(args[2], CancellationToken.None);
                    await output.WriteLineAsync($"Revoked {revoked.Prefix} ({revoked.Name}).");
                    return 0;
            }
        }
        catch (RequestValidationException ex)
        {
            foreach (var (field, messages) in ex.Errors)
            {
                await error.WriteLineAsync($"{field}: {string.Join(" ", messages)}");
            }

            return 2;
        }
        catch (NotFoundException ex)
        {
            await error.WriteLineAsync(ex.Message);
            return 1;
        }
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int? IntOption(string[] args, string name) =>
        int.TryParse(Option(args, name), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

    private sealed class CliActor : ICurrentActor
    {
        public string ActorId => "cli";

        public string UserId => Owner.UserId;

        public ChangeChannel Channel => ChangeChannel.Cli;
    }
}
