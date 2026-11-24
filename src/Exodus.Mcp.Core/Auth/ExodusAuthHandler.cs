using System.Net;
using System.Net.Http.Headers;

namespace Exodus.Mcp.Core.Auth;

/// <summary>
/// Adds <c>Authorization: Bearer</c> to every Exodus API request. On 401 it renews the session once and
/// resends an equivalent request; a second 401 is returned as is. There is no loop: Exodus auth
/// endpoints are rate-limited and repeated failed logins lock the account.
/// </summary>
public sealed class ExodusAuthHandler(TokenStore tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // A request message can't be sent twice; buffer the body once so the retry carries the same bytes.
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        var session = await tokens.GetSessionAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        var renewed = await tokens.RenewAfterUnauthorizedAsync(session.AccessToken, cancellationToken);

        // The clone stays referenced by the returned response (RequestMessage); its content is a plain byte array.
        var retry = Clone(request, body, renewed.AccessToken);
        return await base.SendAsync(retry, cancellationToken);
    }

    private static HttpRequestMessage Clone(HttpRequestMessage original, byte[]? body, string accessToken)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
            VersionPolicy = original.VersionPolicy,
        };

        foreach (var header in original.Headers)
        {
            if (!string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var option in original.Options)
        {
            ((IDictionary<string, object?>)clone.Options)[option.Key] = option.Value;
        }

        if (original.Content is { } content && body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            foreach (var header in content.Headers)
            {
                // ByteArrayContent computes its own Content-Length.
                if (!string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        clone.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return clone;
    }
}
