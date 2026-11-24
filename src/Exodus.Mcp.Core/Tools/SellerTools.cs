using System.ComponentModel;
using System.Globalization;
using Exodus.Mcp.Core.Api;
using Exodus.Mcp.Core.Api.Dtos;
using Exodus.Mcp.Core.Configuration;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Exodus.Mcp.Core.Tools;

/// <summary>Seller-profile tools (listing and order management).</summary>
[McpServerToolType]
public sealed class SellerTools(ExodusApiClient api, ExodusOptions options, TimeProvider time)
{
    private const int PageSize = 20;
    private static readonly string[] StockFilters = ["all", "low", "out"];
    private const int SummaryPageSize = 100;
    private const int SummaryMaxPages = 20;
    private static readonly string[] OrderStatuses = ["Pending", "Confirmed", "Processing", "Shipped", "Delivered", "Cancelled"];
    private static readonly string[] Conditions = ["New", "LikeNew", "Used", "Refurbished"];

    [McpServerTool(Name = "list_my_listings", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Satıcının kendi listing'lerini listeler (sayfa başına 20). stockFilter: all (varsayılan), low (stoğu azalanlar) veya out (stoğu bitenler). " +
                 "isActive ve page yalnızca all için kullanılır; low ve out tüm sonuçları tek seferde döndürür.")]
    public Task<string> ListMyListings(
        [Description("Stok filtresi: all, low, out. Varsayılan: all.")] string? stockFilter = "all",
        [Description("Yalnızca all için: true ise aktif, false ise pasif listing'ler. Boşsa hepsi.")] bool? isActive = null,
        [Description("Yalnızca all için: sayfa numarası, 1'den başlar. Varsayılan: 1.")] int page = 1,
        CancellationToken ct = default) => ToolErrors.RunAsync<object>(options, async () =>
    {
        var filter = ToolInput.OneOf(stockFilter, "stockFilter", StockFilters, "all")!;
        ToolInput.Require(page >= 1, "page en az 1 olmalı.");

        switch (filter)
        {
            case "low":
                var low = (await api.GetLowStockAsync(ct)).Select(MapLow).ToList();
                return new FilterView<LowItemView>(filter, null, null, low.Count, low);
            case "out":
                var outOfStock = (await api.GetOutOfStockAsync(ct)).Select(MapOut).ToList();
                return new FilterView<OutItemView>(filter, null, null, outOfStock.Count, outOfStock);
            default:
                var result = await api.GetMyListingsAsync(isActive, page, PageSize, ct);
                var totalPages = result.TotalPages > 0 ? result.TotalPages : (int)Math.Ceiling(result.TotalCount / (double)PageSize);
                return new FilterView<AllItemView>(filter, result.Page, totalPages, result.TotalCount,
                    (result.Items ?? []).Select(MapAll).ToList());
        }
    });

    [McpServerTool(Name = "create_listing", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Yeni listing oluşturur. Ya mevcut ürün için productId verin ya da yeni ürün için newProductName ve newProductBarcode verin (tam olarak biri). " +
                 "Yeni üründe newProductDescription boşsa ürün adı açıklama olarak kullanılır. Ürün oluşturulup listing oluşturulamazsa hata mesajındaki productId ile tekrar deneyin; ürünü yeniden oluşturmayın.")]
    public Task<string> CreateListing(
        [Description("Fiyat (TRY), 0'dan büyük.")] decimal price,
        [Description("Stok adedi, 0 veya daha büyük.")] int stockQuantity,
        [Description("Mevcut ürün no. newProductName ile birlikte verilmez.")] int? productId = null,
        [Description("Yeni ürünün adı. productId ile birlikte verilmez; newProductBarcode zorunlu.")] string? newProductName = null,
        [Description("Yeni ürünün açıklaması (en az 5 karakter). Boşsa ürün adı kullanılır.")] string? newProductDescription = null,
        [Description("Yeni ürünün barkodu. newProductName verildiyse zorunlu.")] string? newProductBarcode = null,
        [Description("Ürün durumu: New, LikeNew, Used, Refurbished. Varsayılan: New.")] string? condition = "New",
        CancellationToken ct = default) => ToolErrors.RunAsync(options, async () =>
    {
        var name = string.IsNullOrWhiteSpace(newProductName) ? null : newProductName.Trim();
        var barcode = string.IsNullOrWhiteSpace(newProductBarcode) ? null : newProductBarcode.Trim();
        var cond = ToolInput.OneOf(condition, "condition", Conditions, "New")!;
        ToolInput.Require((productId is not null) ^ (name is not null), "productId ve newProductName'den tam olarak biri verilmeli.");
        ToolInput.Require(name is null || barcode is not null, "newProductName verildiyse newProductBarcode zorunlu.");
        ToolInput.Require(price > 0, "price 0'dan büyük olmalı.");
        ToolInput.Require(stockQuantity >= 0, "stockQuantity 0 veya daha büyük olmalı.");

        int targetProductId;
        int? createdProductId = null;
        if (name is not null)
        {
            var description = string.IsNullOrWhiteSpace(newProductDescription) ? name : newProductDescription.Trim();
            var product = await api.CreateProductAsync(name, description, barcode!, ct);
            createdProductId = product.Id;
            targetProductId = product.Id;
        }
        else
        {
            targetProductId = productId!.Value;
        }

        SellerListingItemDto listing;
        try
        {
            listing = await api.CreateSellerListingAsync(targetProductId, price, stockQuantity, cond, ct);
        }
        catch (Exception error) when (createdProductId is not null)
        {
            var translated = ToolErrors.Translate(error, options);
            if (translated is OperationCanceledException)
            {
                throw;
            }

            var id = createdProductId.Value.ToString(CultureInfo.InvariantCulture);
            throw new McpException(
                $"Ürün oluşturuldu (productId={id}) ancak listing oluşturulamadı: {translated.Message.TrimEnd('.')}. " +
                $"Tekrar denerken productId={id} kullanın.", error);
        }

        return new CreatedView(listing.Id, listing.ProductId, listing.ProductName, listing.Price, listing.StockQuantity,
            listing.StockStatus, listing.Condition, listing.IsActive, createdProductId);
    }, notFound: "Ürün bulunamadı", forbidden: "Bu işlem için satıcı yetkisi gerekli");

    [McpServerTool(Name = "update_listing", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Listing'in fiyatını, aktiflik durumunu ve/veya düşük stok eşiğini günceller. Yalnızca verilen alanlar değişir; en az biri verilmelidir. Stok için update_stock kullanın.")]
    public Task<string> UpdateListing(
        [Description("Listing no (list_my_listings sonucundaki listingId).")] int listingId,
        [Description("Yeni fiyat (TRY), 0'dan büyük.")] decimal? price = null,
        [Description("true: listing'i aktif yap, false: pasif yap.")] bool? isActive = null,
        [Description("Düşük stok eşiği, 0 veya daha büyük.")] int? lowStockThreshold = null,
        CancellationToken ct = default) => ToolErrors.RunAsync(options, async () =>
    {
        ToolInput.Require(price is not null || isActive is not null || lowStockThreshold is not null,
            "price, isActive ve lowStockThreshold'dan en az biri verilmeli.");
        ToolInput.Require(price is null or > 0, "price 0'dan büyük olmalı.");
        ToolInput.Require(lowStockThreshold is null or >= 0, "lowStockThreshold 0 veya daha büyük olmalı.");

        await api.UpdateSellerListingAsync(listingId, price, isActive, lowStockThreshold, ct);
        return new UpdatedView(listingId, new UpdatedFields(price, isActive, lowStockThreshold));
    }, notFound: $"Listing bulunamadı ya da size ait değil (id={listingId})");

    [McpServerTool(Name = "update_stock", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Listing'in stoğunu MUTLAK değere ayarlar (artış/azalış miktarı değil): stockQuantity yeni toplam stoktur.")]
    public Task<string> UpdateStock(
        [Description("Listing no (list_my_listings sonucundaki listingId).")] int listingId,
        [Description("Yeni toplam stok adedi (mutlak değer, delta değil), 0 veya daha büyük.")] int stockQuantity,
        CancellationToken ct = default) => ToolErrors.RunAsync(options, async () =>
    {
        ToolInput.Require(stockQuantity >= 0, "stockQuantity 0 veya daha büyük olmalı.");
        var result = await api.UpdateStockAsync(listingId, stockQuantity, ct);
        return new StockView(listingId, result.StockQuantity, result.StockStatus);
    }, notFound: $"Listing bulunamadı ya da size ait değil (id={listingId})");

    [McpServerTool(Name = "list_seller_orders", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Satıcının siparişlerini listeler, en yeniden eskiye (sayfa başına 20). status: Pending (onay bekliyor), Confirmed, Processing, Shipped, Delivered, Cancelled; boşsa hepsi. " +
                 "hasMore true ise sonraki sayfayı page ile isteyin.")]
    public Task<string> ListSellerOrders(
        [Description("Durum filtresi: Pending (onay bekliyor), Confirmed, Processing, Shipped, Delivered, Cancelled. Boşsa hepsi.")] string? status = null,
        [Description("Sayfa numarası, 1'den başlar. Varsayılan: 1.")] int page = 1,
        CancellationToken ct = default) => ToolErrors.RunAsync(options, async () =>
    {
        var filter = ToolInput.OneOf(status, "status", OrderStatuses);
        ToolInput.Require(page >= 1, "page en az 1 olmalı.");

        var orders = await api.GetSellerOrdersAsync(filter, page, PageSize, ct);
        return new OrderListView(page, orders.Count == PageSize, orders.Select(MapListItem).ToList());
    });

    [McpServerTool(Name = "get_seller_order", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Tek bir satıcı siparişinin kalemlerini, kargo bilgisini ve (kargolandıysa) kargo geçmişini döndürür.")]
    public Task<string> GetSellerOrder(
        [Description("Satıcı sipariş no (list_seller_orders sonucundaki sellerOrderId).")] int sellerOrderId,
        CancellationToken ct = default) => ToolErrors.RunAsync(options, () => LoadOrderViewAsync(sellerOrderId, ct),
        notFound: OrderNotFound(sellerOrderId));

    [McpServerTool(Name = "confirm_order", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Onay bekleyen (Pending) satıcı siparişini onaylar ve güncel sipariş görünümünü döndürür.")]
    public Task<string> ConfirmOrder(
        [Description("Satıcı sipariş no.")] int sellerOrderId,
        CancellationToken ct = default) => ToolErrors.RunAsync(options, async () =>
    {
        await api.ConfirmSellerOrderAsync(sellerOrderId, ct);
        return await LoadOrderViewAsync(sellerOrderId, ct);
    }, notFound: OrderNotFound(sellerOrderId), forbidden: NotOwned(sellerOrderId));

    [McpServerTool(Name = "ship_order", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Satıcı siparişini kargoya verildi olarak işaretler. carrier (kargo firması) ve trackingNumber (takip no) boş olamaz. Güncel sipariş görünümünü döndürür.")]
    public Task<string> ShipOrder(
        [Description("Satıcı sipariş no.")] int sellerOrderId,
        [Description("Kargo firması, boş olamaz.")] string carrier,
        [Description("Kargo takip numarası, boş olamaz.")] string trackingNumber,
        CancellationToken ct = default) => ToolErrors.RunAsync(options, async () =>
    {
        ToolInput.Require(!string.IsNullOrWhiteSpace(carrier), "carrier boş olamaz.");
        ToolInput.Require(!string.IsNullOrWhiteSpace(trackingNumber), "trackingNumber boş olamaz.");

        await api.ShipSellerOrderAsync(sellerOrderId, carrier.Trim(), trackingNumber.Trim(), ct);
        return await LoadOrderViewAsync(sellerOrderId, ct);
    }, notFound: OrderNotFound(sellerOrderId), forbidden: NotOwned(sellerOrderId));

    [McpServerTool(Name = "mark_delivered", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Kargolanmış satıcı siparişini teslim edildi olarak işaretler. Sipariş henüz kargolanmadıysa Exodus'un hata mesajı döner. Güncel sipariş görünümünü döndürür.")]
    public Task<string> MarkDelivered(
        [Description("Satıcı sipariş no.")] int sellerOrderId,
        CancellationToken ct = default) => ToolErrors.RunAsync(options, async () =>
    {
        await api.MarkDeliveredAsync(sellerOrderId, ct);
        return await LoadOrderViewAsync(sellerOrderId, ct);
    }, notFound: OrderNotFound(sellerOrderId), forbidden: NotOwned(sellerOrderId));

    [McpServerTool(Name = "sales_summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Tarih aralığındaki satış özeti: sipariş sayısı, ciro (iptaller hariç), durum dağılımı ve en çok satan 5 listing. " +
                 "Tarihler yyyy-MM-dd (UTC), uçlar dahil. Varsayılan: toDate bugün, fromDate toDate'ten 29 gün önce. En fazla 2000 sipariş taranır; fazlaysa truncated=true.")]
    public Task<string> SalesSummary(
        [Description("Başlangıç tarihi, yyyy-MM-dd. Varsayılan: toDate - 29 gün.")] string? fromDate = null,
        [Description("Bitiş tarihi, yyyy-MM-dd (dahil). Varsayılan: bugün (UTC).")] string? toDate = null,
        CancellationToken ct = default) => ToolErrors.RunAsync(options, async () =>
    {
        var to = string.IsNullOrWhiteSpace(toDate)
            ? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)
            : ToolInput.Date(toDate, "toDate");
        var from = string.IsNullOrWhiteSpace(fromDate) ? to.AddDays(-29) : ToolInput.Date(fromDate, "fromDate");
        ToolInput.Require(from <= to, "fromDate, toDate'ten sonra olamaz.");

        var rangeStart = new DateTime(from.Year, from.Month, from.Day, 0, 0, 0, DateTimeKind.Utc);
        var collected = new List<SellerOrderDto>();
        var truncated = false;
        for (var page = 1; ; page++)
        {
            var batch = await api.GetSellerOrdersAsync(null, page, SummaryPageSize, ct);
            collected.AddRange(batch);
            if (batch.Count < SummaryPageSize
                || batch.Min(o => o.CreatedAt) < rangeStart)
            {
                break;
            }

            if (page >= SummaryMaxPages)
            {
                truncated = true;
                break;
            }
        }

        return SalesSummaryCalculator.Calculate(collected, from, to, truncated);
    });

    private async Task<OrderView> LoadOrderViewAsync(int sellerOrderId, CancellationToken ct)
    {
        var order = await api.GetSellerOrderAsync(sellerOrderId, ct);
        var timeline = order.Shipment is null
            ? null
            : (await api.GetShipmentTimelineAsync(sellerOrderId, ct))
                .Select(e => new TimelineView(e.Status, e.CreatedAt)).ToList();
        return MapOrder(order, timeline);
    }

    private static string OrderNotFound(int id) => $"Satıcı siparişi bulunamadı (id={id})";

    private static string NotOwned(int id) => $"Bu satıcı siparişi size ait değil (id={id})";

    private static OrderListItemView MapListItem(SellerOrderDto o) =>
        new(o.Id, o.OrderNumber, o.Status, o.Total, o.Items?.Sum(i => i.Quantity) ?? 0, o.CreatedAt);

    private static OrderView MapOrder(SellerOrderDto o, IReadOnlyList<TimelineView>? timeline) =>
        new(o.Id, o.OrderNumber, o.Status, o.Total, o.CreatedAt,
            (o.Items ?? []).Select(i => new OrderLineView(i.ListingId, i.ProductName, i.Quantity, i.UnitPrice, i.TotalPrice)).ToList(),
            o.Shipment is null ? null : new ShipmentView(o.Shipment.Carrier, o.Shipment.TrackingNumber,
                o.Shipment.ShippedAt,
                o.Shipment.DeliveredAt),
            timeline);

    private sealed record OrderListItemView(int SellerOrderId, string? OrderNumber, string? Status, decimal Total, int ItemCount, DateTime CreatedAt);
    private sealed record OrderListView(int Page, bool HasMore, IReadOnlyList<OrderListItemView> Items);
    private sealed record OrderLineView(int ListingId, string? ProductName, int Quantity, decimal UnitPrice, decimal TotalPrice);
    private sealed record ShipmentView(string? Carrier, string? TrackingNumber, DateTime? ShippedAt, DateTime? DeliveredAt);
    private sealed record TimelineView(string? Status, DateTime At);
    private sealed record OrderView(int SellerOrderId, string? OrderNumber, string? Status, decimal Total, DateTime CreatedAt,
        IReadOnlyList<OrderLineView> Items, ShipmentView? Shipment, IReadOnlyList<TimelineView>? Timeline);

    private static AllItemView MapAll(SellerListingItemDto i) =>
        new(i.Id, i.ProductName, i.Price, i.StockQuantity, i.StockStatus, i.LowStockThreshold, i.Condition, i.IsActive);

    private static LowItemView MapLow(SellerLowStockItemDto i) => new(i.Id, i.ProductName, i.StockQuantity, i.LowStockThreshold);

    private static OutItemView MapOut(SellerOutOfStockItemDto i) => new(i.Id, i.ProductName, i.IsActive);

    private sealed record FilterView<T>(string Filter, int? Page, int? TotalPages, int TotalCount, IReadOnlyList<T> Items);
    private sealed record AllItemView(int ListingId, string? ProductName, decimal Price, int Stock, string? StockStatus, int LowStockThreshold, string? Condition, bool IsActive);
    private sealed record LowItemView(int ListingId, string? ProductName, int Stock, int LowStockThreshold);
    private sealed record OutItemView(int ListingId, string? ProductName, bool IsActive);
    private sealed record CreatedView(int ListingId, int ProductId, string? ProductName, decimal Price, int Stock, string? StockStatus, string? Condition, bool IsActive, int? CreatedProductId);
    private sealed record UpdatedFields(decimal? Price, bool? IsActive, int? LowStockThreshold);
    private sealed record UpdatedView(int ListingId, UpdatedFields Updated);
    private sealed record StockView(int ListingId, int Stock, string? StockStatus);
}
