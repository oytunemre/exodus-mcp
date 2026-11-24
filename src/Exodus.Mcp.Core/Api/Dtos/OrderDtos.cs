namespace Exodus.Mcp.Core.Api.Dtos;

/// <summary>Response of <c>GET api/order</c> (no totalPages).</summary>
public sealed record OrderListDto(List<OrderListItemDto>? Items, int TotalCount, int Page, int PageSize);

public sealed record OrderListItemDto(int Id, string? OrderNumber, string? Status, decimal TotalAmount, string? Currency, int ItemCount, DateTime CreatedAt);

/// <summary>Response of <c>GET api/order/{id}</c> and <c>GET api/order/number/{orderNumber}</c>.</summary>
public sealed record OrderDetailDto(
    int Id, string? OrderNumber, string? Status, decimal TotalAmount, string? Currency, string? ShippingAddress,
    DateTime CreatedAt, DateTime? PaidAt, DateTime? ShippedAt, DateTime? DeliveredAt, List<SellerOrderDto>? SellerOrders);

public sealed record SellerOrderDto(int Id, string? SellerName, string? Status, decimal Total, List<OrderItemDto>? Items, ShipmentInfoDto? Shipment);

public sealed record OrderItemDto(string? ProductName, int Quantity, decimal UnitPrice);

public sealed record ShipmentInfoDto(string? Carrier, string? TrackingNumber, DateTime? ShippedAt, DateTime? DeliveredAt);
