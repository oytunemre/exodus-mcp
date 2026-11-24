namespace Exodus.Mcp.Core.Api.Dtos;

/// <summary>Response of <c>GET api/seller/listings</c> (paged).</summary>
public sealed record SellerListingListDto(List<SellerListingItemDto>? Items, int TotalCount, int Page, int PageSize, int TotalPages);

/// <summary>Item of <c>GET api/seller/listings</c> and the 201 body of <c>POST api/seller/listings</c>.</summary>
public sealed record SellerListingItemDto(
    int Id, int ProductId, string? ProductName, string? PrimaryImage, decimal Price, int StockQuantity, string? StockStatus,
    int LowStockThreshold, string? Sku, string? Condition, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);

/// <summary>Item of <c>GET api/seller/listings/low-stock</c>.</summary>
public sealed record SellerLowStockItemDto(int Id, int ProductId, string? ProductName, int StockQuantity, int LowStockThreshold, string? Sku);

/// <summary>Item of <c>GET api/seller/listings/out-of-stock</c>.</summary>
public sealed record SellerOutOfStockItemDto(int Id, int ProductId, string? ProductName, string? Sku, bool IsActive);

/// <summary>201 body of <c>POST api/product</c>.</summary>
public sealed record ProductCreatedDto(int Id, string? ProductName, string? ProductDescription, List<string>? Barcodes);

/// <summary>Response of <c>PATCH api/seller/listings/{id}/stock</c>.</summary>
public sealed record SellerStockDto(string? Message, int StockQuantity, string? StockStatus);

/// <summary>Item of <c>GET api/seller/orders</c> and body of <c>GET api/seller/orders/{id}</c> (mirrors Exodus <c>SellerOrderListItemDto</c>).</summary>
public sealed record SellerOrderDto(
    int Id, int OrderId, string? OrderNumber, DateTime CreatedAt, int SellerId, string? SellerName, string? Status, decimal Total,
    List<SellerOrderLineDto>? Items, SellerShipmentDto? Shipment);

/// <summary>Line of a <see cref="SellerOrderDto"/>.</summary>
public sealed record SellerOrderLineDto(
    int Id, int ListingId, int ProductId, string? ProductName, int Quantity, decimal UnitPrice, decimal TotalPrice);

/// <summary>Shipment of a <see cref="SellerOrderDto"/>; null on the order until it is shipped.</summary>
public sealed record SellerShipmentDto(int Id, string? Carrier, string? TrackingNumber, DateTime? ShippedAt, DateTime? DeliveredAt);

/// <summary>Item of <c>GET api/shipments/seller-orders/{id}/timeline</c> (Exodus <c>ShipmentEventDto</c>).</summary>
public sealed record SellerShipmentEventDto(int Id, int ShipmentId, string? Status, string? PayloadJson, DateTime CreatedAt);
