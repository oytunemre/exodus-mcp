using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Exodus.Mcp.Core.Tools;

/// <summary>The JSON every tool returns: compact, camelCase, no nulls, Turkish characters kept as is.</summary>
public static class ToolJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // The output is a tool result read by the model, not HTML: "ı", "ş", "'" stay readable.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        };
        // Enums always travel as strings (global constraint), should a view ever carry one.
        options.Converters.Add(new JsonStringEnumConverter());
        // Shared static instance: freeze it so no caller can change the output format for everyone.
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
