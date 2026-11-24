using Exodus.Mcp.Core.Api.Dtos;
using Exodus.Mcp.Core.Tools;
using FluentAssertions;

namespace Exodus.Mcp.Tests.Tools;

public class SalesSummaryCalculatorTests
{
    private static SellerOrderDto Order(int id, DateTime createdAt, string status, decimal total, params SellerOrderLineDto[] lines)
        => new(id, id, $"ORD-{id}", createdAt, 3, "Satıcı", status, total, lines.ToList(), null);

    private static SellerOrderLineDto Line(int listingId, int quantity, decimal unitPrice)
        => new(listingId, listingId, listingId, $"Ürün {listingId}", quantity, unitPrice, quantity * unitPrice);

    [Fact]
    public void Calculate_FixtureTotals()
    {
        var orders = new[]
        {
            Order(1, new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc), "Delivered", 100, Line(1, 2, 50)),
            Order(2, new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc), "Cancelled", 40, Line(2, 1, 40)),
            Order(3, new DateTime(2026, 11, 15, 23, 59, 59, DateTimeKind.Unspecified), "Pending", 60, Line(1, 1, 60)),
            Order(4, new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc), "Shipped", 30, Line(3, 3, 10)),
        };

        var summary = SalesSummaryCalculator.Calculate(orders, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 15), truncated: false);

        summary.OrderCount.Should().Be(3);
        summary.Revenue.Should().Be(160m);
        summary.CancelledCount.Should().Be(1);
        summary.StatusBreakdown.Should().BeEquivalentTo(new Dictionary<string, int> { ["Delivered"] = 1, ["Cancelled"] = 1, ["Pending"] = 1 });
        summary.TopListings.Should().HaveCount(1);
        summary.TopListings[0].Should().Be(new TopListing(1, "Ürün 1", 3, 160m));
        summary.Truncated.Should().BeFalse();
    }

    [Fact]
    public void Calculate_EmptyInput_ZeroesNotNulls()
    {
        var summary = SalesSummaryCalculator.Calculate([], new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 15), truncated: true);

        summary.OrderCount.Should().Be(0);
        summary.Revenue.Should().Be(0m);
        summary.CancelledCount.Should().Be(0);
        summary.StatusBreakdown.Should().NotBeNull().And.BeEmpty();
        summary.TopListings.Should().NotBeNull().And.BeEmpty();
        summary.Truncated.Should().BeTrue();
    }
}
