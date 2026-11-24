using System.Globalization;

namespace Exodus.Mcp.Core.Api;

public static class QueryString
{
    public static string Build(string relativePath, params (string Key, object? Value)[] parameters)
    {
        var parts = new List<string>();
        foreach (var (key, value) in parameters)
        {
            if (value is null)
            {
                continue;
            }

            var text = value switch
            {
                string s => s,
                bool b => b ? "true" : "false",
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            };
            parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(text)}");
        }

        return parts.Count == 0 ? relativePath : $"{relativePath}?{string.Join('&', parts)}";
    }
}
