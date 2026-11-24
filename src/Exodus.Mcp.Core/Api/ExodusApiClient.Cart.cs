using Exodus.Mcp.Core.Api.Dtos;

namespace Exodus.Mcp.Core.Api;

public sealed partial class ExodusApiClient
{
    /// <summary><c>GET api/cart/my</c>.</summary>
    public Task<CartDto> GetCartAsync(CancellationToken ct)
        => SendAsync<CartDto>(HttpMethod.Get, "api/cart/my", jsonBody: null, ct);

    /// <summary><c>POST api/cart/add</c>.</summary>
    public Task<CartDto> AddToCartAsync(int userId, int listingId, int quantity, CancellationToken ct)
        => SendAsync<CartDto>(HttpMethod.Post, "api/cart/add", new { userId, listingId, quantity }, ct);

    /// <summary><c>PUT api/cart/{userId}/item/{cartItemId}</c>.</summary>
    public Task<CartDto> UpdateCartItemAsync(int userId, int cartItemId, int quantity, CancellationToken ct)
        => SendAsync<CartDto>(HttpMethod.Put, $"api/cart/{userId}/item/{cartItemId}", new { quantity }, ct);

    /// <summary><c>DELETE api/cart/{userId}/item/{cartItemId}</c>: 200 with the cart.</summary>
    public Task<CartDto> RemoveCartItemAsync(int userId, int cartItemId, CancellationToken ct)
        => SendAsync<CartDto>(HttpMethod.Delete, $"api/cart/{userId}/item/{cartItemId}", jsonBody: null, ct);
}
