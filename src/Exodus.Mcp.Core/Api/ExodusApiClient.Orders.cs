using Exodus.Mcp.Core.Api.Dtos;

namespace Exodus.Mcp.Core.Api;

public sealed partial class ExodusApiClient
{
    /// <summary><c>GET api/order?status=&amp;page=&amp;pageSize=</c>.</summary>
    public Task<OrderListDto> GetOrdersAsync(string? status, int page, int pageSize, CancellationToken ct)
        => SendAsync<OrderListDto>(HttpMethod.Get,
            QueryString.Build("api/order", ("status", status), ("page", page), ("pageSize", pageSize)), jsonBody: null, ct);

    /// <summary><c>GET api/order/{id}</c>.</summary>
    public Task<OrderDetailDto> GetOrderAsync(int id, CancellationToken ct)
        => SendAsync<OrderDetailDto>(HttpMethod.Get, $"api/order/{id}", jsonBody: null, ct);

    /// <summary><c>GET api/order/number/{orderNumber}</c>.</summary>
    public Task<OrderDetailDto> GetOrderByNumberAsync(string orderNumber, CancellationToken ct)
        => SendAsync<OrderDetailDto>(HttpMethod.Get, $"api/order/number/{Uri.EscapeDataString(orderNumber)}", jsonBody: null, ct);

    /// <summary><c>POST api/order/checkout</c>: 201 with the order. Exodus ignores a coupon code, so none is sent.</summary>
    public Task<OrderDetailDto> CheckoutAsync(int shippingAddressId, int? billingAddressId, string? customerNote, CancellationToken ct)
        => SendAsync<OrderDetailDto>(HttpMethod.Post, "api/order/checkout", new { shippingAddressId, billingAddressId, customerNote }, ct);

    /// <summary><c>POST api/payment/intents</c> with the cash-on-delivery method (no card data).</summary>
    public Task<PaymentIntentDto> CreateCashOnDeliveryIntentAsync(int orderId, CancellationToken ct)
        => SendAsync<PaymentIntentDto>(HttpMethod.Post, "api/payment/intents", new { orderId, method = "CashOnDelivery" }, ct);

    /// <summary><c>POST api/order/{id}/cancel</c>: 200 with the order.</summary>
    public Task<OrderDetailDto> CancelOrderAsync(int orderId, string reason, string? note, CancellationToken ct)
        => SendAsync<OrderDetailDto>(HttpMethod.Post, $"api/order/{orderId}/cancel", new { reason, note }, ct);
}
