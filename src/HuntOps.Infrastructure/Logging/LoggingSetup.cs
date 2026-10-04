using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace HuntOps.Infrastructure.Logging;

public static class LoggingSetup
{
    /// <summary>
    /// Structured logging for every HuntOps host: JSON to stdout by default (LOG_FORMAT=text for local readability),
    /// minimum level from LOG_LEVEL. HuntOps code never logs connection strings or secrets.
    /// </summary>
    public static IHostApplicationBuilder AddHuntOpsLogging(
        this IHostApplicationBuilder builder,
        string applicationName,
        Action<LoggerConfiguration>? configure = null)
    {
        var level = ParseLevel(builder.Configuration["LOG_LEVEL"]);
        var textFormat = string.Equals(builder.Configuration["LOG_FORMAT"], "text", StringComparison.OrdinalIgnoreCase);

        builder.Services.AddSerilog((_, logger) =>
        {
            logger
                .MinimumLevel.Is(level)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", applicationName);

            configure?.Invoke(logger);

            if (textFormat)
            {
                logger.WriteTo.Console(formatProvider: CultureInfo.InvariantCulture);
            }
            else
            {
                logger.WriteTo.Console(new RenderedCompactJsonFormatter());
            }
        });

        return builder;
    }

    /// <summary>Accepts Serilog level names plus the Microsoft names Trace and Critical.</summary>
    internal static LogEventLevel ParseLevel(string? value)
    {
        if (string.Equals(value, "Trace", StringComparison.OrdinalIgnoreCase))
        {
            return LogEventLevel.Verbose;
        }

        if (string.Equals(value, "Critical", StringComparison.OrdinalIgnoreCase))
        {
            return LogEventLevel.Fatal;
        }

        return Enum.TryParse<LogEventLevel>(value, ignoreCase: true, out var level) && Enum.IsDefined(level)
            ? level
            : LogEventLevel.Information;
    }
}
