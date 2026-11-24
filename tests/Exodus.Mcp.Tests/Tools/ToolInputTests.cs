using System.Globalization;
using Exodus.Mcp.Core.Tools;
using FluentAssertions;
using ModelContextProtocol;

namespace Exodus.Mcp.Tests.Tools;

public class ToolInputTests
{
    private static readonly string[] Conditions = ["New", "LikeNew", "Used", "Refurbished"];

    [Fact]
    public void ToolInput_OneOf_NormalizesCase()
    {
        ToolInput.OneOf("likenew", "condition", Conditions).Should().Be("LikeNew");
    }

    [Fact]
    public void ToolInput_OneOf_IsCultureInvariant()
    {
        // tr-TR lower-cases "I" to "ı": a culture-sensitive comparison would reject "REFURBISHED" or "LIKENEW".
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        try
        {
            ToolInput.OneOf("REFURBISHED", "condition", Conditions).Should().Be("Refurbished");
            ToolInput.OneOf("LIKENEW", "condition", Conditions).Should().Be("LikeNew");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ToolInput_OneOf_NullOrBlank_ReturnsDefault()
    {
        ToolInput.OneOf(null, "sortBy", ["price", "newest", "name"], "price").Should().Be("price");
        ToolInput.OneOf("  ", "sortBy", ["price", "newest", "name"], "price").Should().Be("price");
        ToolInput.OneOf(null, "condition", Conditions).Should().BeNull();
    }

    [Fact]
    public void ToolInput_OneOf_InvalidListsAllowedValues()
    {
        var act = () => ToolInput.OneOf("mint", "condition", Conditions);

        var error = act.Should().Throw<McpException>().Which;
        error.Should().NotBeOfType<McpProtocolException>();
        error.Message.Should().Contain("condition").And.Contain("New, LikeNew, Used, Refurbished");
    }

    [Fact]
    public void ToolInput_Date_ParsesIsoDate()
    {
        ToolInput.Date("2026-11-18", "from").Should().Be(new DateOnly(2026, 11, 18));
    }

    [Theory]
    [InlineData("18.11.2026")]
    [InlineData("2026-11-18T00:00:00")]
    [InlineData("2026/11/18")]
    [InlineData("2026-13-01")]
    [InlineData("")]
    public void ToolInput_Date_RejectsOtherFormats(string value)
    {
        var act = () => ToolInput.Date(value, "from");

        act.Should().Throw<McpException>().Which.Message.Should().Contain("from").And.Contain("yyyy-MM-dd");
    }

    [Fact]
    public void ToolInput_Require_ThrowsMcpExceptionWithMessage()
    {
        var act = () => ToolInput.Require(false, "orderId ya da orderNumber verilmeli.");

        act.Should().Throw<McpException>().WithMessage("orderId ya da orderNumber verilmeli.");
        ToolInput.Require(true, "unused");
    }
}
