using Exodus.Mcp.Core.Api.Dtos;

namespace Exodus.Mcp.Core.Tools;

/// <summary>Output shape of a category node; shared by the list_categories tool and the categories resource.</summary>
public sealed record CategoryView(int Id, string? Name, int ProductCount, IReadOnlyList<CategoryView> Children)
{
    public static IReadOnlyList<CategoryView> Map(IEnumerable<CategoryTreeDto>? tree)
        => (tree ?? []).Select(n => new CategoryView(n.Id, n.Name, n.ProductCount, Map(n.Children))).ToList();
}
