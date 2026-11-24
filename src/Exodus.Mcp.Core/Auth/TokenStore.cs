using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Exodus.Mcp.Core.Api;
using Exodus.Mcp.Core.Api.Dtos;
using Exodus.Mcp.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Exodus.Mcp.Core.Auth;

/// <summary>
/// The single Exodus session of this MCP process: lazy login on first use, rotating refresh shortly
/// before expiry, and a latch for permanent login failures.
/// </summary>
/// <remarks>
/// Exodus locks an account for 15 minutes after 5 failed logins and rate-limits its auth endpoints
/// (10/min/IP). So every login/refresh happens under one lock, nothing is retried in a loop, and a
/// permanent failure (bad credentials, 2FA, wrong role) is remembered: later calls get the same
/// message without another login request. Network errors and 429 are not latched.
/// </remarks>
public sealed class TokenStore(IHttpClientFactory httpClientFactory, ExodusOptions options, TimeProvider time, ILogger<TokenStore> logger)
{
    public const string AuthClientName = "exodus-auth";
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    private const string LoginPath = "api/auth/login";
    private const string RefreshPath = "api/auth/refresh";
    private const string RateLimitedMessage =
        "Exodus giriş rate limit'ine takıldı (dakikada 10 istek). Bir dakika bekleyip tekrar deneyin.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Guards _session and _latchedFailure; at most one login or refresh is in flight.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AuthSession? _session;
    private string? _latchedFailure;

    public async Task<AuthSession> GetSessionAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            ThrowIfLatched();

            if (_session is null)
            {
                return await LoginAsync(ct);
            }

            if (_session.AccessTokenExpiresAtUtc - time.GetUtcNow().UtcDateTime >= RefreshMargin)
            {
                return _session;
            }

            return await RefreshOrLoginAsync(_session, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AuthSession> RenewAfterUnauthorizedAsync(string rejectedAccessToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rejectedAccessToken);

        await _gate.WaitAsync(ct);
        try
        {
            ThrowIfLatched();

            if (_session is null)
            {
                return await LoginAsync(ct);
            }

            if (!string.Equals(_session.AccessToken, rejectedAccessToken, StringComparison.Ordinal))
            {
                // Another caller renewed the session while this one waited for the lock.
                return _session;
            }

            return await RefreshOrLoginAsync(_session, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Caller holds _gate.
    private async Task<AuthSession> RefreshOrLoginAsync(AuthSession current, CancellationToken ct)
    {
        if (current.RefreshToken is null)
        {
            return await LoginAsync(ct);
        }

        var http = httpClientFactory.CreateClient(AuthClientName);
        using var response = await http.PostAsJsonAsync(RefreshPath, new RefreshRequest { RefreshToken = current.RefreshToken }, JsonOptions, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Expired, revoked or already rotated: the refresh token is dead, so exactly one login attempt.
            logger.LogInformation("Exodus rejected the refresh token; logging in again.");
            _session = null;
            return await LoginAsync(ct);
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new ExodusAuthException(RateLimitedMessage, permanent: false);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await ExodusApiException.FromResponseAsync(response, ct);
        }

        var body = await response.Content.ReadFromJsonAsync<RefreshResponse>(JsonOptions, ct);
        if (string.IsNullOrEmpty(body?.AccessToken) || body.AccessTokenExpiresAt is not { } expiresAt)
        {
            throw new InvalidOperationException("Exodus refresh response did not contain an access token.");
        }

        // Refresh tokens rotate: the one just sent is dead, keep the new one.
        _session = current with
        {
            AccessToken = body.AccessToken,
            AccessTokenExpiresAtUtc = AsUtc(expiresAt),
            RefreshToken = body.RefreshToken,
        };
        logger.LogDebug("Exodus access token refreshed.");
        return _session;
    }

    // Caller holds _gate.
    private async Task<AuthSession> LoginAsync(CancellationToken ct)
    {
        _session = null;

        var http = httpClientFactory.CreateClient(AuthClientName);
        var request = new LoginRequest { EmailOrUsername = options.Username, Password = options.Password };
        using var response = await http.PostAsJsonAsync(LoginPath, request, JsonOptions, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var detail = (await ExodusApiException.FromResponseAsync(response, ct)).Detail ?? "(ayrıntı yok)";
            throw Latch($"Exodus girişi başarısız: {detail}. EXODUS_USERNAME/EXODUS_PASSWORD değerlerini düzeltip MCP sunucusunu yeniden başlatın.");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new ExodusAuthException(RateLimitedMessage, permanent: false);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await ExodusApiException.FromResponseAsync(response, ct);
        }

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Exodus login response was empty.");

        if (body.TwoFactorRequired)
        {
            throw Latch("Bu hesapta iki adımlı doğrulama açık. MCP için 2FA kapalı bir hesap kullanın.");
        }

        var role = body.Role ?? string.Empty;
        var expectedRole = options.Profile.ToString();
        if (!string.Equals(role, expectedRole, StringComparison.OrdinalIgnoreCase))
        {
            var shownRole = role.Length == 0 ? "(bilinmiyor)" : role;
            throw Latch($"EXODUS_PROFILE={expectedRole.ToLowerInvariant()} ama hesabın rolü {shownRole}. Doğru hesabı ya da profili kullanın.");
        }

        if (string.IsNullOrEmpty(body.Token) || body.ExpiresAt is not { } expiresAt)
        {
            throw new InvalidOperationException("Exodus login response did not contain an access token.");
        }

        _session = new AuthSession(body.UserId, role, body.Token, AsUtc(expiresAt), body.RefreshToken);
        logger.LogInformation("Logged in to Exodus as {Username} with role {Role}.", body.Username ?? options.Username, role);
        return _session;
    }

    // Caller holds _gate. Every later call fails with this message and no HTTP request.
    private ExodusAuthException Latch(string message)
    {
        _latchedFailure = message;
        logger.LogError("Exodus login failed permanently; no further login attempts until restart. {Reason}", message);
        return new ExodusAuthException(message, permanent: true);
    }

    private void ThrowIfLatched()
    {
        if (_latchedFailure is not null)
        {
            // A new instance per call: one exception object must not be thrown concurrently on several threads.
            throw new ExodusAuthException(_latchedFailure, permanent: true);
        }
    }

    // Exodus (SQL Server datetime2) may omit the 'Z': an unspecified kind means UTC.
    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
