namespace Exodus.Mcp.Core.Api.Dtos;

/// <summary>Cart response of <c>GET api/cart/my</c> and the cart write endpoints.</summary>
public sealed record CartDto(int CartId, int UserId, decimal CartTotal, List<CartItemDto>? Items);

public sealed record CartItemDto(int CartItemId, int ListingId, int ProductId, string? ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

/// <summary>Response of <c>POST api/payment/intents</c> (only the fields the tools use; no card data).</summary>
public sealed record PaymentIntentDto(int Id, int OrderId, string? Method, string? Status);
