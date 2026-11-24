namespace Exodus.Mcp.Core.Api.Dtos;

// Classes, not records: a record's generated ToString would print the password and tokens.

/// <summary>Body of <c>POST api/auth/login</c>.</summary>
internal sealed class LoginRequest
{
    public required string EmailOrUsername { get; init; }
    public required string Password { get; init; }
}

/// <summary>Response of <c>POST api/auth/login</c>: the access token is <c>token</c>/<c>expiresAt</c>.</summary>
internal sealed class LoginResponse
{
    public int UserId { get; init; }
    public string? Username { get; init; }
    public string? Role { get; init; }
    public string? Token { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string? RefreshToken { get; init; }
    public bool TwoFactorRequired { get; init; }
}

/// <summary>Body of <c>POST api/auth/refresh</c>.</summary>
internal sealed class RefreshRequest
{
    public required string RefreshToken { get; init; }
}

/// <summary>Response of <c>POST api/auth/refresh</c>: the access token is <c>accessToken</c>/<c>accessTokenExpiresAt</c>.</summary>
internal sealed class RefreshResponse
{
    public string? AccessToken { get; init; }
    public DateTime? AccessTokenExpiresAt { get; init; }
    public string? RefreshToken { get; init; }
}

// A record is safe here: none of these fields is a secret.
/// <summary>Response of <c>GET api/auth/me</c>. Exodus sends <c>userId</c> as a string.</summary>
public sealed record MeDto(string UserId, string? Username, string? Email, string? Role);
