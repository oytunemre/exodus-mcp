using System.Net.Http.Json;
using System.Text.Json;

namespace Exodus.Mcp.Core.Api;

public sealed partial class ExodusApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new UtcDateTimeConverter() },
    };

    internal async Task<T> SendAsync<T>(HttpMethod method, string relativePath, object? jsonBody, CancellationToken ct)
    {
        using var request = BuildRequest(method, relativePath, jsonBody);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw await ExodusApiException.FromResponseAsync(response, ct);
        }

        var length = response.Content.Headers.ContentLength;
        T? result = default;
        if (length != 0)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(text))
            {
                result = JsonSerializer.Deserialize<T>(text, JsonOptions);
            }
        }

        // Reference-type nullability is erased at runtime, so only Nullable<T> may legitimately be empty.
        if (result is null && Nullable.GetUnderlyingType(typeof(T)) is null)
        {
            throw new InvalidOperationException($"Exodus API returned an empty response body for {method} {relativePath}.");
        }

        return result!;
    }

    internal async Task SendAsync(HttpMethod method, string relativePath, object? jsonBody, CancellationToken ct)
    {
        using var request = BuildRequest(method, relativePath, jsonBody);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw await ExodusApiException.FromResponseAsync(response, ct);
        }
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string relativePath, object? jsonBody)
    {
        var request = new HttpRequestMessage(method, relativePath);
        if (jsonBody is not null)
        {
            request.Content = JsonContent.Create(jsonBody, jsonBody.GetType(), options: JsonOptions);
        }
        return request;
    }
}
