namespace Exodus.Mcp.Core.Api.Dtos;

/// <summary>Query of <c>GET api/listings/search</c>; values are Exodus parameter values (e.g. <c>createdAt</c>, not <c>newest</c>).</summary>
public sealed record ListingSearchQuery(
    string? Search, int? CategoryId, decimal? MinPrice, decimal? MaxPrice, string? Condition,
    string SortBy, string SortOrder, int Page, int PageSize);

/// <summary>Response of <c>GET api/listings/search</c>.</summary>
public sealed record ListingSearchResultDto(List<ListingListItemDto>? Items, int TotalCount, int Page, int PageSize, int TotalPages);

/// <summary>Search item: stock is <c>stockQuantity</c> here (but <c>stock</c> in <see cref="ListingDto"/>).</summary>
public sealed record ListingListItemDto(
    int Id, int ProductId, string? ProductName, int? CategoryId, string? CategoryName, decimal Price,
    int StockQuantity, string? StockStatus, string? Condition, int SellerId, string? SellerName, bool IsActive);

/// <summary>Response of <c>GET api/listings/{id}</c>.</summary>
public sealed record ListingDto(int Id, int ProductId, int SellerId, decimal Price, int Stock, string? Condition, bool IsActive);

/// <summary>Response of <c>GET api/product/{id}</c>.</summary>
public sealed record ProductDto(int Id, string? ProductName, string? ProductDescription);

/// <summary>Node of <c>GET api/category/tree</c>.</summary>
public sealed record CategoryTreeDto(int Id, string? Name, int ProductCount, List<CategoryTreeDto>? Children);

/// <summary>Item of <c>GET api/address</c>.</summary>
public sealed record AddressDto(int Id, string? Title, string? FullName, string? FullAddress, bool IsDefault, string? Type);
