using Exodus.Mcp.Core.Api.Dtos;

namespace Exodus.Mcp.Core.Api;

public sealed partial class ExodusApiClient
{
    /// <summary><c>GET api/seller/listings?isActive=&amp;page=&amp;pageSize=</c>.</summary>
    public Task<SellerListingListDto> GetMyListingsAsync(bool? isActive, int page, int pageSize, CancellationToken ct)
        => SendAsync<SellerListingListDto>(HttpMethod.Get,
            QueryString.Build("api/seller/listings", ("isActive", isActive), ("page", page), ("pageSize", pageSize)), jsonBody: null, ct);

    /// <summary><c>GET api/seller/listings/low-stock</c>: a bare array.</summary>
    public Task<List<SellerLowStockItemDto>> GetLowStockAsync(CancellationToken ct)
        => SendAsync<List<SellerLowStockItemDto>>(HttpMethod.Get, "api/seller/listings/low-stock", jsonBody: null, ct);

    /// <summary><c>GET api/seller/listings/out-of-stock</c>: a bare array.</summary>
    public Task<List<SellerOutOfStockItemDto>> GetOutOfStockAsync(CancellationToken ct)
        => SendAsync<List<SellerOutOfStockItemDto>>(HttpMethod.Get, "api/seller/listings/out-of-stock", jsonBody: null, ct);

    /// <summary><c>POST api/product</c>: 201 with the product.</summary>
    public Task<ProductCreatedDto> CreateProductAsync(string name, string description, string barcode, CancellationToken ct)
        => SendAsync<ProductCreatedDto>(HttpMethod.Post, "api/product",
            new { productName = name, productDescription = description, barcodes = new[] { barcode } }, ct);

    /// <summary><c>POST api/seller/listings</c>: 201 with the listing.</summary>
    public Task<SellerListingItemDto> CreateSellerListingAsync(int productId, decimal price, int stockQuantity, string condition, CancellationToken ct)
        => SendAsync<SellerListingItemDto>(HttpMethod.Post, "api/seller/listings", new { productId, price, stockQuantity, condition }, ct);

    /// <summary><c>PUT api/seller/listings/{id}</c>: partial update, null fields are not sent.</summary>
    public Task UpdateSellerListingAsync(int id, decimal? price, bool? isActive, int? lowStockThreshold, CancellationToken ct)
    {
        var body = new Dictionary<string, object>();
        if (price is { } p) body["price"] = p;
        if (isActive is { } a) body["isActive"] = a;
        if (lowStockThreshold is { } t) body["lowStockThreshold"] = t;
        return SendAsync(HttpMethod.Put, $"api/seller/listings/{id}", body, ct);
    }

    /// <summary><c>PATCH api/seller/listings/{id}/stock</c>: sets the absolute stock.</summary>
    public Task<SellerStockDto> UpdateStockAsync(int id, int stockQuantity, CancellationToken ct)
        => SendAsync<SellerStockDto>(HttpMethod.Patch, $"api/seller/listings/{id}/stock", new { stockQuantity }, ct);

    /// <summary><c>GET api/seller/orders?status=&amp;page=&amp;pageSize=</c>: a bare array, newest first.</summary>
    public async Task<IReadOnlyList<SellerOrderDto>> GetSellerOrdersAsync(string? status, int page, int pageSize, CancellationToken ct)
        => await SendAsync<List<SellerOrderDto>>(HttpMethod.Get,
            QueryString.Build("api/seller/orders", ("status", status), ("page", page), ("pageSize", pageSize)), jsonBody: null, ct);

    /// <summary><c>GET api/seller/orders/{id}</c>.</summary>
    public Task<SellerOrderDto> GetSellerOrderAsync(int id, CancellationToken ct)
        => SendAsync<SellerOrderDto>(HttpMethod.Get, $"api/seller/orders/{id}", jsonBody: null, ct);

    /// <summary><c>GET api/shipments/seller-orders/{id}/timeline</c>: a bare array.</summary>
    public Task<List<SellerShipmentEventDto>> GetShipmentTimelineAsync(int sellerOrderId, CancellationToken ct)
        => SendAsync<List<SellerShipmentEventDto>>(HttpMethod.Get, $"api/shipments/seller-orders/{sellerOrderId}/timeline", jsonBody: null, ct);

    /// <summary><c>POST api/seller/orders/{id}/confirm</c>.</summary>
    public Task ConfirmSellerOrderAsync(int id, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"api/seller/orders/{id}/confirm", jsonBody: null, ct);

    /// <summary><c>POST api/seller/orders/{id}/ship</c>.</summary>
    public Task ShipSellerOrderAsync(int id, string carrier, string trackingNumber, CancellationToken ct)
        => SendAsync(HttpMethod.Post, $"api/seller/orders/{id}/ship", new { carrier, trackingNumber }, ct);

    /// <summary><c>PATCH api/shipments/seller-orders/{id}/deliver</c>.</summary>
    public Task MarkDeliveredAsync(int id, CancellationToken ct)
        => SendAsync(HttpMethod.Patch, $"api/shipments/seller-orders/{id}/deliver", jsonBody: null, ct);
}
