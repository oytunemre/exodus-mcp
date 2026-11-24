using Exodus.Mcp.Core.Api.Dtos;

namespace Exodus.Mcp.Core.Tools;

public sealed record SalesSummary(
    DateOnly From, DateOnly To, int OrderCount, decimal Revenue, int CancelledCount,
    IReadOnlyDictionary<string, int> StatusBreakdown, IReadOnlyList<TopListing> TopListings, bool Truncated);

public sealed record TopListing(int ListingId, string ProductName, int Quantity, decimal Revenue);

/// <summary>Pure aggregation of seller orders for <c>sales_summary</c>: no I/O, no clock.</summary>
public static class SalesSummaryCalculator
{
    private const string Cancelled = "Cancelled";
    private const int TopCount = 5;

    /// <summary>
    /// createdAt is taken as UTC (Unspecified is treated as UTC). Range: [from 00:00Z, to+1 00:00Z).
    /// Revenue and TopListings exclude cancelled orders; OrderCount and StatusBreakdown include them.
    /// </summary>
    public static SalesSummary Calculate(IEnumerable<SellerOrderDto> orders, DateOnly from, DateOnly to, bool truncated)
    {
        ArgumentNullException.ThrowIfNull(orders);

        var start = new DateTime(from.Year, from.Month, from.Day, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(to.Year, to.Month, to.Day, 0, 0, 0, DateTimeKind.Utc).AddDays(1);

        var inRange = orders.Where(o =>
        {
            var at = ToUtc(o.CreatedAt);
            return at >= start && at < end;
        }).ToList();

        var active = inRange.Where(o => !IsCancelled(o)).ToList();
        var breakdown = inRange
            .GroupBy(o => o.Status ?? "Unknown")
            .ToDictionary(g => g.Key, g => g.Count());

        var top = active
            .SelectMany(o => o.Items ?? [])
            .GroupBy(l => l.ListingId)
            .Select(g => new TopListing(g.Key, g.First().ProductName ?? string.Empty, g.Sum(l => l.Quantity), g.Sum(l => l.TotalPrice)))
            .OrderByDescending(t => t.Quantity)
            .ThenByDescending(t => t.Revenue)
            .ThenBy(t => t.ListingId)
            .Take(TopCount)
            .ToList();

        return new SalesSummary(from, to, inRange.Count, active.Sum(o => o.Total), inRange.Count - active.Count, breakdown, top, truncated);
    }

    /// <summary>Treats an Unspecified kind as UTC (Exodus may omit the Z).</summary>
    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static bool IsCancelled(SellerOrderDto order) => string.Equals(order.Status, Cancelled, StringComparison.OrdinalIgnoreCase);
}
