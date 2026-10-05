using System.Net;
using HuntOps.IntegrationTests.Infrastructure;
using HuntOps.Infrastructure.Identity;
using HuntOps.Web.Components.Account.Pages;
using HuntOps.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace HuntOps.IntegrationTests.Web;

public sealed class AuthenticationTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/")]
    [InlineData("/programs")]
    [InlineData("/settings/api-keys")]
    [InlineData("/system")]
    public async Task Anonymous_dashboard_requests_redirect_to_login(string path)
    {
        await using var factory = await DashboardFactory.CreateAsync(postgres);
        using var browser = factory.CreateBrowser();

        using var response = await browser.GetAsync(new Uri(path, UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/account/login", response.Headers.Location!.PathAndQuery, StringComparison.Ordinal);
        Assert.Contains($"ReturnUrl={Uri.EscapeDataString(path)}", response.Headers.Location.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Owner_signs_in_with_secure_cookie_and_sees_the_dashboard()
    {
        await using var factory = await DashboardFactory.CreateAsync(postgres);
        using var browser = factory.CreateBrowser();

        using var signIn = await DashboardFactory.SignInAsync(browser, factory.Email, factory.Password, "/programs");

        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        var location = signIn.Headers.Location!;
        Assert.Equal("/programs", location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString);
        Assert.True(!location.IsAbsoluteUri || location.Host == "localhost", "Sign-in must only redirect within the site.");
        var authCookie = Assert.Single(signIn.Headers.GetValues("Set-Cookie"), c => c.StartsWith(WebSecuritySetup.AuthCookieName + "=", StringComparison.Ordinal));
        Assert.Contains("secure", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", authCookie, StringComparison.OrdinalIgnoreCase);

        var dashboard = await browser.GetStringAsync(new Uri("/", UriKind.Relative), Ct);
        Assert.Contains("Action required", dashboard, StringComparison.Ordinal);
        Assert.Contains(factory.Email, dashboard, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wrong_password_is_rejected_without_a_cookie()
    {
        await using var factory = await DashboardFactory.CreateAsync(postgres);
        using var browser = factory.CreateBrowser();

        using var response = await DashboardFactory.SignInAsync(browser, factory.Email, factory.Password + "x");
        var html = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid email or password.", html, StringComparison.Ordinal);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith(WebSecuritySetup.AuthCookieName, StringComparison.Ordinal)));
        Assert.DoesNotContain(factory.Password, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_even_for_the_right_password()
    {
        await using var factory = await DashboardFactory.CreateAsync(postgres);
        using var browser = factory.CreateBrowser();

        for (var i = 0; i < IdentityPolicy.MaxFailedAttempts; i++)
        {
            using var failed = await DashboardFactory.SignInAsync(browser, factory.Email, TestSecrets.NewOwnerPassword());
        }

        using var response = await DashboardFactory.SignInAsync(browser, factory.Email, factory.Password);
        var html = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Too many failed attempts", html, StringComparison.Ordinal);
        using var dashboard = await browser.GetAsync(new Uri("/", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.Redirect, dashboard.StatusCode);
    }

    [Fact]
    public async Task Login_post_without_antiforgery_token_is_rejected()
    {
        await using var factory = await DashboardFactory.CreateAsync(postgres);
        using var browser = factory.CreateBrowser();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["Input.Email"] = factory.Email,
            ["Input.Password"] = factory.Password,
        });

        using var response = await browser.PostAsync(new Uri("/account/login", UriKind.Relative), content, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie") && response.Headers.GetValues("Set-Cookie").Any(c => c.StartsWith(WebSecuritySetup.AuthCookieName, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Logout_requires_antiforgery_and_ends_the_session()
    {
        await using var factory = await DashboardFactory.CreateAsync(postgres);
        using var browser = factory.CreateBrowser();
        using (await DashboardFactory.SignInAsync(browser, factory.Email, factory.Password))
        {
        }

        using (var forged = new FormUrlEncodedContent(new Dictionary<string, string> { ["returnUrl"] = "" }))
        using (var rejected = await browser.PostAsync(new Uri("/account/logout", UriKind.Relative), forged, Ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        var page = await browser.GetStringAsync(new Uri("/", UriKind.Relative), Ct);
        var fields = DashboardFactory.HiddenFields(page);
        Assert.Contains(fields.Keys, k => k.Contains("RequestVerificationToken", StringComparison.Ordinal));
        using var logout = await browser.PostAsync(new Uri("/account/logout", UriKind.Relative), new FormUrlEncodedContent(fields), Ct);

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        using var after = await browser.GetAsync(new Uri("/", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
    }

    [Fact]
    public async Task Cookie_authentication_never_authorizes_the_api()
    {
        await using var factory = await DashboardFactory.CreateAsync(postgres);
        using var browser = factory.CreateBrowser();
        using (await DashboardFactory.SignInAsync(browser, factory.Email, factory.Password))
        {
        }

        using var response = await browser.GetAsync(new Uri("/api/jurisdictions", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_page_explains_a_missing_owner_instead_of_offering_defaults()
    {
        var factory = new DashboardFactory(await postgres.CreateMigratedDatabaseAsync(), "", "");
        await using (factory)
        {
            using var browser = factory.CreateBrowser();
            var html = await browser.GetStringAsync(new Uri("/account/login", UriKind.Relative), Ct);

            Assert.Contains("No owner account is configured", html, StringComparison.Ordinal);
            Assert.Contains("HUNTOPS_ADMIN_EMAIL", html, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("/programs", "/programs")]
    [InlineData("/events/123?x=1", "/events/123?x=1")]
    [InlineData("//evil.example/phish", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("/account/logout", "/")]
    [InlineData(null, "/")]
    public void Return_urls_are_restricted_to_local_dashboard_paths(string? returnUrl, string expected)
    {
        Assert.Equal(expected, Login.SafeReturnUrl(returnUrl));
    }

    [Theory]
    [InlineData(null, CookieSecurePolicy.SameAsRequest)]
    [InlineData("auto", CookieSecurePolicy.SameAsRequest)]
    [InlineData("ALWAYS", CookieSecurePolicy.Always)]
    public void Cookie_security_mode_is_explicit(string? value, CookieSecurePolicy expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["HUNTOPS_COOKIE_SECURE"] = value })
            .Build();

        Assert.Equal(expected, WebSecuritySetup.CookieSecurity(configuration));
    }

    [Fact]
    public void Unknown_cookie_security_mode_fails_fast()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["HUNTOPS_COOKIE_SECURE"] = "sometimes" })
            .Build();

        Assert.Throws<InvalidOperationException>(() => WebSecuritySetup.CookieSecurity(configuration));
    }
}
