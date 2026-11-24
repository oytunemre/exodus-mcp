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
