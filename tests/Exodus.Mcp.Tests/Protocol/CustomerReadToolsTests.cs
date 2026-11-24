using System.Globalization;
using System.Net;
using System.Text.Json;
using Exodus.Mcp.Core.Configuration;
using Exodus.Mcp.Tests.Fakes;
using Exodus.Mcp.Tests.Infrastructure;
using FluentAssertions;

namespace Exodus.Mcp.Tests.Protocol;

public class CustomerReadToolsTests
{
    private const string SearchPrefix = "/api/listings/search";

    private static readonly object EmptySearch = new { items = Array.Empty<object>(), totalCount = 0, page = 1, pageSize = 10, totalPages = 0 };

    private static async Task<(McpTestHost Host, FakeExodusHandler Fake)> StartAsync(Action<FakeExodusHandler> setup)
    {
        var fake = new FakeExodusHandler().WithLogin();
        setup(fake);
        return (await McpTestHost.StartAsync(ExodusProfile.Customer, fake), fake);
    }

    [Fact]
    public async Task SearchListings_BuildsQueryAndMapsItems()
    {
        const string path = SearchPrefix + "?search=kulakl%C4%B1k&maxPrice=1500&sortBy=price&sortOrder=asc&page=1&pageSize=10";
        var (host, fake) = await StartAsync(f => f.On(HttpMethod.Get, path, HttpStatusCode.OK, new
        {
            items = new[]
            {
                new { id = 5, productId = 2, productName = "Kulaklık X", categoryName = "Ses", price = 999.9m, stockQuantity = 4, stockStatus = "InStock", condition = "New", sellerId = 3, sellerName = "Mağaza A", isActive = true },
            },
            totalCount = 11, page = 1, pageSize = 10, totalPages = 2,
        }));
        await using var _ = host;

        var outcome = await host.CallAsync("search_listings", new() { ["query"] = "kulaklık", ["maxPrice"] = 1500, ["sortBy"] = "price" });

        outcome.IsError.Should().BeFalse(outcome.Text);
        fake.Count(HttpMethod.Get, path).Should().Be(1);
        using var json = JsonDocument.Parse(outcome.Text);
        var root = json.RootElement;
        root.GetProperty("totalPages").GetInt32().Should().Be(2);
        root.GetProperty("totalCount").GetInt32().Should().Be(11);
        var item = root.GetProperty("items")[0];
        item.GetProperty("listingId").GetInt32().Should().Be(5);
        item.GetProperty("seller").GetString().Should().Be("Mağaza A");
        item.GetProperty("stock").GetInt32().Should().Be(4);
        item.GetProperty("category").GetString().Should().Be("Ses");
        item.GetProperty("productName").GetString().Should().Be("Kulaklık X");
    }

    [Fact]
    public async Task SearchListings_FormatsDecimalsInvariant_UnderTurkishCulture()
    {
        const string path = SearchPrefix + "?minPrice=1500.5&sortBy=price&sortOrder=asc&page=1&pageSize=10";
        var (host, fake) = await StartAsync(f => f.On(HttpMethod.Get, path, HttpStatusCode.OK, EmptySearch));
        await using var _ = host;

        var oldCurrent = CultureInfo.CurrentCulture;
        var oldDefault = CultureInfo.DefaultThreadCurrentCulture;
        var tr = new CultureInfo("tr-TR");
        try
        {
            CultureInfo.CurrentCulture = tr;
            CultureInfo.DefaultThreadCurrentCulture = tr;

            var outcome = await host.CallAsync("search_listings", new() { ["minPrice"] = 1500.5 });

            outcome.IsError.Should().BeFalse(outcome.Text);
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCurrent;
            CultureInfo.DefaultThreadCurrentCulture = oldDefault;
        }

        fake.Requests.Should().Contain(r => r.PathAndQuery == path);
    }

    [Fact]
    public async Task SearchListings_NormalizesCondition()
    {
        const string path = SearchPrefix + "?condition=LikeNew&sortBy=price&sortOrder=asc&page=1&pageSize=10";
        var (host, fake) = await StartAsync(f => f.On(HttpMethod.Get, path, HttpStatusCode.OK, EmptySearch));
        await using var _ = host;

        var outcome = await host.CallAsync("search_listings", new() { ["condition"] = "likenew" });

        outcome.IsError.Should().BeFalse(outcome.Text);
        fake.Count(HttpMethod.Get, path).Should().Be(1);
    }

    [Fact]
    public async Task SearchListings_NewestMapsToCreatedAtDesc()
    {
        const string path = SearchPrefix + "?sortBy=createdAt&sortOrder=desc&page=1&pageSize=10";
        var (host, fake) = await StartAsync(f => f.On(HttpMethod.Get, path, HttpStatusCode.OK, EmptySearch));
        await using var _ = host;

        var outcome = await host.CallAsync("search_listings", new() { ["sortBy"] = "newest", ["sortOrder"] = "desc" });

        outcome.IsError.Should().BeFalse(outcome.Text);
        fake.Count(HttpMethod.Get, path).Should().Be(1);
    }

    [Fact]
    public async Task SearchListings_MinGreaterThanMax_IsErrorWithoutRequest()
    {
        var (host, fake) = await StartAsync(_ => { });
        await using var _ = host;

        var outcome = await host.CallAsync("search_listings", new() { ["minPrice"] = 20, ["maxPrice"] = 10 });

        outcome.IsError.Should().BeTrue();
        outcome.Text.Should().Contain("minPrice");
        fake.Requests.Should().NotContain(r => r.PathAndQuery.StartsWith(SearchPrefix));
    }

    [Fact]
    public async Task GetListing_CombinesListingAndProduct()
    {
        var (host, _) = await StartAsync(f => f
            .On(HttpMethod.Get, "/api/listings/5", HttpStatusCode.OK, new { id = 5, productId = 2, sellerId = 3, price = 99.5m, stock = 7, condition = "Used", isActive = true })
            .On(HttpMethod.Get, "/api/product/2", HttpStatusCode.OK, new { id = 2, productName = "Kulaklık X", productDescription = "Kablosuz" }));
        await using var _ = host;

        var outcome = await host.CallAsync("get_listing", new() { ["listingId"] = 5 });

        outcome.IsError.Should().BeFalse(outcome.Text);
        using var json = JsonDocument.Parse(outcome.Text);
        var root = json.RootElement;
        root.GetProperty("listingId").GetInt32().Should().Be(5);
        root.GetProperty("productId").GetInt32().Should().Be(2);
        root.GetProperty("productName").GetString().Should().Be("Kulaklık X");
        root.GetProperty("description").GetString().Should().Be("Kablosuz");
        root.GetProperty("stock").GetInt32().Should().Be(7);
        root.GetProperty("condition").GetString().Should().Be("Used");
        root.GetProperty("isActive").GetBoolean().Should().BeTrue();
        root.GetProperty("sellerId").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task GetListing_NotFound_TurkishMessage()
    {
        var (host, _) = await StartAsync(f => f.On(HttpMethod.Get, "/api/listings/99", HttpStatusCode.NotFound, new { detail = "yok" }));
        await using var _ = host;

        var outcome = await host.CallAsync("get_listing", new() { ["listingId"] = 99 });

        outcome.IsError.Should().BeTrue();
        outcome.Text.Should().Contain("Listing bulunamadı (id=99)");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task GetOrder_RequiresExactlyOneIdentifier(bool withId, bool withNumber)
    {
        var (host, fake) = await StartAsync(_ => { });
        await using var _ = host;
        var args = new Dictionary<string, object?>();
        if (withId) args["orderId"] = 1;
        if (withNumber) args["orderNumber"] = "ORD-1";

        var outcome = await host.CallAsync("get_order", args);

        outcome.IsError.Should().BeTrue();
        outcome.Text.Should().Contain("tam olarak biri");
        fake.Requests.Should().NotContain(r => r.PathAndQuery.StartsWith("/api/order"));
    }

    [Fact]
    public async Task GetOrder_ByNumber_EscapesPath()
    {
        const string path = "/api/order/number/A%2FB%20C";
        var (host, fake) = await StartAsync(f => f.On(HttpMethod.Get, path, HttpStatusCode.OK, new
        {
            id = 9, orderNumber = "A/B C", status = "Shipped", totalAmount = 250m, currency = "TRY", shippingAddress = "Adres 1",
            createdAt = "2025-01-02T10:00:00", paidAt = "2025-01-02T10:05:00",
            sellerOrders = new[]
            {
                new
                {
                    id = 4, sellerName = "Mağaza A", status = "Shipped", total = 250m,
                    items = new[] { new { productName = "Kulaklık X", quantity = 2, unitPrice = 125m } },
                    shipment = new { carrier = "Yurtiçi", trackingNumber = "TR123", shippedAt = "2025-01-03T09:00:00" },
                },
            },
        }));
        await using var _ = host;

        var outcome = await host.CallAsync("get_order", new() { ["orderNumber"] = "A/B C" });

        outcome.IsError.Should().BeFalse(outcome.Text);
        fake.Requests.Should().Contain(r => r.PathAndQuery == path);
        using var json = JsonDocument.Parse(outcome.Text);
        var root = json.RootElement;
        root.GetProperty("orderId").GetInt32().Should().Be(9);
        root.GetProperty("status").GetString().Should().Be("Shipped");
        var seller = root.GetProperty("sellerOrders")[0];
        seller.GetProperty("sellerOrderId").GetInt32().Should().Be(4);
        seller.GetProperty("items")[0].GetProperty("quantity").GetInt32().Should().Be(2);
        seller.GetProperty("shipment").GetProperty("trackingNumber").GetString().Should().Be("TR123");
        // The fake Exodus sends no zone; the output must be explicit UTC.
        root.GetProperty("createdAt").GetString().Should().EndWith("Z");
        root.GetProperty("paidAt").GetString().Should().EndWith("Z");
        seller.GetProperty("shipment").GetProperty("shippedAt").GetString().Should().EndWith("Z");
    }

    [Fact]
    public async Task ListMyOrders_PassesStatusFilter()
    {
        const string path = "/api/order?status=Shipped&page=1&pageSize=10";
        var (host, fake) = await StartAsync(f => f.On(HttpMethod.Get, path, HttpStatusCode.OK, new
        {
            items = new[] { new { id = 9, orderNumber = "ORD-9", status = "Shipped", totalAmount = 250m, currency = "TRY", itemCount = 2, createdAt = "2025-01-02T10:00:00" } },
            totalCount = 1, page = 1, pageSize = 10,
        }));
        await using var _ = host;

        var outcome = await host.CallAsync("list_my_orders", new() { ["status"] = "shipped" });

        outcome.IsError.Should().BeFalse(outcome.Text);
        fake.Count(HttpMethod.Get, path).Should().Be(1);
        using var json = JsonDocument.Parse(outcome.Text);
        json.RootElement.GetProperty("items")[0].GetProperty("orderNumber").GetString().Should().Be("ORD-9");
        json.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
    }
}
