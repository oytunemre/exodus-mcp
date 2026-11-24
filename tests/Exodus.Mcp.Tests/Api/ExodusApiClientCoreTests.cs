using System.Globalization;
using System.Net;
using Exodus.Mcp.Core.Api;
using Exodus.Mcp.Tests.Fakes;
using FluentAssertions;

namespace Exodus.Mcp.Tests.Api;

public class ExodusApiClientCoreTests
{
    private static async Task<ExodusApiException> Fail(HttpStatusCode status, string? body, string contentType = "application/json")
    {
        var handler = new FakeExodusHandler().OnRaw(HttpMethod.Get, "/api/x", status, body, contentType);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        using var response = await http.GetAsync("api/x");
        return await ExodusApiException.FromResponseAsync(response, CancellationToken.None);
    }

    [Fact]
    public async Task ParsesProblemDetailsDetail()
    {
        var ex = await Fail(HttpStatusCode.NotFound, """{"status":404,"title":"Request failed","detail":"Listing not found."}""");
        ex.StatusCode.Should().Be(404);
        ex.Detail.Should().Be("Listing not found.");
    }

    [Fact]
    public async Task ParsesMessageBody()
    {
        var ex = await Fail(HttpStatusCode.BadRequest, """{"message":"Shipment yok. Önce ship yap."}""");
        ex.Detail.Should().Be("Shipment yok. Önce ship yap.");
    }

    [Fact]
    public async Task ParsesValidationErrors()
    {
        var ex = await Fail(HttpStatusCode.BadRequest, """{"title":"One or more validation errors occurred.","errors":{"Quantity":["must be > 0"]}}""");
        ex.Detail.Should().Contain("Quantity: must be > 0");
    }

    [Fact]
    public async Task EmptyBody429()
    {
        var ex = await Fail(HttpStatusCode.TooManyRequests, null);
        ex.StatusCode.Should().Be(429);
        ex.Detail.Should().BeNull();
    }

    [Fact]
    public async Task NonJsonBody()
    {
        var ex = await Fail(HttpStatusCode.BadGateway, "<html>bad</html>", "text/html");
        ex.Detail.Should().BeNull();
    }

    [Fact]
    public void QueryString_SkipsNullsAndEscapesUtf8()
    {
        QueryString.Build("api/listings", ("search", "kulaklık"), ("category", null))
            .Should().Be("api/listings?search=kulakl%C4%B1k");
    }

    [Fact]
    public void QueryString_UsesInvariantCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            QueryString.Build("api/listings", ("minPrice", 1500.5m)).Should().Be("api/listings?minPrice=1500.5");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
