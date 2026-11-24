using System.Net;
using System.Text.Json;
using Exodus.Mcp.Core.Configuration;
using Exodus.Mcp.Tests.Fakes;
using Exodus.Mcp.Tests.Infrastructure;
using FluentAssertions;

namespace Exodus.Mcp.Tests.Protocol;

public class SellerListingToolsTests
{
    private static object Item(int id = 5, string stockStatus = "InStock") => new
    {
        id, productId = 2, productName = "Kulaklık X", primaryImage = (string?)null, price = 249.9m, stockQuantity = 10, stockStatus,
        lowStockThreshold = 5, sku = (string?)null, condition = "New", isActive = true, createdAt = "2025-01-02T10:00:00", updatedAt = (string?)null,
    };

    private static async Task<(McpTestHost Host, FakeExodusHandler Fake)> StartAsync(Action<FakeExodusHandler> setup)
    {
        var fake = new FakeExodusHandler().WithLogin(role: "Seller", userId: 3);
        setup(fake);
        return (await McpTestHost.StartAsync(ExodusProfile.Seller, fake), fake);
    }

    [Fact]
    public async Task ListMyListings_Low_UsesLowStockRoute()
    {
        var (host, fake) = await StartAsync(f => f.On(HttpMethod.Get, "/api/seller/listings/low-stock", HttpStatusCode.OK,
            new[] { new { id = 5, productId = 2, productName = "Kulaklık X", stockQuantity = 3, lowStockThreshold = 5, sku = (string?)null } }));
        await using var hostScope = host;

        var outcome = await host.CallAsync("list_my_listings", new() { ["stockFilter"] = "low", ["isActive"] = true, ["page"] = 4 });

        outcome.IsError.Should().BeFalse(outcome.Text);
        fake.Requests.Select(r => r.PathAndQuery).Should().NotContain(p => p.StartsWith("/api/seller/listings?"));
        fake.Count(HttpMethod.Get, "/api/seller/listings/low-stock").Should().Be(1);
        using var json = JsonDocument.Parse(outcome.Text);
        json.RootElement.GetProperty("filter").GetString().Should().Be("low");
        json.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        json.RootElement.TryGetProperty("page", out _).Should().BeFalse();
        var item = json.RootElement.GetProperty("items")[0];
        item.GetProperty("listingId").GetInt32().Should().Be(5);
        item.GetProperty("stock").GetInt32().Should().Be(3);
        item.GetProperty("lowStockThreshold").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task ListMyListings_All_PassesIsActiveAndPage()
    {
        var (host, fake) = await StartAsync(f => f.On(HttpMethod.Get, "/api/seller/listings?isActive=true&page=2&pageSize=20", HttpStatusCode.OK,
            new { items = new[] { Item() }, totalCount = 21, page = 2, pageSize = 20, totalPages = 2 }));
        await using var hostScope = host;

        var outcome = await host.CallAsync("list_my_listings", new() { ["isActive"] = true, ["page"] = 2 });

        outcome.IsError.Should().BeFalse(outcome.Text);
        fake.Count(HttpMethod.Get, "/api/seller/listings?isActive=true&page=2&pageSize=20").Should().Be(1);
        using var json = JsonDocument.Parse(outcome.Text);
        json.RootElement.GetProperty("filter").GetString().Should().Be("all");
        json.RootElement.GetProperty("page").GetInt32().Should().Be(2);
        json.RootElement.GetProperty("totalPages").GetInt32().Should().Be(2);
        var item = json.RootElement.GetProperty("items")[0];
        item.GetProperty("price").GetDecimal().Should().Be(249.9m);
        item.GetProperty("stock").GetInt32().Should().Be(10);
        item.GetProperty("stockStatus").GetString().Should().Be("InStock");
        item.GetProperty("condition").GetString().Should().Be("New");
    }

    [Fact]
    public async Task CreateListing_WithExistingProduct_SingleRequest()
    {
        var (host, fake) = await StartAsync(f => f.On(HttpMethod.Post, "/api/seller/listings", HttpStatusCode.Created, Item(8)));
        await using var hostScope = host;

        var outcome = await host.CallAsync("create_listing", new() { ["productId"] = 2, ["price"] = 249.9, ["stockQuantity"] = 10 });

        outcome.IsError.Should().BeFalse(outcome.Text);
        fake.Requests.Count(r => r.Method == "POST" && r.PathAndQuery != "/api/auth/login").Should().Be(1);
        fake.Requests.Single(r => r.PathAndQuery == "/api/seller/listings").Body
            .Should().Be("{\"productId\":2,\"price\":249.9,\"stockQuantity\":10,\"condition\":\"New\"}");
        using var json = JsonDocument.Parse(outcome.Text);
        json.RootElement.GetProperty("listingId").GetInt32().Should().Be(8);
        json.RootElement.TryGetProperty("createdProductId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task CreateListing_WithNewProduct_CreatesProductThenListing()
    {
        var (host, fake) = await StartAsync(f => f
            .On(HttpMethod.Post, "/api/product", HttpStatusCode.Created, new { id = 2, productName = "Kulaklık X", productDescription = "Açıklama", barcodes = new[] { "8690000000001" } })
            .On(HttpMethod.Post, "/api/seller/listings", HttpStatusCode.Created, Item(8)));
        await using var hostScope = host;

        var outcome = await host.CallAsync("create_listing", new()
        {
            ["newProductName"] = "Kulaklık X", ["newProductDescription"] = "Açıklama", ["newProductBarcode"] = "8690000000001",
            ["price"] = 249.9, ["stockQuantity"] = 10, ["condition"] = "Used",
        });

        outcome.IsError.Should().BeFalse(outcome.Text);
        fake.Requests.Select(r => r.PathAndQuery).Where(p => p != "/api/auth/login").Should().Equal("/api/product", "/api/seller/listings");
        using (var product = JsonDocument.Parse(fake.Requests.Single(r => r.PathAndQuery == "/api/product").Body!))
        {
            product.RootElement.GetProperty("barcodes")[0].GetString().Should().Be("8690000000001");
            product.RootElement.GetProperty("productName").GetString().Should().Be("Kulaklık X");
        }
        using (var listing = JsonDocument.Parse(fake.Requests.Single(r => r.PathAndQuery == "/api/seller/listings").Body!))
        {
            listing.RootElement.GetProperty("productId").GetInt32().Should().Be(2);
            listing.RootElement.GetProperty("condition").GetString().Should().Be("Used");
        }
        using var json = JsonDocument.Parse(outcome.Text);
        json.RootElement.GetProperty("createdProductId").GetInt32().Should().Be(2);
        json.RootElement.GetProperty("listingId").GetInt32().Should().Be(8);
    }

    [Fact]
    public async Task CreateListing_NewProductWithoutBarcode_IsErrorWithoutRequest()
    {
        var (host, fake) = await StartAsync(_ => { });
        await using var hostScope = host;

        var outcome = await host.CallAsync("create_listing", new() { ["newProductName"] = "Kulaklık X", ["price"] = 10, ["stockQuantity"] = 1 });

        outcome.IsError.Should().BeTrue();
        outcome.Text.Should().Contain("newProductBarcode");
        fake.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateListing_BothProductIdAndName_IsError()
    {
        var (host, fake) = await StartAsync(_ => { });
        await using var hostScope = host;

        var outcome = await host.CallAsync("create_listing", new()
        {
            ["productId"] = 2, ["newProductName"] = "Kulaklık X", ["newProductBarcode"] = "1", ["price"] = 10, ["stockQuantity"] = 1,
        });

        outcome.IsError.Should().BeTrue();
        outcome.Text.Should().Contain("tam olarak biri");
        fake.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateListing_ListingFails_ReportsCreatedProductId()
    {
        var (host, fake) = await StartAsync(f => f
            .On(HttpMethod.Post, "/api/product", HttpStatusCode.Created, new { id = 2, productName = "Kulaklık X", productDescription = "Kulaklık X", barcodes = new[] { "1" } })
            .On(HttpMethod.Post, "/api/seller/listings", HttpStatusCode.BadRequest, new { detail = "Price must be greater than 0." }));
        await using var hostScope = host;

        var outcome = await host.CallAsync("create_listing", new() { ["newProductName"] = "Kulaklık X", ["newProductBarcode"] = "1", ["price"] = 10, ["stockQuantity"] = 1 });

        outcome.IsError.Should().BeTrue();
        outcome.Text.Should().Contain("Ürün oluşturuldu (productId=2) ancak listing oluşturulamadı: ")
            .And.Contain("Tekrar denerken productId=2 kullanın.").And.NotContain("..");
        fake.Count(HttpMethod.Post, "/api/product").Should().Be(1);
        fake.Count(HttpMethod.Post, "/api/seller/listings").Should().Be(1);
    }

    [Fact]
    public async Task UpdateListing_SendsOnlyProvidedFields()
    {
        var (host, fake) = await StartAsync(f => f.On(HttpMethod.Put, "/api/seller/listings/5", HttpStatusCode.OK, new { message = "Listing updated" }));
        await using var hostScope = host;

        var outcome = await host.CallAsync("update_listing", new() { ["listingId"] = 5, ["price"] = 249.9 });

        outcome.IsError.Should().BeFalse(outcome.Text);
        fake.Requests.Single(r => r.Method == "PUT").Body.Should().Be("{\"price\":249.9}");
        using var json = JsonDocument.Parse(outcome.Text);
        json.RootElement.GetProperty("listingId").GetInt32().Should().Be(5);
        json.RootElement.GetProperty("updated").GetProperty("price").GetDecimal().Should().Be(249.9m);
        json.RootElement.GetProperty("updated").TryGetProperty("isActive", out _).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateListing_NothingToUpdate_IsError()
    {
        var (host, fake) = await StartAsync(_ => { });
        await using var hostScope = host;

        var outcome = await host.CallAsync("update_listing", new() { ["listingId"] = 5 });

        outcome.IsError.Should().BeTrue();
        fake.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateStock_Negative_IsErrorWithoutRequest()
    {
        var (host, fake) = await StartAsync(_ => { });
        await using var hostScope = host;

        var outcome = await host.CallAsync("update_stock", new() { ["listingId"] = 5, ["stockQuantity"] = -1 });

        outcome.IsError.Should().BeTrue();
        fake.Requests.Should().BeEmpty();
    }
}
