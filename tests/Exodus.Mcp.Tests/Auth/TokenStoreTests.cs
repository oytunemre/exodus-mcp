using System.Net;
using Exodus.Mcp.Core.Api;
using Exodus.Mcp.Core.Auth;
using Exodus.Mcp.Core.Configuration;
using Exodus.Mcp.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using static Exodus.Mcp.Tests.Auth.AuthTestServices;

namespace Exodus.Mcp.Tests.Auth;

public class TokenStoreTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private const string BadPasswordMessage =
        "Exodus girişi reddetti: Invalid credentials. Exodus mesajını kontrol edin; gerekirse EXODUS_USERNAME/EXODUS_PASSWORD değerlerini düzeltip sunucuyu yeniden başlatın.";

    [Fact]
    public async Task LazyLogin_NoRequestUntilFirstUse()
    {
        var fake = new FakeExodusHandler().WithLogin(expiresAtUtc: At(TimeSpan.FromHours(1)));
        using var sp = Build(fake, new ManualTimeProvider(Start));

        var store = sp.GetRequiredService<TokenStore>();
        _ = sp.GetRequiredService<ExodusApiClient>();
        fake.Requests.Should().BeEmpty();

        var session = await store.GetSessionAsync(Ct);

        session.UserId.Should().Be(7);
        session.Role.Should().Be("Customer");
        session.AccessToken.Should().Be("access-1");
        session.RefreshToken.Should().Be("refresh-1");
        session.AccessTokenExpiresAtUtc.Should().Be(At(TimeSpan.FromHours(1)));
        fake.Count(HttpMethod.Post, LoginPath).Should().Be(1);
        JsonField(fake.Requests[0].Body, "emailOrUsername").Should().Be(Username);
        JsonField(fake.Requests[0].Body, "password").Should().Be(Password);

        (await store.GetSessionAsync(Ct)).Should().BeSameAs(session);
        fake.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task ConcurrentCallers_SingleLogin()
    {
        var fake = new FakeExodusHandler().WithLogin(expiresAtUtc: At(TimeSpan.FromHours(1)), delay: TimeSpan.FromMilliseconds(50));
        using var sp = Build(fake, new ManualTimeProvider(Start));
        var store = sp.GetRequiredService<TokenStore>();

        var sessions = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => store.GetSessionAsync(Ct))));

        fake.Count(HttpMethod.Post, LoginPath).Should().Be(1);
        sessions.Should().AllSatisfy(s => s.AccessToken.Should().Be("access-1"));
    }

    [Fact]
    public async Task RefreshesWithin60Seconds_AndStoresRotatedRefreshToken()
    {
        var clock = new ManualTimeProvider(Start);
        var fake = new FakeExodusHandler()
            .WithLogin(token: "access-1", refreshToken: "refresh-1", expiresAtUtc: At(TimeSpan.FromMinutes(10)))
            .On(HttpMethod.Post, RefreshPath, HttpStatusCode.OK, RefreshJson("access-2", "refresh-2", WithoutZone(At(TimeSpan.FromMinutes(20)))))
            .On(HttpMethod.Post, RefreshPath, HttpStatusCode.OK, RefreshJson("access-3", "refresh-3", WithoutZone(At(TimeSpan.FromMinutes(30)))));
        using var sp = Build(fake, clock);
        var store = sp.GetRequiredService<TokenStore>();

        (await store.GetSessionAsync(Ct)).AccessToken.Should().Be("access-1");

        clock.Advance(TimeSpan.FromMinutes(8) + TimeSpan.FromSeconds(59)); // 61 s left: outside the margin
        (await store.GetSessionAsync(Ct)).AccessToken.Should().Be("access-1");
        fake.Count(HttpMethod.Post, RefreshPath).Should().Be(0);

        clock.Advance(TimeSpan.FromSeconds(31)); // 30 s left
        var second = await store.GetSessionAsync(Ct);
        second.AccessToken.Should().Be("access-2");
        second.RefreshToken.Should().Be("refresh-2");
        second.UserId.Should().Be(7);
        second.Role.Should().Be("Customer");
        second.AccessTokenExpiresAtUtc.Should().Be(At(TimeSpan.FromMinutes(20)));
        second.AccessTokenExpiresAtUtc.Kind.Should().Be(DateTimeKind.Utc);

        clock.Advance(TimeSpan.FromMinutes(10)); // 30 s left on access-2
        var third = await store.GetSessionAsync(Ct);
        third.AccessToken.Should().Be("access-3");
        third.RefreshToken.Should().Be("refresh-3");

        fake.Requests.Where(r => r.PathAndQuery == RefreshPath).Select(r => JsonField(r.Body, "refreshToken"))
            .Should().Equal("refresh-1", "refresh-2");
        fake.Count(HttpMethod.Post, LoginPath).Should().Be(1);
    }

    [Fact]
    public async Task RefreshUnauthorized_FallsBackToSingleLogin()
    {
        var clock = new ManualTimeProvider(Start);
        var fake = new FakeExodusHandler()
            .WithLogin(token: "access-1", refreshToken: "refresh-1", expiresAtUtc: At(TimeSpan.FromMinutes(10)))
            .WithLogin(token: "access-2", refreshToken: "refresh-2", expiresAtUtc: At(TimeSpan.FromHours(1)))
            .On(HttpMethod.Post, RefreshPath, HttpStatusCode.Unauthorized, new { detail = "Invalid refresh token" });
        using var sp = Build(fake, clock);
        var store = sp.GetRequiredService<TokenStore>();

        (await store.GetSessionAsync(Ct)).AccessToken.Should().Be("access-1");
        clock.Advance(TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(30));

        var renewed = await store.GetSessionAsync(Ct);

        renewed.AccessToken.Should().Be("access-2");
        renewed.RefreshToken.Should().Be("refresh-2");
        fake.Count(HttpMethod.Post, RefreshPath).Should().Be(1);
        fake.Count(HttpMethod.Post, LoginPath).Should().Be(2);

        (await store.GetSessionAsync(Ct)).Should().BeSameAs(renewed);
        fake.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task RenewAfterUnauthorized_AlreadyRenewedByAnotherCaller_ReturnsCurrentSession()
    {
        var fake = new FakeExodusHandler()
            .WithLogin(token: "access-1", refreshToken: "refresh-1", expiresAtUtc: At(TimeSpan.FromHours(1)))
            .On(HttpMethod.Post, RefreshPath, HttpStatusCode.OK, RefreshJson("access-2", "refresh-2", At(TimeSpan.FromHours(2))));
        using var sp = Build(fake, new ManualTimeProvider(Start));
        var store = sp.GetRequiredService<TokenStore>();
        await store.GetSessionAsync(Ct);

        var first = await store.RenewAfterUnauthorizedAsync("access-1", Ct);
        var second = await store.RenewAfterUnauthorizedAsync("access-1", Ct);

        first.AccessToken.Should().Be("access-2");
        second.Should().BeSameAs(first);
        fake.Count(HttpMethod.Post, RefreshPath).Should().Be(1);
        JsonField(fake.Requests.Single(r => r.PathAndQuery == RefreshPath).Body, "refreshToken").Should().Be("refresh-1");
    }

    [Fact]
    public async Task BadPassword_IsLatched_SingleLoginRequest()
    {
        var fake = new FakeExodusHandler().On(HttpMethod.Post, LoginPath, HttpStatusCode.Unauthorized, new { detail = "Invalid credentials" });
        using var sp = Build(fake, new ManualTimeProvider(Start));
        var store = sp.GetRequiredService<TokenStore>();

        var messages = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var error = await FluentActions.Awaiting(() => store.GetSessionAsync(Ct)).Should().ThrowAsync<ExodusAuthException>();
            error.Which.Permanent.Should().BeTrue();
            messages.Add(error.Which.Message);
        }

        var renew = await FluentActions.Awaiting(() => store.RenewAfterUnauthorizedAsync("access-1", Ct)).Should().ThrowAsync<ExodusAuthException>();
        messages.Add(renew.Which.Message);

        messages.Should().Equal(Enumerable.Repeat(BadPasswordMessage, 4));
        messages.Should().NotContain(m => m.Contains(Password));
        fake.Count(HttpMethod.Post, LoginPath).Should().Be(1);
    }

    [Fact]
    public async Task LockedAccount_DetailEndingWithDot_DoesNotProduceDoubleDot()
    {
        const string detail = "Account is locked. Try again in 5 minutes.";
        var fake = new FakeExodusHandler().On(HttpMethod.Post, LoginPath, HttpStatusCode.Unauthorized, new { detail });
        using var sp = Build(fake, new ManualTimeProvider(Start));
        var store = sp.GetRequiredService<TokenStore>();

        var error = await FluentActions.Awaiting(() => store.GetSessionAsync(Ct)).Should().ThrowAsync<ExodusAuthException>();

        error.Which.Message.Should().Contain(detail.TrimEnd('.')).And.NotContain("..").And.NotContain(Password);
    }

    [Fact]
    public async Task TwoFactorRequired_IsLatched()
    {
        var fake = new FakeExodusHandler().On(HttpMethod.Post, LoginPath, HttpStatusCode.OK, new
        {
            userId = 7,
            name = "Test User",
            email = "test@example.com",
            username = "testuser",
            role = "Customer",
            token = (string?)null,
            expiresAt = (DateTime?)null,
            refreshToken = (string?)null,
            refreshTokenExpiresAt = (DateTime?)null,
            twoFactorRequired = true,
        });
        using var sp = Build(fake, new ManualTimeProvider(Start));
        var store = sp.GetRequiredService<TokenStore>();

        for (var i = 0; i < 2; i++)
        {
            var error = await FluentActions.Awaiting(() => store.GetSessionAsync(Ct)).Should().ThrowAsync<ExodusAuthException>();
            error.Which.Permanent.Should().BeTrue();
            error.Which.Message.Should().Be("Bu hesapta iki adımlı doğrulama açık. MCP için 2FA kapalı bir hesap kullanın.");
        }

        fake.Count(HttpMethod.Post, LoginPath).Should().Be(1);
    }

    [Fact]
    public async Task RoleMismatch_IsLatched()
    {
        var fake = new FakeExodusHandler().WithLogin(role: "Customer", expiresAtUtc: At(TimeSpan.FromHours(1)));
        using var sp = Build(fake, new ManualTimeProvider(Start), ExodusProfile.Seller);
        var store = sp.GetRequiredService<TokenStore>();

        for (var i = 0; i < 2; i++)
        {
            var error = await FluentActions.Awaiting(() => store.GetSessionAsync(Ct)).Should().ThrowAsync<ExodusAuthException>();
            error.Which.Permanent.Should().BeTrue();
            error.Which.Message.Should().Be("EXODUS_PROFILE=seller ama hesabın rolü Customer. Doğru hesabı ya da profili kullanın.");
        }

        fake.Count(HttpMethod.Post, LoginPath).Should().Be(1);
    }

    [Fact]
    public async Task RateLimited429_IsNotLatched()
    {
        var fake = new FakeExodusHandler()
            .OnRaw(HttpMethod.Post, LoginPath, HttpStatusCode.TooManyRequests, null)
            .WithLogin(expiresAtUtc: At(TimeSpan.FromHours(1)));
        using var sp = Build(fake, new ManualTimeProvider(Start));
        var store = sp.GetRequiredService<TokenStore>();

        var error = await FluentActions.Awaiting(() => store.GetSessionAsync(Ct)).Should().ThrowAsync<ExodusAuthException>();
        error.Which.Permanent.Should().BeFalse();
        error.Which.Message.Should().Be("Exodus giriş rate limit'ine takıldı (dakikada 10 istek). Bir dakika bekleyip tekrar deneyin.");

        (await store.GetSessionAsync(Ct)).AccessToken.Should().Be("access-1");
        fake.Count(HttpMethod.Post, LoginPath).Should().Be(2);
    }

    [Fact]
    public async Task ConnectionRefused_IsNotLatched()
    {
        var fake = new FakeExodusHandler()
            .OnThrow(HttpMethod.Post, LoginPath, new HttpRequestException("Connection refused (exodus.test:80)"))
            .WithLogin(expiresAtUtc: At(TimeSpan.FromHours(1)));
        using var sp = Build(fake, new ManualTimeProvider(Start));
        var store = sp.GetRequiredService<TokenStore>();

        await FluentActions.Awaiting(() => store.GetSessionAsync(Ct)).Should().ThrowAsync<HttpRequestException>();

        (await store.GetSessionAsync(Ct)).AccessToken.Should().Be("access-1");
        fake.Count(HttpMethod.Post, LoginPath).Should().Be(2);
    }

    [Fact]
    public void AuthSession_ToString_DoesNotContainTokens()
    {
        var session = new AuthSession(7, "Customer", "secret-access", At(TimeSpan.FromHours(1)), "secret-refresh");

        var text = session.ToString();

        text.Should().Contain("Customer").And.NotContain("secret-access").And.NotContain("secret-refresh");
    }
}
