using System.ComponentModel;
using Exodus.Mcp.Core.Api;
using Exodus.Mcp.Core.Api.Dtos;
using Exodus.Mcp.Core.Configuration;
using ModelContextProtocol.Server;

namespace Exodus.Mcp.Core.Tools;

/// <summary>Customer-profile tools (catalog, addresses, orders).</summary>
[McpServerToolType]
public sealed class CustomerTools(ExodusApiClient api, ExodusOptions options)
{
    private const int PageSize = 10;
    private static readonly string[] SortBys = ["price", "newest", "name"];
    private static readonly string[] SortOrders = ["asc", "desc"];
    private static readonly string[] Conditions = ["New", "LikeNew", "Used", "Refurbished"];
    private static readonly string[] OrderStatuses =
        ["Pending", "Processing", "Confirmed", "Shipped", "Delivered", "Completed", "Cancelled", "Refunded", "PartialRefund", "Failed"];

    [McpServerTool(Name = "search_listings", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Pazaryerindeki aktif listing'leri arar (sayfa başına 10 sonuç). Sonuçta listingId, ürün adı, fiyat, stok, satıcı ve kategori bulunur.")]
    public Task<string> SearchListings(
        [Description("Aranacak metin (ürün adı/açıklaması). Boş bırakılırsa tüm listing'ler.")] string? query = null,
        [Description("Kategori no (list_categories ile bulunur).")] int? categoryId = null,
        [Description("En düşük fiyat (TRY).")] decimal? minPrice = null,
        [Description("En yüksek fiyat (TRY).")] decimal? maxPrice = null,
        [Description("Ürün durumu: New, LikeNew, Used, Refurbished. Boşsa filtre yok.")] string? condition = null,
        [Description("Sıralama alanı: price, newest, name. Varsayılan: price.")] string? sortBy = "price",
        [Description("Sıralama yönü: asc veya desc. Varsayılan: asc.")] string? sortOrder = "asc",
        [Description("Sayfa numarası, 1'den başlar. Varsayılan: 1.")] int page = 1,
        CancellationToken ct = default) => ToolErrors.RunAsync(options, async () =>
    {
        var sort = ToolInput.OneOf(sortBy, "sortBy", SortBys, "price")!;
        var order = ToolInput.OneOf(sortOrder, "sortOrder", SortOrders, "asc")!;
        var cond = ToolInput.OneOf(condition, "condition", Conditions);
        ToolInput.Require(page >= 1, "page en az 1 olmalı.");
        ToolInput.Require(!(minPrice is { } min && maxPrice is { } max && min > max), "minPrice, maxPrice değerinden büyük olamaz.");

        var result = await api.SearchListingsAsync(new ListingSearchQuery(
            string.IsNullOrWhiteSpace(query) ? null : query.Trim(), categoryId, minPrice, maxPrice, cond,
            sort == "newest" ? "createdAt" : sort, order, page, PageSize), ct);

        var items = (result.Items ?? []).Select(i => new SearchItemView(
            i.Id, i.ProductName, i.Price, i.StockQuantity, i.StockStatus, i.Condition, i.SellerName, i.CategoryName)).ToList();
        var totalPages = result.TotalPages > 0 ? result.TotalPages : (int)Math.Ceiling(result.TotalCount / (double)PageSize);
        return new SearchView(result.Page, totalPages, result.TotalCount, items);
    });

    [McpServerTool(Name = "get_listing", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Tek bir listing'in ayrıntısını (ürün adı, açıklama, fiyat, stok, durum, satıcı) getirir.")]
    public Task<string> GetListing(
        [Description("Listing no (search_listings sonucundaki listingId).")] int listingId,
        CancellationToken ct = default) => ToolErrors.RunAsync(options, async () =>
    {
        var listing = await api.GetListingAsync(listingId, ct);
        var product = await api.GetProductAsync(listing.ProductId, ct);
        return new ListingView(listing.Id, listing.ProductId, product.ProductName, product.ProductDescription,
            listing.Price, listing.Stock, listing.Condition, listing.IsActive, listing.SellerId);
    }, notFound: $"Listing bulunamadı (id={listingId})");

    [McpServerTool(Name = "list_categories", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Kategori ağacını (kategori no, ad, ürün sayısı, alt kategoriler) listeler.")]
    public Task<string> ListCategories(CancellationToken ct = default) => ToolErrors.RunAsync(options, async () =>
        CategoryView.Map(await api.GetCategoryTreeAsync(ct)));

    [McpServerTool(Name = "list_my_addresses", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Hesabın kayıtlı teslimat/fatura adreslerini listeler.")]
    public Task<string> ListMyAddresses(CancellationToken ct = default) => ToolErrors.RunAsync(options, async () =>
        (await api.GetAddressesAsync(ct)).Select(a => new AddressView(a.Id, a.Title, a.FullName, a.FullAddress, a.IsDefault, a.Type)).ToList());

    [McpServerTool(Name = "list_my_orders", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Hesabın siparişlerini listeler (sayfa başına 10, en yeniden eskiye).")]
    public Task<string> ListMyOrders(
        [Description("Durum filtresi: Pending, Processing, Confirmed, Shipped, Delivered, Completed, Cancelled, Refunded, PartialRefund, Failed. Boşsa tümü.")] string? status = null,
        [Description("Sayfa numarası, 1'den başlar. Varsayılan: 1.")] int page = 1,
        CancellationToken ct = default) => ToolErrors.RunAsync(options, async () =>
    {
        var st = ToolInput.OneOf(status, "status", OrderStatuses);
        ToolInput.Require(page >= 1, "page en az 1 olmalı.");
        var result = await api.GetOrdersAsync(st, page, PageSize, ct);
        return new OrderListView(result.Page, result.TotalCount,
            (result.Items ?? []).Select(o => new OrderListItemView(o.Id, o.OrderNumber, o.Status, o.TotalAmount, o.Currency, o.ItemCount, o.CreatedAt)).ToList());
    });

    [McpServerTool(Name = "get_order", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bir siparişin ayrıntısını (durum, tutar, adres, satıcı siparişleri, ürünler, kargo) getirir. orderId ve orderNumber'dan tam olarak biri verilmelidir.")]
    public Task<string> GetOrder(
        [Description("Sipariş no (sayı). orderNumber ile birlikte verilmez.")] int? orderId = null,
        [Description("Sipariş numarası (örn. ORD-2025-0001). orderId ile birlikte verilmez.")] string? orderNumber = null,
        CancellationToken ct = default)
    {
        var number = string.IsNullOrWhiteSpace(orderNumber) ? null : orderNumber.Trim();
        var label = orderId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? number ?? "";
        return ToolErrors.RunAsync(options, async () =>
        {
            ToolInput.Require((orderId is not null) ^ (number is not null), "orderId ve orderNumber'dan tam olarak biri verilmeli.");
            var o = orderId is { } id ? await api.GetOrderAsync(id, ct) : await api.GetOrderByNumberAsync(number!, ct);
            return new OrderView(o.Id, o.OrderNumber, o.Status, o.TotalAmount, o.Currency, o.CreatedAt, o.PaidAt, o.ShippedAt, o.DeliveredAt,
                o.ShippingAddress,
                (o.SellerOrders ?? []).Select(s => new SellerOrderView(s.Id, s.SellerName, s.Status, s.Total,
                    (s.Items ?? []).Select(i => new OrderItemView(i.ProductName, i.Quantity, i.UnitPrice)).ToList(),
                    s.Shipment is null ? null : new ShipmentView(s.Shipment.Carrier, s.Shipment.TrackingNumber, s.Shipment.ShippedAt, s.Shipment.DeliveredAt))).ToList());
        }, notFound: $"Sipariş bulunamadı ({label})");
    }

    private sealed record SearchItemView(int ListingId, string? ProductName, decimal Price, int Stock, string? StockStatus, string? Condition, string? Seller, string? Category);
    private sealed record SearchView(int Page, int TotalPages, int TotalCount, IReadOnlyList<SearchItemView> Items);
    private sealed record ListingView(int ListingId, int ProductId, string? ProductName, string? Description, decimal Price, int Stock, string? Condition, bool IsActive, int SellerId);
    private sealed record AddressView(int AddressId, string? Title, string? FullName, string? FullAddress, bool IsDefault, string? Type);
    private sealed record OrderListItemView(int OrderId, string? OrderNumber, string? Status, decimal TotalAmount, string? Currency, int ItemCount, DateTime CreatedAt);
    private sealed record OrderListView(int Page, int TotalCount, IReadOnlyList<OrderListItemView> Items);
    private sealed record OrderItemView(string? ProductName, int Quantity, decimal UnitPrice);
    private sealed record ShipmentView(string? Carrier, string? TrackingNumber, DateTime? ShippedAt, DateTime? DeliveredAt);
    private sealed record SellerOrderView(int SellerOrderId, string? SellerName, string? Status, decimal Total, IReadOnlyList<OrderItemView> Items, ShipmentView? Shipment);
    private sealed record OrderView(int OrderId, string? OrderNumber, string? Status, decimal TotalAmount, string? Currency, DateTime CreatedAt, DateTime? PaidAt, DateTime? ShippedAt, DateTime? DeliveredAt, string? ShippingAddress, IReadOnlyList<SellerOrderView> SellerOrders);
}
