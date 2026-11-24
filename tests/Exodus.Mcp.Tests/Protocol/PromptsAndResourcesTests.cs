using System.Net;
using System.Text.Json;
using Exodus.Mcp.Core.Configuration;
using Exodus.Mcp.Tests.Fakes;
using Exodus.Mcp.Tests.Infrastructure;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Exodus.Mcp.Tests.Protocol;

public class PromptsAndResourcesTests
{
    private const string CategoryPath = "/api/category/tree";

    private static FakeExodusHandler Fake(string role = "Customer") => new FakeExodusHandler().WithLogin(role: role);

    private static string TextOf(ModelContextProtocol.Client.McpClient client, GetPromptResult result)
    {
        result.Messages.Should().ContainSingle();
        result.Messages[0].Role.Should().Be(Role.User);
        return ((TextContentBlock)result.Messages[0].Content).Text;
    }

    [Fact]
    public async Task Prompts_ArePerProfile()
    {
        await using var customer = await McpTestHost.StartAsync(ExodusProfile.Customer, Fake());
        await using var seller = await McpTestHost.StartAsync(ExodusProfile.Seller, Fake("Seller"));

        (await customer.Client.ListPromptsAsync()).Select(p => p.Name).Should().Equal("shopping_assistant");
        (await seller.Client.ListPromptsAsync()).Select(p => p.Name).Should().Equal("seller_daily_report");
    }

    [Fact]
    public async Task SellerDailyReport_MentionsToolsAndDates()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 3, 10, 23, 30, 0, TimeSpan.Zero));
        await using var host = await McpTestHost.StartAsync(ExodusProfile.Seller, Fake("Seller"), time);

        var text = TextOf(host.Client, await host.Client.GetPromptAsync("seller_daily_report"));

        text.Should().Contain("list_my_listings").And.Contain("list_seller_orders").And.Contain("sales_summary");
        text.Should().Contain("stockFilter=\"low\"").And.Contain("stockFilter=\"out\"").And.Contain("status=\"Pending\"");
        text.Should().Contain("2026-03-10").And.Contain("2026-03-04");
    }

    [Fact]
    public async Task ShoppingAssistant_RequiresConfirmationBeforePlaceOrder()
    {
        await using var host = await McpTestHost.StartAsync(ExodusProfile.Customer, Fake());

        var plain = TextOf(host.Client, await host.Client.GetPromptAsync("shopping_assistant"));
        var withGoal = TextOf(host.Client, await host.Client.GetPromptAsync("shopping_assistant",
            new Dictionary<string, object?> { ["goal"] = "kablosuz kulaklık" }));

        plain.Should().Contain("place_order").And.Contain("onay").And.Contain("list_my_addresses");
        plain.Should().NotContain("kablosuz kulaklık");
        withGoal.Should().Contain("kablosuz kulaklık").And.Contain("onay");
    }

    [Theory]
    [InlineData(ExodusProfile.Customer)]
    [InlineData(ExodusProfile.Seller)]
    public async Task CategoriesResource_ReturnsJson_BothProfiles(ExodusProfile profile)
    {
        var fake = Fake(profile == ExodusProfile.Seller ? "Seller" : "Customer").OnRaw(HttpMethod.Get, CategoryPath, HttpStatusCode.OK,
            """[{"id":1,"name":"Elektronik","productCount":3,"children":[{"id":2,"name":"Telefon","productCount":1,"children":[]}]}]""");
        await using var host = await McpTestHost.StartAsync(profile, fake);

        var resources = await host.Client.ListResourcesAsync();
        resources.Should().ContainSingle(r => r.Uri == "exodus://categories" && r.MimeType == "application/json");

        var read = await host.Client.ReadResourceAsync("exodus://categories");
        var content = read.Contents.Should().ContainSingle().Which.Should().BeOfType<TextResourceContents>().Subject;
        content.MimeType.Should().Be("application/json");

        using var json = JsonDocument.Parse(content.Text);
        json.RootElement[0].GetProperty("name").GetString().Should().Be("Elektronik");
        json.RootElement[0].GetProperty("children")[0].GetProperty("id").GetInt32().Should().Be(2);

        if (profile == ExodusProfile.Customer)
        {
            // Same JSON as the list_categories tool (the seller profile has no such tool).
            (await host.CallAsync("list_categories")).Text.Should().Be(content.Text);
        }
    }

    [Fact]
    public async Task CategoriesResource_ApiFailure_SurfacesTurkishMessage()
    {
        var fake = Fake().OnRaw(HttpMethod.Get, CategoryPath, HttpStatusCode.InternalServerError, "boom");
        await using var host = await McpTestHost.StartAsync(ExodusProfile.Customer, fake);

        var act = async () => await host.Client.ReadResourceAsync("exodus://categories");

        var error = await act.Should().ThrowAsync<ModelContextProtocol.McpException>();
        error.Which.Message.Should().NotContain("boom").And.Contain("Exodus beklenmeyen bir hata döndürdü (HTTP 500)");
    }
}
