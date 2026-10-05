using System.Net;
using System.Text.RegularExpressions;
using HuntOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace HuntOps.IntegrationTests.Infrastructure;

/// <summary>The real web host with a bootstrapped owner account, for sign-in and dashboard tests.</summary>
internal sealed partial class DashboardFactory(string connectionString, string email, string password, string environment = "Development")
    : WebApplicationFactory<Program>
{
    public string ConnectionString => connectionString;

    public string Email => email;

    public string Password => password;

    public static async Task<DashboardFactory> CreateAsync(PostgresFixture postgres, string environment = "Development")
    {
        var factory = new DashboardFactory(await postgres.CreateMigratedDatabaseAsync(), "owner@huntops.test", TestSecrets.NewOwnerPassword(), environment);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OwnerBootstrapper>().RunAsync(CancellationToken.None);
        return factory;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:HuntOps", connectionString);
        builder.UseSetting(OwnerBootstrapper.EmailVariable, email);
        builder.UseSetting(OwnerBootstrapper.PasswordVariable, password);
        builder.UseSetting("LOG_LEVEL", "Warning");
    }

    /// <summary>A browser-like client over HTTPS (so Secure cookies flow) that does not follow redirects.</summary>
    public HttpClient CreateBrowser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    /// <summary>Loads the login form (collecting its antiforgery fields) and posts credentials.</summary>
    public static async Task<HttpResponseMessage> SignInAsync(HttpClient browser, string email, string password, string returnUrl = "/")
    {
        var form = await browser.GetStringAsync(new Uri($"/account/login?ReturnUrl={Uri.EscapeDataString(returnUrl)}", UriKind.Relative), TestContext.Current.CancellationToken);
        var fields = HiddenFields(form);
        fields["Input.Email"] = email;
        fields["Input.Password"] = password;
        using var content = new FormUrlEncodedContent(fields);
        return await browser.PostAsync(new Uri($"/account/login?ReturnUrl={Uri.EscapeDataString(returnUrl)}", UriKind.Relative), content, TestContext.Current.CancellationToken);
    }

    public static Dictionary<string, string> HiddenFields(string html)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in HiddenInput().Matches(html))
        {
            fields[WebUtility.HtmlDecode(match.Groups["name"].Value)] = WebUtility.HtmlDecode(match.Groups["value"].Value);
        }

        return fields;
    }

    [GeneratedRegex("<input[^>]*type=\"hidden\"[^>]*name=\"(?<name>[^\"]+)\"[^>]*value=\"(?<value>[^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex HiddenInput();
}
