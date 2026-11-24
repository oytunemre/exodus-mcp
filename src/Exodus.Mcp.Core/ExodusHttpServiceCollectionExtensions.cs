using Exodus.Mcp.Core.Api;
using Exodus.Mcp.Core.Auth;
using Exodus.Mcp.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Exodus.Mcp.Core;

public static class ExodusHttpServiceCollectionExtensions
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Registers the Exodus HTTP stack: options, <see cref="TokenStore"/>, the <c>exodus-auth</c> client for
    /// login/refresh and the typed <see cref="ExodusApiClient"/> behind <see cref="ExodusAuthHandler"/>.
    /// No primary handler is fixed here, so tests can swap it via <c>ConfigureHttpClientDefaults</c>.
    /// A <see cref="TimeProvider"/> registered before this call is kept.
    /// </summary>
    public static IServiceCollection AddExodusApi(this IServiceCollection services, ExodusOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<TokenStore>();
        services.AddTransient<ExodusAuthHandler>();

        // Login/refresh client: no ExodusAuthHandler, which would call back into TokenStore.
        // Header values are always redacted from HttpClient logs (Authorization carries the token).
        services.AddHttpClient(TokenStore.AuthClientName, http => Configure(http, options))
            .RedactLoggedHeaders(static _ => true);

        services.AddHttpClient<ExodusApiClient>(http => Configure(http, options))
            .AddHttpMessageHandler<ExodusAuthHandler>()
            .RedactLoggedHeaders(static _ => true);

        return services;
    }

    private static void Configure(HttpClient http, ExodusOptions options)
    {
        http.BaseAddress = options.BaseUrl;
        http.Timeout = RequestTimeout;
    }
}
