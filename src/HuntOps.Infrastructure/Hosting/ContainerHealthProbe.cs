using System.Globalization;

namespace HuntOps.Infrastructure.Hosting;

/// <summary>
/// Lets a HuntOps container act as its own Docker HEALTHCHECK (<c>dotnet HuntOps.Web.dll --healthcheck</c>),
/// so runtime images do not need curl or wget.
/// </summary>
public static class ContainerHealthProbe
{
    public const string Argument = "--healthcheck";

    public static bool IsRequested(string[] args) => args.Contains(Argument, StringComparer.Ordinal);

    public static async Task<int> RunAsync(int defaultPort)
    {
        var port = ResolvePort(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS"), defaultPort);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            using var response = await http.GetAsync(new Uri($"http://127.0.0.1:{port}/health/ready"));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }

    internal static int ResolvePort(string? httpPorts, int defaultPort)
    {
        var first = httpPorts?
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ? port : defaultPort;
    }
}
