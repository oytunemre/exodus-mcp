using Exodus.Mcp.Core.Api;
using Exodus.Mcp.Core.Configuration;
using Exodus.Mcp.Core.Tools;
using ModelContextProtocol.Server;

namespace Exodus.Mcp.Core.Resources;

/// <summary>Catalog data exposed as MCP resources (both profiles).</summary>
[McpServerResourceType]
public sealed class CatalogResources(ExodusApiClient api, ExodusOptions options)
{
    [McpServerResource(UriTemplate = "exodus://categories", Name = "categories", MimeType = "application/json")]
    [System.ComponentModel.Description("Exodus kategori ağacı (kategori no, ad, ürün sayısı, alt kategoriler); list_categories ile aynı veri.")]
    public async Task<string> Categories(CancellationToken ct)
    {
        try
        {
            return ToolJson.Serialize(CategoryView.Map(await api.GetCategoryTreeAsync(ct)));
        }
        catch (Exception error)
        {
            var translated = ToolErrors.Translate(error, options);
            if (ReferenceEquals(translated, error))
            {
                throw;
            }

            throw translated;
        }
    }
}
