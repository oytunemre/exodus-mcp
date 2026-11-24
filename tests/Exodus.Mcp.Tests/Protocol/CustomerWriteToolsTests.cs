using System.Net;
using System.Text.Json;
using Exodus.Mcp.Core.Configuration;
using Exodus.Mcp.Tests.Fakes;
using Exodus.Mcp.Tests.Infrastructure;
using FluentAssertions;

namespace Exodus.Mcp.Tests.Protocol;

public class CustomerWriteToolsTests
{
    private static readonly object Cart = new
    {
        cartId = 1, userId = 7, cartTotal = 200m,
        items = new[] { new { cartItemId = 11, listingId = 3, productId = 2, productName = "Kulaklık X", sellerId = 1, unitPrice = 100m, quantity = 2, lineTotal = 200m } },
    };

    private static object Order(string status = "Pending") => new
    {
        id = 9, orderNumber = "ORD-2025-0009", status, totalAmount = 250m, currency = "TRY", shippingAddress = "Ev",
        createdAt = "2025-01-02T10:00:00",
        sellerOrders = new[] { new { id = 4, sellerId = 1, sellerName = "Mağaza A", status, total = 250m, items = new[] { new { productName = "Kulaklık X", quantity = 2, unitPrice = 100m } } } },
    };

    private static async Task<(McpTestHost Host, FakeExodusHandler Fake)> StartAsync(Action<FakeExodusHandler> setup)
    {
        var fake = new FakeExodusHandler().WithLogin(userId: 7);
        setup(fake);
        return (await McpTestHost.StartAsync(ExodusProfile.Customer, fake), fake);
    }

    [Fact]
    public async Task AddToCart_SendsSessionUserId()
    {
        var (host, fake) = await StartAsync(f => f.On(HttpMethod.Post, "/api/cart/add", HttpStatusCode.OK, Cart));
        await using var _ = host;

        var outcome = await host.CallAsync("add_to_cart", new() { ["listingId"] = 3, ["quantity"] = 2 });

        outcome.IsError.Should().BeFalse(outcome.Text);
        fake.Requests.Single(r => r.PathAndQuery == "/api/cart/add").Body.Should().Be("{\"userId\":7,\"listingId\":3,\"quantity\":2}");
        using var json = JsonDocument.Parse(outcome.Text);
        json.RootElement.GetProperty("total").GetDecimal().Should().Be(200m);
        var item = json.RootElement.GetProperty("items")[0];
        item.GetProperty("cartItemId").GetInt32().Should().Be(11);
        item.GetProperty("lineTotal").GetDecimal().Should().Be(200m);

        var tools = await host.Client.ListToolsAsync();
        var schema = tools.Single(t => t.Name == "add_to_cart").ProtocolTool.InputSchema;
        schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("listingId", "quantity");
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo("listingId");
    }

    [Fact]
    public async Task UpdateCartItem_ZeroQuantity_IsErrorWithoutRequest()
    {
        var (host, fake) = await StartAsync(_ => { });
        await using var _ = host;

        var outcome = await host.CallAsync("update_cart_item", new() { ["cartItemId"] = 11, ["quantity"] = 0 });

        outcome.IsError.Should().BeTrue();
        outcome.Text.Should().Contain("remove_from_cart");
        fake.Requests.Should().NotContain(r => r.PathAndQuery.StartsWith("/api/cart"));
    }

    [Fact]
    public async Task UpdateCartItem_PutsQuantityToUserItemPath_AndReturnsCart()
    {
        var (host, fake) = await StartAsync(f => f.On(HttpMethod.Put, "/api/cart/7/item/11", HttpStatusCode.OK, Cart));
        await using var _ = host;

        var outcome = await host.CallAsync("update_cart_item", new() { ["cartItemId"] = 11, ["quantity"] = 3 });

        outcome.IsError.Should().BeFalse(outcome.Text);
        fake.Requests.Single(r => r.Method == "PUT" && r.PathAndQuery == "/api/cart/7/item/11").Body.Should().Be("{\"quantity\":3}");
        using var json = JsonDocument.Parse(outcome.Text);
        json.RootElement.GetProperty("total").GetDecimal().Should().Be(200m);
        json.RootElement.GetProperty("items")[0].GetProperty("cartItemId").GetInt32().Should().Be(11);
    }

    [Fact]
    public async Task RemoveFromCart_UsesUserIdInPath()
    {
        var (host, fake) = await StartAsync(f => f.On(HttpMethod.Delete, "/api/cart/7/item/11", HttpStatusCode.OK, Cart));
        await using var _ = host;

        var outcome = await host.CallAsync("remove_from_cart", new() { ["cartItemId"] = 11 });

        outcome.IsError.Should().BeFalse(outcome.Text);
        fake.Count(HttpMethod.Delete, "/api/cart/7/item/11").Should().Be(1);
    }

    [Fact]
    public async Task PlaceOrder_ChecksOutThenCreatesCodIntent()
    {
        var (host, fake) = await StartAsync(f => f
            .On(HttpMethod.Post, "/api/order/checkout", HttpStatusCode.Created, Order())
            .On(HttpMethod.Post, "/api/payment/intents", HttpStatusCode.Created, new { id = 5, orderId = 9, method = "CashOnDelivery", status = "Created" }));
        await using var _ = host;

        var outcome = await host.CallAsync("place_order", new() { ["shippingAddressId"] = 2, ["note"] = "Zili çalın" });

        outcome.IsError.Should().BeFalse(outcome.Text);
        var paths = fake.Requests.Select(r => r.PathAndQuery).Where(p => p.StartsWith("/api/order") || p.StartsWith("/api/payment")).ToList();
        paths.Should().Equal("/api/order/checkout", "/api/payment/intents");
        var checkout = fake.Requests.Single(r => r.PathAndQuery == "/api/order/checkout").Body!;
        checkout.Should().Contain("\"shippingAddressId\":2").And.NotContain("couponCode");
        using (var body = JsonDocument.Parse(checkout))
        {
            body.RootElement.GetProperty("customerNote").GetString().Should().Be("Zili çalın");
        }
        fake.Requests.Single(r => r.PathAndQuery == "/api/payment/intents").Body.Should().Contain("\"method\":\"CashOnDelivery\"");
        using var json = JsonDocument.Parse(outcome.Text);
        json.RootElement.GetProperty("orderNumber").GetString().Should().Be("ORD-2025-0009");
        json.RootElement.GetProperty("sellerOrders")[0].GetProperty("sellerName").GetString().Should().Be("Mağaza A");
        json.RootElement.GetProperty("payment").GetProperty("method").GetString().Should().Be("CashOnDelivery");
        json.RootElement.GetProperty("payment").GetProperty("status").GetString().Should().Be("Created");
    }

    [Fact]
    public async Task PlaceOrder_PaymentFailure_ReportsCreatedOrder_NoRetry()
    {
        var (host, fake) = await StartAsync(f => f
            .On(HttpMethod.Post, "/api/order/checkout", HttpStatusCode.Created, Order())
            .On(HttpMethod.Post, "/api/payment/intents", HttpStatusCode.InternalServerError, new { detail = "boom" }));
        await using var _ = host;

        var outcome = await host.CallAsync("place_order", new() { ["shippingAddressId"] = 2 });

        outcome.IsError.Should().BeTrue();
        outcome.Text.Should().Contain("ORD-2025-0009").And.Contain("tekrar oluşturmayın").And.NotContain("..");
        fake.Count(HttpMethod.Post, "/api/order/checkout").Should().Be(1);
    }

    [Fact]
    public async Task PlaceOrder_SchemaHasNoCardOrCouponFields()
    {
        var (host, _) = await StartAsync(_ => { });
        await using var _ = host;

        var tools = await host.Client.ListToolsAsync();
        var tool = tools.Single(t => t.Name == "place_order");

        tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("shippingAddressId", "billingAddressId", "note");
        tool.Description.Should().Contain("Kart bilgisi istenmez").And.Contain("onaylamadan");
    }

    [Fact]
    public async Task CancelOrder_DefaultReason_AndIsDestructive()
    {
        var (host, fake) = await StartAsync(f => f.On(HttpMethod.Post, "/api/order/9/cancel", HttpStatusCode.OK, Order("Cancelled")));
        await using var _ = host;

        var outcome = await host.CallAsync("cancel_order", new() { ["orderId"] = 9 });

        outcome.IsError.Should().BeFalse(outcome.Text);
        fake.Requests.Single(r => r.PathAndQuery == "/api/order/9/cancel").Body.Should().Contain("\"reason\":\"CustomerRequest\"");
        using var json = JsonDocument.Parse(outcome.Text);
        json.RootElement.GetProperty("status").GetString().Should().Be("Cancelled");
        json.RootElement.GetProperty("orderId").GetInt32().Should().Be(9);

        var tools = await host.Client.ListToolsAsync();
        tools.Single(t => t.Name == "cancel_order").ProtocolTool.Annotations!.DestructiveHint.Should().BeTrue();
    }

    [Fact]
    public async Task CancelOrder_ExodusRejects_ShowsDetail()
    {
        var (host, _) = await StartAsync(f => f.On(HttpMethod.Post, "/api/order/9/cancel", HttpStatusCode.BadRequest,
            new { detail = "Kargoya verilmiş siparişler iptal edilemez." }));
        await using var _ = host;

        var outcome = await host.CallAsync("cancel_order", new() { ["orderId"] = 9, ["reason"] = "Other" });

        outcome.IsError.Should().BeTrue();
        outcome.Text.Should().Contain("Kargoya verilmiş siparişler iptal edilemez.");
    }
}
