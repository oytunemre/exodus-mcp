using Exodus.Mcp.Core.Api.Dtos;

namespace Exodus.Mcp.Core.Api;

public sealed partial class ExodusApiClient
{
    /// <summary><c>GET api/listings/search</c>.</summary>
    public Task<ListingSearchResultDto> SearchListingsAsync(ListingSearchQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var path = QueryString.Build("api/listings/search",
            ("search", q.Search), ("categoryId", q.CategoryId), ("minPrice", q.MinPrice), ("maxPrice", q.MaxPrice),
            ("condition", q.Condition), ("sortBy", q.SortBy), ("sortOrder", q.SortOrder), ("page", q.Page), ("pageSize", q.PageSize));
        return SendAsync<ListingSearchResultDto>(HttpMethod.Get, path, jsonBody: null, ct);
    }

    /// <summary><c>GET api/listings/{id}</c>.</summary>
    public Task<ListingDto> GetListingAsync(int id, CancellationToken ct)
        => SendAsync<ListingDto>(HttpMethod.Get, $"api/listings/{id}", jsonBody: null, ct);

    /// <summary><c>GET api/product/{id}</c>.</summary>
    public Task<ProductDto> GetProductAsync(int id, CancellationToken ct)
        => SendAsync<ProductDto>(HttpMethod.Get, $"api/product/{id}", jsonBody: null, ct);

    /// <summary><c>GET api/category/tree</c>: a bare array.</summary>
    public Task<List<CategoryTreeDto>> GetCategoryTreeAsync(CancellationToken ct)
        => SendAsync<List<CategoryTreeDto>>(HttpMethod.Get, "api/category/tree", jsonBody: null, ct);

    /// <summary><c>GET api/address</c>.</summary>
    public Task<List<AddressDto>> GetAddressesAsync(CancellationToken ct)
        => SendAsync<List<AddressDto>>(HttpMethod.Get, "api/address", jsonBody: null, ct);
}
