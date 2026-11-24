using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Exodus.Mcp.Core;
using Exodus.Mcp.Core.Configuration;
using Exodus.Mcp.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Exodus.Mcp.Tests.Auth;

/// <summary>Real DI wiring (<c>AddExodusApi</c>) with every HTTP client pointed at a <see cref="FakeExodusHandler"/>.</summary>
internal static class AuthTestServices
{
    public const string LoginPath = "/api/auth/login";
    public const string RefreshPath = "/api/auth/refresh";
    public const string Username = "ahmet@test.com";
    public const string Password = "pw-secret-123";

    public static readonly DateTimeOffset Start = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A UTC instant relative to <see cref="Start"/> (the ManualTimeProvider clock, never the wall clock).</summary>
    public static DateTime At(TimeSpan offset) => Start.UtcDateTime + offset;

    /// <summary>Exodus may send timestamps without a 'Z' suffix (SQL Server datetime2).</summary>
    public static string WithoutZone(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    public static ServiceProvider Build(FakeExodusHandler fake, TimeProvider clock, ExodusProfile profile = ExodusProfile.Customer, ILoggerProvider? logs = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            if (logs is not null)
            {
                b.SetMinimumLevel(LogLevel.Trace).AddProvider(logs);
            }
        });
        // Registered before AddExodusApi, exactly like the Task 6 test host: TryAdd must keep it.
        services.AddSingleton(clock);
        services.AddExodusApi(new ExodusOptions
        {
            BaseUrl = new Uri("http://exodus.test/"),
            Profile = profile,
            Username = Username,
            Password = Password,
        });
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => fake));
        return services.BuildServiceProvider();
    }

    public static object RefreshJson(string accessToken, string refreshToken, object accessTokenExpiresAt) => new
    {
        accessToken,
        refreshToken,
        accessTokenExpiresAt,
        refreshTokenExpiresAt = WithoutZone(At(TimeSpan.FromDays(7))),
    };

    public static string? JsonField(string? body, string name)
    {
        if (body is null)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
    }
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<(string Category, LogLevel Level, string Text)> _entries = new();

    public IReadOnlyList<(string Category, LogLevel Level, string Text)> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<(string, LogLevel, string)> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            sink.Enqueue((category, LogLevel.Trace, $"scope: {state}"));
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => sink.Enqueue((category, logLevel, exception is null ? formatter(state, exception) : $"{formatter(state, exception)} {exception}"));
    }
}
