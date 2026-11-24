using Exodus.Mcp.Core.Api;
using Exodus.Mcp.Core.Auth;
using Exodus.Mcp.Core.Configuration;
using ModelContextProtocol;

namespace Exodus.Mcp.Core.Tools;

/// <summary>
/// Runs a tool body and turns every failure into an <see cref="McpException"/> with a Turkish message: the SDK
/// sends only an <see cref="McpException"/>'s message to the client and hides all others.
/// </summary>
public static class ToolErrors
{
    private const string NoDetail = "(ayrıntı yok)";

    /// <summary>
    /// Runs <paramref name="action"/> and returns its result as <see cref="ToolJson"/>. Failures are rethrown
    /// through <see cref="Translate"/>; <paramref name="notFound"/>/<paramref name="forbidden"/> replace the
    /// default 404/403 messages.
    /// </summary>
    public static async Task<string> RunAsync<T>(ExodusOptions options, Func<Task<T>> action, string? notFound = null, string? forbidden = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            return ToolJson.Serialize(await action());
        }
        catch (Exception error)
        {
            var translated = Translate(error, options, notFound, forbidden);
            if (ReferenceEquals(translated, error))
            {
                throw; // McpException or caller cancellation: keep the original stack trace.
            }

            throw translated;
        }
    }

    /// <summary>
    /// Maps <paramref name="error"/> to the exception a tool should throw. An <see cref="McpException"/> and an
    /// <see cref="OperationCanceledException"/> caused by the caller's cancellation are returned as is (same
    /// instance); everything else becomes an <see cref="McpException"/> whose message never contains the original
    /// exception's message, except for the Exodus detail of 4xx responses and the (secret-free) auth messages.
    /// </summary>
    /// <remarks>
    /// No caller token is passed in, so cancellation is read from the exception: an HttpClient timeout is a
    /// <see cref="TaskCanceledException"/> wrapping a <see cref="TimeoutException"/>; a caller cancellation carries
    /// the caller's cancelled token and no <see cref="TimeoutException"/>.
    /// </remarks>
    public static Exception Translate(Exception error, ExodusOptions options, string? notFound = null, string? forbidden = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(options);

        return error switch
        {
            McpException => error,
            OperationCanceledException canceled when IsCallerCancellation(canceled) => error,
            TaskCanceledException => Fail(
                $"Exodus {(int)ExodusHttpServiceCollectionExtensions.RequestTimeout.TotalSeconds} saniye içinde yanıt vermedi (EXODUS_BASE_URL={options.BaseUrl}).",
                error),
            HttpRequestException => Fail($"Exodus'a ulaşılamıyor (EXODUS_BASE_URL={options.BaseUrl}). API çalışıyor mu?", error),
            ExodusAuthException auth => Fail(auth.Message, error),
            ExodusApiException api => Fail(FromStatus(api, notFound, forbidden), error),
            _ => Fail($"Beklenmeyen hata: {error.GetType().Name}", error),
        };
    }

    private static string FromStatus(ExodusApiException error, string? notFound, string? forbidden)
    {
        var detail = string.IsNullOrWhiteSpace(error.Detail) ? NoDetail : error.Detail;
        return error.StatusCode switch
        {
            400 => $"Exodus isteği reddetti: {detail}",
            401 => "Exodus oturumu doğrulanamadı. MCP sunucusunu yeniden başlatmayı deneyin.",
            403 => forbidden ?? $"Bu işlem için yetkiniz yok: {detail}",
            404 => notFound ?? $"Kayıt bulunamadı: {detail}",
            409 => $"İşlem çakıştı: {detail}",
            429 => "Exodus rate limit'e takıldı, biraz bekleyip tekrar deneyin.",
            // Any other 4xx (e.g. 422) is still Exodus rejecting the request, with a detail worth showing.
            >= 400 and < 500 => $"Exodus isteği reddetti: {detail}",
            _ => $"Exodus beklenmeyen bir hata döndürdü (HTTP {error.StatusCode}).",
        };
    }

    private static bool IsCallerCancellation(OperationCanceledException error)
    {
        if (!error.CancellationToken.IsCancellationRequested)
        {
            return false;
        }

        for (Exception? inner = error.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is TimeoutException)
            {
                return false;
            }
        }

        return true;
    }

    // The original stays as InnerException for server-side logs; the SDK sends only Message to the client.
    private static McpException Fail(string message, Exception inner) => new(message, inner);
}
