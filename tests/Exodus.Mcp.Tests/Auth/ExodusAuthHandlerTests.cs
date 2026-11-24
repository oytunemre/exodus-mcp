using System.Net;
using System.Net.Http.Json;
using Exodus.Mcp.Core.Api;
using Exodus.Mcp.Core.Auth;
using Exodus.Mcp.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Exodus.Mcp.Tests.Auth.AuthTestServices;

namespace Exodus.Mcp.Tests.Auth;

public class ExodusAuthHandlerTests
{
    // The HttpClient behind the typed ExodusApiClient: same name, same handler chain.
    private static HttpClient ApiHttp(ServiceProvider sp)
        => sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ExodusApiClient));

    [Fact]
    public async Task AddsBearerHeader()
    {
        var fake = new FakeExodusHandler()
            .WithLogin(token: "access-1", expiresAtUtc: At(TimeSpan.FromHours(1)))
            .On(HttpMethod.Get, "/api/auth/me", HttpStatusCode.OK, new { userId = "7" });
        using var sp = Build(fake, new ManualTimeProvider(Start));
        using var http = ApiHttp(sp);

        using var response = await http.GetAsync("api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        fake.Requests.Single(r => r.PathAndQuery == "/api/auth/me").Authorization.Should().Be("Bearer access-1");
        fake.Requests.Single(r => r.PathAndQuery == LoginPath).Authorization.Should().BeNull();
    }

    [Fact]
    public async Task Unauthorized_RenewsAndRetriesOnce_WithSameBody()
    {
        var fake = new FakeExodusHandler()
            .WithLogin(token: "access-1", refreshToken: "refresh-1", expiresAtUtc: At(TimeSpan.FromHours(1)))
            .On(HttpMethod.Post, RefreshPath, HttpStatusCode.OK, RefreshJson("access-2", "refresh-2", At(TimeSpan.FromHours(2))))
            .On(HttpMethod.Post, "/api/cart/add", HttpStatusCode.Unauthorized, new { detail = "Token expired" })
            .On(HttpMethod.Post, "/api/cart/add", HttpStatusCode.OK, new { ok = true });
        using var sp = Build(fake, new ManualTimeProvider(Start));
        using var http = ApiHttp(sp);

        using var response = await http.PostAsJsonAsync("api/cart/add", new { userId = 7, listingId = 42, quantity = 2 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var attempts = fake.Requests.Where(r => r.PathAndQuery == "/api/cart/add").ToList();
        attempts.Should().HaveCount(2);
        attempts[0].Authorization.Should().Be("Bearer access-1");
        attempts[1].Authorization.Should().Be("Bearer access-2");
        attempts[0].Body.Should().Contain("\"listingId\":42");
        attempts[1].Body.Should().Be(attempts[0].Body);
        fake.Count(HttpMethod.Post, RefreshPath).Should().Be(1);
        fake.Count(HttpMethod.Post, LoginPath).Should().Be(1);
    }

    [Fact]
    public async Task SecondUnauthorized_IsReturned_NoLoop()
    {
        var fake = new FakeExodusHandler()
            .WithLogin(token: "access-1", refreshToken: "refresh-1", expiresAtUtc: At(TimeSpan.FromHours(1)))
            .On(HttpMethod.Post, RefreshPath, HttpStatusCode.OK, RefreshJson("access-2", "refresh-2", At(TimeSpan.FromHours(2))))
            .On(HttpMethod.Get, "/api/cart/7", HttpStatusCode.Unauthorized, new { detail = "Nope" });
        using var sp = Build(fake, new ManualTimeProvider(Start));
        using var http = ApiHttp(sp);

        using var response = await http.GetAsync("api/cart/7");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        fake.Count(HttpMethod.Get, "/api/cart/7").Should().Be(2);
        fake.Count(HttpMethod.Post, RefreshPath).Should().Be(1);
        fake.Count(HttpMethod.Post, LoginPath).Should().Be(1);
    }

    [Fact]
    public async Task Logs_UsernameAndRole_NeverSecrets()
    {
        var logs = new CapturingLoggerProvider();
        var fake = new FakeExodusHandler()
            .WithLogin(token: "access-1", refreshToken: "refresh-1", expiresAtUtc: At(TimeSpan.FromHours(1)))
            .On(HttpMethod.Post, RefreshPath, HttpStatusCode.OK, RefreshJson("access-2", "refresh-2", At(TimeSpan.FromHours(2))))
            .On(HttpMethod.Post, "/api/cart/add", HttpStatusCode.Unauthorized, new { detail = "Token expired" })
            .On(HttpMethod.Post, "/api/cart/add", HttpStatusCode.OK, new { ok = true });
        using var sp = Build(fake, new ManualTimeProvider(Start), logs: logs);
        using var http = ApiHttp(sp);

        using var response = await http.PostAsJsonAsync("api/cart/add", new { listingId = 42 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        logs.Entries.Should().Contain(e =>
            e.Category == typeof(TokenStore).FullName && e.Level == LogLevel.Information
            && e.Text.Contains("testuser") && e.Text.Contains("Customer"));
        // Trace level includes the HttpClient header logs, so this also covers the Authorization header.
        logs.Entries.Should().NotContain(e =>
            e.Text.Contains(Password) || e.Text.Contains("access-1") || e.Text.Contains("access-2")
            || e.Text.Contains("refresh-1") || e.Text.Contains("refresh-2"));
    }
}
