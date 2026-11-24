using System.Net;
using Exodus.Mcp.Core.Api;
using Exodus.Mcp.Core.Auth;
using Exodus.Mcp.Core.Configuration;
using Exodus.Mcp.Core.Tools;
using Exodus.Mcp.Tests.Fakes;
using FluentAssertions;
using ModelContextProtocol;

namespace Exodus.Mcp.Tests.Tools;

public class ToolErrorsTests
{
    private static readonly ExodusOptions Options = new()
    {
        BaseUrl = new Uri("http://exodus.test/"),
        Profile = ExodusProfile.Customer,
        Username = "ahmet@test.com",
        Password = "pw",
    };

    [Theory]
    [InlineData(400, "Stok yetersiz.", "Exodus isteği reddetti: Stok yetersiz.")]
    [InlineData(400, null, "Exodus isteği reddetti: (ayrıntı yok)")]
    [InlineData(401, "Token expired", "Exodus oturumu doğrulanamadı. MCP sunucusunu yeniden başlatmayı deneyin.")]
    [InlineData(403, "Not your listing.", "Bu işlem için yetkiniz yok: Not your listing.")]
    [InlineData(403, null, "Bu işlem için yetkiniz yok: (ayrıntı yok)")]
    [InlineData(404, "Listing not found.", "Kayıt bulunamadı: Listing not found.")]
    [InlineData(404, null, "Kayıt bulunamadı: (ayrıntı yok)")]
    [InlineData(409, "Already shipped.", "İşlem çakıştı: Already shipped.")]
    [InlineData(409, null, "İşlem çakıştı: (ayrıntı yok)")]
    [InlineData(422, "Quantity: must be > 0", "Exodus isteği reddetti: Quantity: must be > 0")]
    [InlineData(429, null, "Exodus rate limit'e takıldı, biraz bekleyip tekrar deneyin.")]
    [InlineData(500, "NullReferenceException at ...", "Exodus beklenmeyen bir hata döndürdü (HTTP 500).")]
    [InlineData(503, null, "Exodus beklenmeyen bir hata döndürdü (HTTP 503).")]
    public void ToolErrors_MapsStatusCodes(int status, string? detail, string expected)
    {
        var translated = ToolErrors.Translate(new ExodusApiException(status, detail), Options);

        translated.Should().BeOfType<McpException>();
        translated.Message.Should().StartWith(expected);
    }

    [Fact]
    public void ToolErrors_NotFoundAndForbidden_OverrideDefaults()
    {
        ToolErrors.Translate(new ExodusApiException(404, "nope"), Options, notFound: "Listing bulunamadı (id=42)")
            .Message.Should().Be("Listing bulunamadı (id=42)");
        ToolErrors.Translate(new ExodusApiException(403, "nope"), Options, forbidden: "Bu listing size ait değil.")
            .Message.Should().Be("Bu listing size ait değil.");
        // The overrides apply to their own status only.
        ToolErrors.Translate(new ExodusApiException(409, "dup"), Options, notFound: "x", forbidden: "y")
            .Message.Should().Be("İşlem çakıştı: dup");
    }

    [Fact]
    public void ToolErrors_HttpRequestException_MapsToUnreachable()
    {
        var translated = ToolErrors.Translate(new HttpRequestException("Connection refused (exodus.test:80)"), Options);

        translated.Should().BeOfType<McpException>();
        translated.Message.Should().Be("Exodus'a ulaşılamıyor (EXODUS_BASE_URL=http://exodus.test/). API çalışıyor mu?");
    }

    [Fact]
    public void ToolErrors_AuthException_UsesItsOwnMessage()
    {
        var translated = ToolErrors.Translate(new ExodusAuthException("Exodus girişi başarısız: Invalid credentials.", permanent: true), Options);

        translated.Should().BeOfType<McpException>();
        translated.Message.Should().Be("Exodus girişi başarısız: Invalid credentials.");
    }

    [Fact]
    public void ToolErrors_McpException_PassesThrough()
    {
        var original = new McpException("minPrice maxPrice'tan büyük olamaz.");

        ToolErrors.Translate(original, Options).Should().BeSameAs(original);
    }

    [Fact]
    public void ToolErrors_UnknownException_DoesNotLeakMessage()
    {
        var translated = ToolErrors.Translate(new InvalidOperationException("password=pw token=access-1 at /var/secret"), Options);

        translated.Should().BeOfType<McpException>();
        translated.Message.Should().Be("Beklenmeyen hata: InvalidOperationException");
        translated.Message.Should().NotContain("pw").And.NotContain("access-1").And.NotContain("secret");
    }

    [Fact]
    public async Task ToolErrors_HttpClientTimeout_MapsToTimeoutMessage()
    {
        var fake = new FakeExodusHandler().On(HttpMethod.Get, "/api/slow", HttpStatusCode.OK, new { ok = true }, delay: TimeSpan.FromSeconds(10));
        using var http = new HttpClient(fake) { BaseAddress = new Uri("http://exodus.test/"), Timeout = TimeSpan.FromMilliseconds(50) };

        var act = () => ToolErrors.RunAsync(Options, async () => (await http.GetAsync("api/slow")).StatusCode);

        (await act.Should().ThrowExactlyAsync<McpException>())
            .Which.Message.Should().Be("Exodus 30 saniye içinde yanıt vermedi (EXODUS_BASE_URL=http://exodus.test/).");
    }

    [Fact]
    public void ToolErrors_TaskCanceledWithoutCancelledToken_IsTimeout()
    {
        ToolErrors.Translate(new TaskCanceledException(), Options)
            .Message.Should().StartWith("Exodus 30 saniye içinde yanıt vermedi");
    }

    [Fact]
    public async Task ToolErrors_CallerCancellation_IsRethrown()
    {
        var fake = new FakeExodusHandler().On(HttpMethod.Get, "/api/slow", HttpStatusCode.OK, new { ok = true }, delay: TimeSpan.FromSeconds(10));
        using var http = new HttpClient(fake) { BaseAddress = new Uri("http://exodus.test/") };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = () => ToolErrors.RunAsync(Options, async () => (await http.GetAsync("api/slow", cts.Token)).StatusCode);

        (await act.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public void ToolErrors_Translate_ReturnsCallerCancellationAsIs()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = new OperationCanceledException(cts.Token);

        ToolErrors.Translate(cancelled, Options).Should().BeSameAs(cancelled);
    }

    [Fact]
    public async Task ToolErrors_RunAsync_SerializesResultWithToolJson()
    {
        var json = await ToolErrors.RunAsync(Options, () => Task.FromResult(new { OrderId = 5, Note = (string?)null, City = "İstanbul" }));

        json.Should().Be("""{"orderId":5,"city":"İstanbul"}""");
    }

    [Fact]
    public async Task ToolErrors_RunAsync_TranslatesFailures()
    {
        var act = () => ToolErrors.RunAsync<int>(Options, () => throw new ExodusApiException(404, "Order not found."), notFound: "Sipariş bulunamadı (17)");

        (await act.Should().ThrowExactlyAsync<McpException>()).Which.Message.Should().Be("Sipariş bulunamadı (17)");
    }
}
