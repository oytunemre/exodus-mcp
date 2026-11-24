using Exodus.Mcp.Core.Api.Dtos;

namespace Exodus.Mcp.Core.Api;

public sealed partial class ExodusApiClient
{
    /// <summary><c>GET api/auth/me</c>: the account behind the current session.</summary>
    public Task<MeDto> GetMeAsync(CancellationToken ct)
        => SendAsync<MeDto>(HttpMethod.Get, "api/auth/me", jsonBody: null, ct);
}
