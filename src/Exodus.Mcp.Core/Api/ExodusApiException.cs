using System.Text.Json;

namespace Exodus.Mcp.Core.Api;

public sealed class ExodusApiException : Exception
{
    public ExodusApiException(int statusCode, string? detail)
        : base(detail is null
            ? $"Exodus API returned HTTP {statusCode}."
            : $"Exodus API returned HTTP {statusCode}: {detail}")
    {
        StatusCode = statusCode;
        Detail = detail;
    }

    public int StatusCode { get; }

    public string? Detail { get; }

    public static async Task<ExodusApiException> FromResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? text = null;
        if (response.Content is not null)
        {
            text = await response.Content.ReadAsStringAsync(ct);
        }

        return new ExodusApiException((int)response.StatusCode, ParseDetail(text));
    }

    private static string? ParseDetail(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (Str(root, "detail") is { } detail)
            {
                return detail;
            }

            if (Str(root, "message") is { } message)
            {
                return message;
            }

            if (TryGet(root, "errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                var items = new List<string>();
                foreach (var prop in errors.EnumerateObject())
                {
                    var messages = prop.Value.ValueKind == JsonValueKind.Array
                        ? prop.Value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)
                        : prop.Value.ValueKind == JsonValueKind.String ? [prop.Value.GetString()!] : [];
                    var joined = string.Join(", ", messages);
                    if (joined.Length > 0)
                    {
                        items.Add($"{prop.Name}: {joined}");
                    }
                }

                if (items.Count > 0)
                {
                    return string.Join("; ", items);
                }
            }

            return Str(root, "title");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? Str(JsonElement obj, string name)
        => TryGet(obj, name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()
            : null;
}
