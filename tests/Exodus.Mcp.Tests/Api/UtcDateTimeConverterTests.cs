using System.Text.Json;
using Exodus.Mcp.Core.Api;
using FluentAssertions;

namespace Exodus.Mcp.Tests.Api;

public class UtcDateTimeConverterTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new UtcDateTimeConverter() },
    };

    private sealed record Holder(DateTime At, DateTime? Maybe);

    [Fact]
    public void Unspecified_IsReadAsUtc_WithSameWallClock()
    {
        var value = JsonSerializer.Deserialize<DateTime>("\"2026-11-24T23:30:00\"", Options);

        value.Kind.Should().Be(DateTimeKind.Utc);
        value.Should().Be(new DateTime(2026, 11, 24, 23, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Offset_IsConvertedToUtc()
    {
        var value = JsonSerializer.Deserialize<DateTime>("\"2026-11-24T23:30:00+03:00\"", Options);

        value.Kind.Should().Be(DateTimeKind.Utc);
        value.Should().Be(new DateTime(2026, 11, 24, 20, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ZuluInput_StaysUtc()
    {
        var value = JsonSerializer.Deserialize<DateTime>("\"2026-11-24T23:30:00Z\"", Options);

        value.Kind.Should().Be(DateTimeKind.Utc);
        value.Should().Be(new DateTime(2026, 11, 24, 23, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void NullableMember_IsHandled_AndWriteIsRoundtripUtc()
    {
        var holder = JsonSerializer.Deserialize<Holder>("{\"at\":\"2026-11-24T09:00:00\",\"maybe\":null}", Options)!;
        holder.Maybe.Should().BeNull();

        var json = JsonSerializer.Serialize(new Holder(holder.At, new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Unspecified)), Options);
        json.Should().Contain("\"at\":\"2026-11-24T09:00:00.0000000Z\"").And.Contain("\"maybe\":\"2026-11-24T12:00:00.0000000Z\"");
    }
}
