using Exodus.Mcp.Core.Configuration;
using Exodus.Mcp.Tests.Fakes;
using Exodus.Mcp.Tests.Infrastructure;
using FluentAssertions;

namespace Exodus.Mcp.Tests.Protocol;

public class RegistrationContractTests
{
    private static readonly string[] CustomerTools =
    [
        "whoami", "search_listings", "get_listing", "list_categories", "get_cart", "add_to_cart", "update_cart_item",
        "remove_from_cart", "list_my_addresses", "place_order", "list_my_orders", "get_order", "cancel_order",
    ];

    private static readonly string[] SellerTools =
    [
        "whoami", "list_my_listings", "create_listing", "update_listing", "update_stock", "list_seller_orders",
        "get_seller_order", "confirm_order", "ship_order", "mark_delivered", "sales_summary",
    ];

    // Task 6 table: name -> (ReadOnly, Destructive, Idempotent). OpenWorld is false everywhere.
    private static readonly Dictionary<string, (bool ReadOnly, bool Destructive, bool Idempotent)> Hints = Build();

    private static Dictionary<string, (bool, bool, bool)> Build()
    {
        var map = new Dictionary<string, (bool, bool, bool)>();
        foreach (var n in new[] { "whoami", "search_listings", "get_listing", "list_categories", "get_cart", "list_my_addresses",
                     "list_my_orders", "get_order", "list_my_listings", "list_seller_orders", "get_seller_order", "sales_summary" })
        {
            map[n] = (true, false, true);
        }

        foreach (var n in new[] { "update_cart_item", "remove_from_cart", "update_listing", "update_stock", "confirm_order" })
        {
            map[n] = (false, false, true);
        }

        foreach (var n in new[] { "add_to_cart", "place_order", "create_listing", "ship_order", "mark_delivered" })
        {
            map[n] = (false, false, false);
        }

        map["cancel_order"] = (false, true, false);
        return map;
    }

    public static TheoryData<ExodusProfile> Profiles => new() { ExodusProfile.Customer, ExodusProfile.Seller };

    private static Task<McpTestHost> Start(ExodusProfile profile)
        => McpTestHost.StartAsync(profile, new FakeExodusHandler());

    [Fact]
    public async Task CustomerProfile_ExposesExactlyCustomerTools()
    {
        await using var host = await Start(ExodusProfile.Customer);

        var names = (await host.Client.ListToolsAsync()).Select(t => t.Name).ToList();

        names.Should().BeEquivalentTo(CustomerTools).And.HaveCount(13);
        names.Should().NotContain(SellerTools.Except(CustomerTools));
    }

    [Fact]
    public async Task SellerProfile_ExposesExactlySellerTools()
    {
        await using var host = await Start(ExodusProfile.Seller);

        var names = (await host.Client.ListToolsAsync()).Select(t => t.Name).ToList();

        names.Should().BeEquivalentTo(SellerTools).And.HaveCount(11);
        names.Should().NotContain(CustomerTools.Except(SellerTools));
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task EveryTool_HasExplicitAnnotationsMatchingTable(ExodusProfile profile)
    {
        await using var host = await Start(profile);

        foreach (var tool in await host.Client.ListToolsAsync())
        {
            var a = tool.ProtocolTool.Annotations;
            a.Should().NotBeNull(tool.Name);
            Hints.Should().ContainKey(tool.Name);
            var (readOnly, destructive, idempotent) = Hints[tool.Name];
            a!.ReadOnlyHint.Should().Be(readOnly, tool.Name);
            a.DestructiveHint.Should().Be(destructive, tool.Name);
            a.IdempotentHint.Should().Be(idempotent, tool.Name);
            a.OpenWorldHint.Should().BeFalse(tool.Name);
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public async Task EveryToolAndParameter_HasDescription(ExodusProfile profile)
    {
        await using var host = await Start(profile);

        foreach (var tool in await host.Client.ListToolsAsync())
        {
            tool.Description.Should().NotBeNullOrWhiteSpace(tool.Name);
            if (!tool.JsonSchema.TryGetProperty("properties", out var props))
            {
                continue;
            }

            foreach (var prop in props.EnumerateObject())
            {
                prop.Value.TryGetProperty("description", out var d).Should().BeTrue($"{tool.Name}.{prop.Name} needs a description");
                d.GetString().Should().NotBeNullOrWhiteSpace($"{tool.Name}.{prop.Name}");
            }
        }
    }

    [Fact]
    public async Task OptionalParameters_AreNotRequired()
    {
        await using var host = await Start(ExodusProfile.Customer);
        var tools = await host.Client.ListToolsAsync();

        var search = tools.Single(t => t.Name == "search_listings").JsonSchema;
        (!search.TryGetProperty("required", out var req) || req.GetArrayLength() == 0).Should().BeTrue();

        var get = tools.Single(t => t.Name == "get_listing").JsonSchema;
        get.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().Equal("listingId");
    }

    [Fact]
    public async Task ServerInstructions_AreProfileSpecific()
    {
        await using var customer = await Start(ExodusProfile.Customer);
        await using var seller = await Start(ExodusProfile.Seller);

        var c = customer.Client.ServerInstructions;
        var s = seller.Client.ServerInstructions;

        c.Should().Contain("onay").And.Contain("Kart bilgisi");
        c.Should().NotContain("taşıyıcı").And.NotContain("takip numarası");
        s.Should().Contain("taşıyıcı").And.Contain("takip numarası");
        s.Should().NotContain("Kart bilgisi").And.NotContain("açık onay");
    }
}
