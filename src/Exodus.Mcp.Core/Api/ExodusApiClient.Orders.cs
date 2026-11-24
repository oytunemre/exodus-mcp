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
}
