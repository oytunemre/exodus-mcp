using System.ComponentModel;
using Exodus.Mcp.Core.Api;
using Exodus.Mcp.Core.Configuration;
using ModelContextProtocol.Server;

namespace Exodus.Mcp.Core.Tools;

/// <summary>Tools available in every profile.</summary>
[McpServerToolType]
public sealed class CommonTools(ExodusApiClient api, ExodusOptions options)
{
    [McpServerTool(Name = "whoami", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Bağlı Exodus hesabını (kullanıcı no, kullanıcı adı, e-posta, rol), MCP profilini (Customer/Seller) ve Exodus adresini gösterir.")]
    public Task<string> WhoAmI(CancellationToken ct) => ToolErrors.RunAsync(options, async () =>
    {
        var me = await api.GetMeAsync(ct);
        return new WhoAmIView(me.UserId, me.Username, me.Email, me.Role, options.Profile.ToString(), options.BaseUrl.ToString());
    });

    private sealed record WhoAmIView(string UserId, string? Username, string? Email, string? Role, string Profile, string ExodusBaseUrl);
}
