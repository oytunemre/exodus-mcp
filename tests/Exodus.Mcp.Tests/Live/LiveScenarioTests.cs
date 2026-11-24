using System.Text.Json;
using Exodus.Mcp.Tests.Infrastructure;
using FluentAssertions;
using Xunit.Sdk;

namespace Exodus.Mcp.Tests.Live;

/// <summary>
/// Spec Doğrulama B.4 without the LLM: a customer and a seller stdio process against a running Exodus (seed data).
/// Every run places a new order, so nothing assumes a fresh database: the seller order is found by its order number.
/// </summary>
public class LiveScenarioTests
{
    private const string Password = "Demo123!";
    private const string SellerName = "Zeynep Kaya";
    private const int MaxPages = 5;

    [LiveFact]
    public async Task CustomerAndSeller_EndToEnd()
    {
        var baseUrl = Environment.GetEnvironmentVariable("EXODUS_BASE_URL") is { Length: > 0 } url ? url : "http://localhost:5013";
        await using var customer = await StdioServer.StartAsync(StdioServer.Environment(baseUrl, "customer", "ahmet@test.com", Password));
        await using var seller = await StdioServer.StartAsync(StdioServer.Environment(baseUrl, "seller", "zeynep@test.com", Password));

        // A run that stopped before place_order leaves items in the cart; they must not ride along into this order.
        await EmptyCartAsync(customer);

        // 1. Zeynep's cheapest in-stock listing at or below 1500 TL.
        var listingId = await FindListingAsync(customer);

        // 2.
        using (var cart = await CallJsonAsync(customer, "add_to_cart", new() { ["listingId"] = listingId, ["quantity"] = 1 }))
        {
            cart.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("listingId").GetInt32())
                .Should().Equal(listingId);
        }

        // 3.
        int addressId;
        using (var addresses = await CallJsonAsync(customer, "list_my_addresses"))
        {
            var defaults = addresses.RootElement.EnumerateArray().Where(a => a.GetProperty("isDefault").GetBoolean()).ToList();
            defaults.Should().ContainSingle("ahmet@test.com has a default address in the seed");
            addressId = defaults[0].GetProperty("addressId").GetInt32();
        }

        // 4.
        string orderNumber;
        using (var placed = await CallJsonAsync(customer, "place_order", new() { ["shippingAddressId"] = addressId }))
        {
            orderNumber = placed.RootElement.GetProperty("orderNumber").GetString() ?? "";
            orderNumber.Should().NotBeEmpty();
        }

        // 5.
        var sellerOrderId = await FindPendingSellerOrderAsync(seller, orderNumber);

        // 6.
        using (var confirmed = await CallJsonAsync(seller, "confirm_order", new() { ["sellerOrderId"] = sellerOrderId }))
        {
            confirmed.RootElement.GetProperty("status").GetString().Should().Be("Confirmed");
        }

        // 7.
        var shipArgs = new Dictionary<string, object?> { ["sellerOrderId"] = sellerOrderId, ["carrier"] = "Yurtici", ["trackingNumber"] = "TR123" };
        using (var shipped = await CallJsonAsync(seller, "ship_order", shipArgs))
        {
            shipped.RootElement.GetProperty("status").GetString().Should().Be("Shipped");
            shipped.RootElement.GetProperty("shipment").GetProperty("trackingNumber").GetString().Should().Be("TR123");
        }

        // 8.
        using (var order = await CallJsonAsync(customer, "get_order", new() { ["orderNumber"] = orderNumber }))
        {
            order.RootElement.GetProperty("status").GetString().Should().Be("Shipped");
        }

        // 9.
        using (var summary = await CallJsonAsync(seller, "sales_summary"))
        {
            summary.RootElement.GetProperty("orderCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        }
    }

    private static async Task EmptyCartAsync(StdioServer customer)
    {
        int[] cartItemIds;
        using (var cart = await CallJsonAsync(customer, "get_cart"))
        {
            cartItemIds = cart.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("cartItemId").GetInt32()).ToArray();
        }

        foreach (var cartItemId in cartItemIds)
        {
            (await CallJsonAsync(customer, "remove_from_cart", new() { ["cartItemId"] = cartItemId })).Dispose();
        }
    }

    private static async Task<int> FindListingAsync(StdioServer customer)
    {
        var seen = new List<string>();
        for (var page = 1; page <= MaxPages; page++)
        {
            using var result = await CallJsonAsync(customer, "search_listings",
                new() { ["maxPrice"] = 1500, ["sortBy"] = "price", ["page"] = page });
            foreach (var item in result.RootElement.GetProperty("items").EnumerateArray())
            {
                var listingId = item.GetProperty("listingId").GetInt32();
                var stock = item.GetProperty("stock").GetInt32();
                var seller = item.TryGetProperty("seller", out var s) ? s.GetString() : null;
                if (seller == SellerName && stock > 0)
                {
                    return listingId;
                }

                seen.Add($"{listingId} (seller={seller}, stock={stock})");
            }

            if (page >= result.RootElement.GetProperty("totalPages").GetInt32())
            {
                break;
            }
        }

        throw new XunitException(
            $"No in-stock listing of '{SellerName}' at or below 1500 TL in the first {MaxPages} search pages. Seen: {string.Join(", ", seen)}");
    }

    // list_seller_orders is newest first, so the order just placed is normally on page 1.
    private static async Task<int> FindPendingSellerOrderAsync(StdioServer seller, string orderNumber)
    {
        var seen = new List<string>();
        for (var page = 1; page <= MaxPages; page++)
        {
            using var result = await CallJsonAsync(seller, "list_seller_orders", new() { ["status"] = "Pending", ["page"] = page });
            foreach (var item in result.RootElement.GetProperty("items").EnumerateArray())
            {
                var number = item.TryGetProperty("orderNumber", out var n) ? n.GetString() : null;
                if (number == orderNumber)
                {
                    return item.GetProperty("sellerOrderId").GetInt32();
                }

                seen.Add(number ?? "(none)");
            }

            if (!result.RootElement.GetProperty("hasMore").GetBoolean())
            {
                break;
            }
        }

        throw new XunitException(
            $"Order {orderNumber} is not among the seller's Pending orders (first {MaxPages} pages). Seen: {string.Join(", ", seen)}");
    }

    private static async Task<JsonDocument> CallJsonAsync(StdioServer server, string tool, Dictionary<string, object?>? args = null)
    {
        var outcome = await server.CallAsync(tool, args);
        if (outcome.IsError)
        {
            throw new XunitException($"{tool} failed: {outcome.Text}");
        }

        return JsonDocument.Parse(outcome.Text);
    }
}
