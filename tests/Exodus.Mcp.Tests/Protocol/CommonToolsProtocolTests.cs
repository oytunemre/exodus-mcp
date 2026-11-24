using System.Net;
using System.Text.Json;
using Exodus.Mcp.Core.Configuration;
using Exodus.Mcp.Tests.Fakes;
using Exodus.Mcp.Tests.Infrastructure;
using FluentAssertions;

namespace Exodus.Mcp.Tests.Protocol;

public class CommonToolsProtocolTests
{
    private const string MePath = "/api/auth/me";

    [Fact]
    public async Task Whoami_ReturnsProfileAndUser()
    {
        var fake = new FakeExodusHandler()
            .WithLogin(token: "access-1", refreshToken: "refresh-1")
            .OnRaw(HttpMethod.Get, MePath, HttpStatusCode.OK, """{"userId":"7","username":"ahmet","email":"ahmet@test.com","role":"Customer"}""");
        await using var host = await McpTestHost.StartAsync(ExodusProfile.Customer, fake);

        var outcome = await host.CallAsync("whoami");

        outcome.IsError.Should().BeFalse(outcome.Text);
        outcome.Text.Should().Contain("\"profile\":\"Customer\"");
        outcome.Text.Should().NotContain("pw").And.NotContain("access-1").And.NotContain("refresh-1");

        using var json = JsonDocument.Parse(outcome.Text);
        var root = json.RootElement;
        root.GetProperty("userId").GetString().Should().Be("7");
        root.GetProperty("username").GetString().Should().Be("ahmet");
        root.GetProperty("email").GetString().Should().Be("ahmet@test.com");
        root.GetProperty("role").GetString().Should().Be("Customer");
        root.GetProperty("exodusBaseUrl").GetString().Should().Be("http://exodus.test/");
        fake.Requests.Single(r => r.PathAndQuery == MePath).Authorization.Should().Be("Bearer access-1");
    }

    [Fact]
    public async Task Whoami_IsReadOnlyIdempotentClosedWorld()
    {
        await using var host = await McpTestHost.StartAsync(ExodusProfile.Customer, new FakeExodusHandler());

        var tools = await host.Client.ListToolsAsync();

        var annotations = tools.Single(t => t.Name == "whoami").ProtocolTool.Annotations;
        annotations.Should().NotBeNull();
        annotations!.ReadOnlyHint.Should().BeTrue();
        annotations.DestructiveHint.Should().BeFalse();
        annotations.IdempotentHint.Should().BeTrue();
        annotations.OpenWorldHint.Should().BeFalse();
    }

    [Fact]
    public async Task ConnectionRefused_MapsToUnreachableMessage()
    {
        var fake = new FakeExodusHandler()
            .OnThrow(HttpMethod.Post, "/api/auth/login", new HttpRequestException("Connection refused (exodus.test:80)"));
        await using var host = await McpTestHost.StartAsync(ExodusProfile.Customer, fake);

        var outcome = await host.CallAsync("whoami");

        outcome.IsError.Should().BeTrue();
        outcome.Text.Should().Contain("Exodus'a ulaşılamıyor (EXODUS_BASE_URL=http://exodus.test/)");
    }

    [Fact]
    public async Task ServerError_MapsToHttpStatusMessage()
    {
        var fake = new FakeExodusHandler()
            .WithLogin()
            .On(HttpMethod.Get, MePath, HttpStatusCode.InternalServerError, new { detail = "NullReferenceException in AuthController" });
        await using var host = await McpTestHost.StartAsync(ExodusProfile.Customer, fake);

        var outcome = await host.CallAsync("whoami");

        outcome.IsError.Should().BeTrue();
        outcome.Text.Should().Contain("Exodus beklenmeyen bir hata döndürdü (HTTP 500).");
        outcome.Text.Should().NotContain("NullReferenceException");
    }

    [Fact]
    public async Task ToolOutput_DoesNotEscapeTurkish()
    {
        var fake = new FakeExodusHandler()
            .WithLogin()
            .OnRaw(HttpMethod.Get, MePath, HttpStatusCode.OK, """{"userId":"7","username":"Işıl","email":"isil@test.com","role":"Customer"}""");
        await using var host = await McpTestHost.StartAsync(ExodusProfile.Customer, fake);

        var outcome = await host.CallAsync("whoami");

        outcome.IsError.Should().BeFalse(outcome.Text);
        outcome.Text.Should().Contain("\"username\":\"Işıl\"");
    }

    [Theory]
    [InlineData(ExodusProfile.Customer, "kapıda ödeme")]
    [InlineData(ExodusProfile.Seller, "takip")]
    public async Task Server_AnnouncesIdentityAndProfileInstructions(ExodusProfile profile, string instructionsHint)
    {
        await using var host = await McpTestHost.StartAsync(profile, new FakeExodusHandler());

        var info = host.Client.ServerInfo;
        info.Should().NotBeNull();
        info!.Name.Should().Be("exodus-mcp");
        info.Version.Should().Be("0.1.0");
        host.Client.ServerInstructions.Should().Contain(instructionsHint);
        var tools = await host.Client.ListToolsAsync();
        tools.Select(t => t.Name).Should().Contain("whoami");
    }

    [Fact]
    public async Task Whoami_SellerProfile_ReportsSeller()
    {
        var fake = new FakeExodusHandler()
            .WithLogin(role: "Seller")
            .OnRaw(HttpMethod.Get, MePath, HttpStatusCode.OK, """{"userId":"9","username":"zeynep","email":"zeynep@test.com","role":"Seller"}""");
        await using var host = await McpTestHost.StartAsync(ExodusProfile.Seller, fake);

        var outcome = await host.CallAsync("whoami");

        outcome.IsError.Should().BeFalse(outcome.Text);
        outcome.Text.Should().Contain("\"profile\":\"Seller\"").And.Contain("\"role\":\"Seller\"");
        fake.Requests.Single(r => r.PathAndQuery == "/api/auth/login").Body.Should().Contain("zeynep@test.com");
    }
}
