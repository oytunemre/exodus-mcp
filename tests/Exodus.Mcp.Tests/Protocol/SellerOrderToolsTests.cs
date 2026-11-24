using System.Net;
using System.Text.Json;
using Exodus.Mcp.Core.Configuration;
using Exodus.Mcp.Tests.Fakes;
using Exodus.Mcp.Tests.Infrastructure;
using FluentAssertions;

namespace Exodus.Mcp.Tests.Protocol;

public class SellerOrderToolsTests
{
    private static object Order(int id = 11, string status = "Confirmed", string createdAt = "2026-11-10T08:00:00", bool shipped = false) => new
    {
        id, orderId = 4, orderNumber = $"ORD-{id}", createdAt, sellerId = 3, sellerName = "Mağaza", status, total = 100m,
        items = new[] { new { id = 1, listingId = 5, productId = 2, productName = "Kulaklık X", quantity = 2, unitPrice = 50m, totalPrice = 100m } },
        shipment = shipped ? new { id = 9, carrier = "Yurtiçi", trackingNumber = "TR123", shippedAt = "2026-11-11T09:00:00", deliveredAt = (string?)null } : null,
    };

    private static async Task<(McpTestHost Host, FakeExodusHandler Fake)> StartAsync(Action<FakeExodusHandler> setup, TimeProvider? time = null)
    {
        var fake = new FakeExodusHandler().WithLogin(role: "Seller", userId: 3);
        setup(fake);
        return (await McpTestHost.StartAsync(ExodusProfile.Seller, fake, time), fake);
    }

    private static int ApiRequests(FakeExodusHandler fake) => fake.Requests.Count(r => r.PathAndQuery != "/api/auth/login");

    [Fact]
    public async Task ListSellerOrders_MapsOrderNumberAndHasMore()
    {
        var page = Enumerable.Range(1, 20).Select(i => Order(i)).ToArray();
        var (host, fake) = await StartAsync(f => f.On(HttpMethod.Get, "/api/seller/orders?status=Pending&page=1&pageSize=20", HttpStatusCode.OK, page));
        await using var scope = host;

        var outcome = await host.CallAsync("list_seller_orders", new() { ["status"] = "pending" });

        outcome.IsError.Should().BeFalse(outcome.Text);
        using var json = JsonDocument.Parse(outcome.Text);
        json.RootElement.GetProperty("page").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("hasMore").GetBoolean().Should().BeTrue();
        var item = json.RootElement.GetProperty("items")[0];
        item.GetProperty("sellerOrderId").GetInt32().Should().Be(1);
        item.GetProperty("orderNumber").GetString().Should().Be("ORD-1");
        item.GetProperty("status").GetString().Should().Be("Confirmed");
        item.GetProperty("total").GetDecimal().Should().Be(100m);
        item.GetProperty("itemCount").GetInt32().Should().Be(2); // sum of quantities (one line, quantity 2)
    }

    [Fact]
    public async Task ListSellerOrders_ShortPage_HasMoreFalse_AndBadStatusRejectedWithoutRequest()
    {
        var (host, fake) = await StartAsync(f => f.On(HttpMethod.Get, "/api/seller/orders?page=1&pageSize=20", HttpStatusCode.OK, new[] { Order() }));
        await using var scope = host;

        var bad = await host.CallAsync("list_seller_orders", new() { ["status"] = "Bogus" });
        bad.IsError.Should().BeTrue();
        ApiRequests(fake).Should().Be(0);

        var ok = await host.CallAsync("list_seller_orders");
        using var json = JsonDocument.Parse(ok.Text);
        json.RootElement.GetProperty("hasMore").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetSellerOrder_FetchesTimelineOnlyWhenShipped()
    {
        var (host, fake) = await StartAsync(f => f
            .On(HttpMethod.Get, "/api/seller/orders/11", HttpStatusCode.OK, Order(11, "Confirmed"))
            .On(HttpMethod.Get, "/api/seller/orders/12", HttpStatusCode.OK, Order(12, "Shipped", shipped: true))
            .On(HttpMethod.Get, "/api/shipments/seller-orders/12/timeline", HttpStatusCode.OK,
                new[] { new { id = 1, shipmentId = 9, status = "Shipped", payloadJson = "{}", createdAt = "2026-11-11T09:00:00" } }));
        await using var scope = host;

        var plain = await host.CallAsync("get_seller_order", new() { ["sellerOrderId"] = 11 });
        plain.IsError.Should().BeFalse(plain.Text);
        fake.Requests.Select(r => r.PathAndQuery).Should().NotContain(p => p.Contains("timeline"));
        using (var json = JsonDocument.Parse(plain.Text))
        {
            json.RootElement.TryGetProperty("shipment", out _).Should().BeFalse();
            json.RootElement.TryGetProperty("timeline", out _).Should().BeFalse();
            json.RootElement.GetProperty("items")[0].GetProperty("listingId").GetInt32().Should().Be(5);
        }

        var shipped = await host.CallAsync("get_seller_order", new() { ["sellerOrderId"] = 12 });
        shipped.IsError.Should().BeFalse(shipped.Text);
        fake.Count(HttpMethod.Get, "/api/shipments/seller-orders/12/timeline").Should().Be(1);
        using var sj = JsonDocument.Parse(shipped.Text);
        sj.RootElement.GetProperty("shipment").GetProperty("trackingNumber").GetString().Should().Be("TR123");
        var step = sj.RootElement.GetProperty("timeline")[0];
        step.GetProperty("status").GetString().Should().Be("Shipped");
        step.GetProperty("at").GetString().Should().StartWith("2026-11-11T09:00:00");
    }

    [Fact]
    public async Task GetSellerOrder_NotFound_TurkishMessage()
    {
        var (host, _) = await StartAsync(f => f.On(HttpMethod.Get, "/api/seller/orders/99", HttpStatusCode.NotFound, new { message = "x" }));
        await using var scope = host;

        var outcome = await host.CallAsync("get_seller_order", new() { ["sellerOrderId"] = 99 });

        outcome.IsError.Should().BeTrue();
        outcome.Text.Should().Contain("Satıcı siparişi bulunamadı (id=99)");
    }

    [Fact]
    public async Task ConfirmOrder_RefetchesOrder()
    {
        var (host, fake) = await StartAsync(f => f
            .On(HttpMethod.Post, "/api/seller/orders/11/confirm", HttpStatusCode.OK, new { message = "Order confirmed successfully" })
            .On(HttpMethod.Get, "/api/seller/orders/11", HttpStatusCode.OK, Order(11, "Confirmed")));
        await using var scope = host;

        var outcome = await host.CallAsync("confirm_order", new() { ["sellerOrderId"] = 11 });

        outcome.IsError.Should().BeFalse(outcome.Text);
        fake.Requests.Where(r => r.PathAndQuery != "/api/auth/login").Select(r => $"{r.Method} {r.PathAndQuery}")
            .Should().Equal("POST /api/seller/orders/11/confirm", "GET /api/seller/orders/11");
        using var json = JsonDocument.Parse(outcome.Text);
        json.RootElement.GetProperty("status").GetString().Should().Be("Confirmed");
    }

    [Fact]
    public async Task ShipOrder_RequiresCarrierAndTracking()
    {
        var (host, fake) = await StartAsync(_ => { });
        await using var scope = host;

        var noCarrier = await host.CallAsync("ship_order", new() { ["sellerOrderId"] = 11, ["carrier"] = " ", ["trackingNumber"] = "TR1" });
        var noTracking = await host.CallAsync("ship_order", new() { ["sellerOrderId"] = 11, ["carrier"] = "Yurtiçi", ["trackingNumber"] = "" });

        noCarrier.IsError.Should().BeTrue();
        noTracking.IsError.Should().BeTrue();
        ApiRequests(fake).Should().Be(0);
    }

    [Fact]
    public async Task ShipOrder_SendsBodyAndRefetches()
    {
        var (host, fake) = await StartAsync(f => f
            .On(HttpMethod.Post, "/api/seller/orders/12/ship", HttpStatusCode.OK, new { message = "Order marked as shipped" })
            .On(HttpMethod.Get, "/api/seller/orders/12", HttpStatusCode.OK, Order(12, "Shipped", shipped: true))
            .On(HttpMethod.Get, "/api/shipments/seller-orders/12/timeline", HttpStatusCode.OK, Array.Empty<object>()));
        await using var scope = host;

        var outcome = await host.CallAsync("ship_order", new() { ["sellerOrderId"] = 12, ["carrier"] = "Yurtiçi", ["trackingNumber"] = "TR123" });

        outcome.IsError.Should().BeFalse(outcome.Text);
        using (var body = JsonDocument.Parse(fake.Requests.Single(r => r.PathAndQuery == "/api/seller/orders/12/ship").Body!))
        {
            body.RootElement.GetProperty("carrier").GetString().Should().Be("Yurtiçi");
            body.RootElement.GetProperty("trackingNumber").GetString().Should().Be("TR123");
        }
        using var json = JsonDocument.Parse(outcome.Text);
        json.RootElement.GetProperty("shipment").GetProperty("carrier").GetString().Should().Be("Yurtiçi");
    }

    [Fact]
    public async Task ShipOrder_Forbidden_TurkishOwnershipMessage()
    {
        var (host, _) = await StartAsync(f => f.On(HttpMethod.Post, "/api/seller/orders/11/ship", HttpStatusCode.Forbidden, new { message = "You do not have access to this seller order." }));
        await using var scope = host;

        var outcome = await host.CallAsync("ship_order", new() { ["sellerOrderId"] = 11, ["carrier"] = "Yurtiçi", ["trackingNumber"] = "TR1" });

        outcome.IsError.Should().BeTrue();
        outcome.Text.Should().Contain("Bu satıcı siparişi size ait değil (id=11)");
    }

    [Fact]
    public async Task MarkDelivered_BeforeShip_ShowsExodusMessage()
    {
        var (host, _) = await StartAsync(f => f.On(HttpMethod.Patch, "/api/shipments/seller-orders/11/deliver", HttpStatusCode.BadRequest,
            new { message = "Shipment yok. Önce ship yap." }));
        await using var scope = host;

        var outcome = await host.CallAsync("mark_delivered", new() { ["sellerOrderId"] = 11 });

        outcome.IsError.Should().BeTrue();
        outcome.Text.Should().Contain("Shipment yok. Önce ship yap.");
    }

    [Fact]
    public async Task MarkDelivered_Success_PatchesThenRefetchesDeliveredOrder()
    {
        var delivered = new
        {
            id = 12, orderId = 4, orderNumber = "ORD-12", createdAt = "2026-11-10T08:00:00", sellerId = 3, sellerName = "Mağaza", status = "Delivered", total = 100m,
            items = new[] { new { id = 1, listingId = 5, productId = 2, productName = "Kulaklık X", quantity = 2, unitPrice = 50m, totalPrice = 100m } },
            shipment = new { id = 9, carrier = "Yurtiçi", trackingNumber = "TR123", shippedAt = "2026-11-11T09:00:00", deliveredAt = "2026-11-12T10:00:00" },
        };
        var (host, fake) = await StartAsync(f => f
            .On(HttpMethod.Patch, "/api/shipments/seller-orders/12/deliver", HttpStatusCode.OK, new { message = "Delivered" })
            .On(HttpMethod.Get, "/api/seller/orders/12", HttpStatusCode.OK, delivered)
            .On(HttpMethod.Get, "/api/shipments/seller-orders/12/timeline", HttpStatusCode.OK, Array.Empty<object>()));
        await using var scope = host;

        var outcome = await host.CallAsync("mark_delivered", new() { ["sellerOrderId"] = 12 });

        outcome.IsError.Should().BeFalse(outcome.Text);
        fake.Requests.Where(r => r.PathAndQuery != "/api/auth/login").Select(r => $"{r.Method} {r.PathAndQuery}")
            .Should().Equal("PATCH /api/shipments/seller-orders/12/deliver", "GET /api/seller/orders/12", "GET /api/shipments/seller-orders/12/timeline");
        using var json = JsonDocument.Parse(outcome.Text);
        json.RootElement.GetProperty("status").GetString().Should().Be("Delivered");
        json.RootElement.GetProperty("shipment").GetProperty("deliveredAt").GetString().Should().StartWith("2026-11-12T10:00:00");
    }

    [Fact]
    public async Task SalesSummary_StopsPagingAtFromDate()
    {
        // 100 items, the last one older than from (2026-11-10): a single request.
        var page = Enumerable.Range(1, 100)
            .Select(i => Order(i, "Delivered", i == 100 ? "2026-11-05T10:00:00" : "2026-11-12T10:00:00")).ToArray();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
        var (host, fake) = await StartAsync(f => f.On(HttpMethod.Get, "/api/seller/orders?page=1&pageSize=100", HttpStatusCode.OK, page), time);
        await using var scope = host;

        var outcome = await host.CallAsync("sales_summary", new() { ["fromDate"] = "2026-11-10", ["toDate"] = "2026-11-15" });

        outcome.IsError.Should().BeFalse(outcome.Text);
        ApiRequests(fake).Should().Be(1);
        using var json = JsonDocument.Parse(outcome.Text);
        json.RootElement.GetProperty("orderCount").GetInt32().Should().Be(99);
        json.RootElement.GetProperty("revenue").GetDecimal().Should().Be(9900m);
        json.RootElement.GetProperty("truncated").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("from").GetString().Should().Be("2026-11-10");
        json.RootElement.GetProperty("statusBreakdown").GetProperty("Delivered").GetInt32().Should().Be(99);
        json.RootElement.GetProperty("topListings")[0].GetProperty("listingId").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task SalesSummary_DefaultRange_Is30DaysEndingTodayUtc_AndTruncatesAt20Pages()
    {
        var page = Enumerable.Range(1, 100).Select(i => Order(i, "Delivered", "2026-11-14T10:00:00")).ToArray();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
        var (host, fake) = await StartAsync(f =>
        {
            for (var p = 1; p <= 20; p++) f.On(HttpMethod.Get, $"/api/seller/orders?page={p}&pageSize=100", HttpStatusCode.OK, page);
        }, time);
        await using var scope = host;

        var outcome = await host.CallAsync("sales_summary");

        outcome.IsError.Should().BeFalse(outcome.Text);
        ApiRequests(fake).Should().Be(20);
        using var json = JsonDocument.Parse(outcome.Text);
        json.RootElement.GetProperty("truncated").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("to").GetString().Should().Be("2026-11-15");
        json.RootElement.GetProperty("from").GetString().Should().Be("2026-10-17");
    }

    [Fact]
    public async Task SalesSummary_RejectsFromAfterTo()
    {
        var (host, fake) = await StartAsync(_ => { });
        await using var scope = host;

        var outcome = await host.CallAsync("sales_summary", new() { ["fromDate"] = "2026-11-15", ["toDate"] = "2026-11-10" });

        outcome.IsError.Should().BeTrue();
        ApiRequests(fake).Should().Be(0);
    }
}
