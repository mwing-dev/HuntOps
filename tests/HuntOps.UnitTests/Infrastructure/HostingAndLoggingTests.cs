using HuntOps.Infrastructure.Hosting;
using HuntOps.Infrastructure.Logging;
using Serilog.Events;

namespace HuntOps.UnitTests.Infrastructure;

public sealed class HostingAndLoggingTests
{
    [Theory]
    [InlineData(null, 8080)]
    [InlineData("", 8080)]
    [InlineData("8090", 8090)]
    [InlineData("8090;9000", 8090)]
    [InlineData(" 8091 , 9000", 8091)]
    [InlineData("not-a-port", 8080)]
    public void Health_probe_uses_first_configured_http_port(string? httpPorts, int expected)
    {
        Assert.Equal(expected, ContainerHealthProbe.ResolvePort(httpPorts, defaultPort: 8080));
    }

    [Fact]
    public void Health_probe_is_only_requested_by_exact_argument()
    {
        Assert.True(ContainerHealthProbe.IsRequested(["--healthcheck"]));
        Assert.False(ContainerHealthProbe.IsRequested(["--HEALTHCHECK"]));
        Assert.False(ContainerHealthProbe.IsRequested([]));
    }

    [Theory]
    [InlineData(null, LogEventLevel.Information)]
    [InlineData("Debug", LogEventLevel.Debug)]
    [InlineData("warning", LogEventLevel.Warning)]
    [InlineData("Trace", LogEventLevel.Verbose)]
    [InlineData("Critical", LogEventLevel.Fatal)]
    [InlineData("Error", LogEventLevel.Error)]
    [InlineData("loud", LogEventLevel.Information)]
    [InlineData("42", LogEventLevel.Information)]
    public void Log_level_accepts_serilog_and_microsoft_names(string? value, LogEventLevel expected)
    {
        Assert.Equal(expected, LoggingSetup.ParseLevel(value));
    }
}
