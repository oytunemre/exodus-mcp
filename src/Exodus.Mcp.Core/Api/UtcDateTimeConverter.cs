using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Exodus.Mcp.Core.Api;

/// <summary>Reads Exodus timestamps as UTC (Exodus omits the zone) and writes ISO-8601 round-trip UTC.</summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => Normalize(reader.GetDateTime());

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(Normalize(value).ToString("O", CultureInfo.InvariantCulture));

    /// <summary>Utc stays, Local (a value with an offset) is converted, Unspecified (no zone) is taken as UTC.</summary>
    public static DateTime Normalize(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
