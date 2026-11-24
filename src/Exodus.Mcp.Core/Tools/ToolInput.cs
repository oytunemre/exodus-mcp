using System.Globalization;
using ModelContextProtocol;

namespace Exodus.Mcp.Core.Tools;

/// <summary>
/// Validation and normalization of tool arguments. Every failure is an <see cref="McpException"/> whose Turkish
/// message reaches the model, so it can correct the call.
/// </summary>
public static class ToolInput
{
    /// <summary>
    /// Matches <paramref name="value"/> against <paramref name="allowed"/> ignoring case (ordinal, so the tr-TR
    /// "i"/"I" rules don't interfere) and returns the canonical spelling from <paramref name="allowed"/>.
    /// A missing (null or blank) value yields <paramref name="defaultValue"/>.
    /// </summary>
    public static string? OneOf(string? value, string parameterName, IReadOnlyList<string> allowed, string? defaultValue = null)
    {
        ArgumentNullException.ThrowIfNull(allowed);

        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        var trimmed = value.Trim();
        foreach (var candidate in allowed)
        {
            if (string.Equals(candidate, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        throw new McpException(
            $"Geçersiz {parameterName} değeri: '{trimmed}'. Geçerli değerler: {string.Join(", ", allowed)}.");
    }

    /// <summary>Parses a calendar date written exactly as <c>yyyy-MM-dd</c>.</summary>
    public static DateOnly Date(string value, string parameterName)
    {
        if (value is not null
            && DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        throw new McpException($"{parameterName} yyyy-MM-dd biçiminde bir tarih olmalı (gelen: '{value}').");
    }

    /// <summary>Throws an <see cref="McpException"/> with <paramref name="message"/> when <paramref name="condition"/> is false.</summary>
    public static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new McpException(message);
        }
    }
}
