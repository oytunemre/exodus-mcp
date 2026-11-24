using System.Globalization;

namespace Exodus.Mcp.Core.Auth;

public sealed record AuthSession(int UserId, string Role, string AccessToken, DateTime AccessTokenExpiresAtUtc, string? RefreshToken)
{
    // The compiler-generated ToString would print both tokens.
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture,
            $"AuthSession {{ UserId = {UserId}, Role = {Role}, AccessTokenExpiresAtUtc = {AccessTokenExpiresAtUtc:O} }}");
}
