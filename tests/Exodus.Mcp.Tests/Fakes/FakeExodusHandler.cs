using System.Net;
using System.Text;
using System.Text.Json;

namespace Exodus.Mcp.Tests.Fakes;

public sealed record RecordedRequest(string Method, string PathAndQuery, string? Authorization, string? Body);

public sealed class FakeExodusHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Canned(HttpStatusCode Status, string? Body, string ContentType, TimeSpan? Delay);

    private readonly object _gate = new();
    private readonly Dictionary<(string Method, string Path), Queue<Canned>> _responses = new();
    private readonly List<RecordedRequest> _requests = [];

    public IReadOnlyList<RecordedRequest> Requests
    {
        get { lock (_gate) { return _requests.ToArray(); } }
    }

    public int Count(HttpMethod method, string pathAndQuery)
    {
        lock (_gate)
        {
            return _requests.Count(r => r.Method == method.Method && r.PathAndQuery == pathAndQuery);
        }
    }

    public FakeExodusHandler On(HttpMethod method, string pathAndQuery, HttpStatusCode status, object? json = null, TimeSpan? delay = null)
        => Enqueue(method, pathAndQuery, new Canned(status, json is null ? null : JsonSerializer.Serialize(json, Json), "application/json", delay));

    public FakeExodusHandler OnRaw(HttpMethod method, string pathAndQuery, HttpStatusCode status, string? body, string contentType = "application/json")
        => Enqueue(method, pathAndQuery, new Canned(status, body, contentType, null));

    public FakeExodusHandler WithLogin(string role = "Customer", int userId = 7, string token = "access-1", DateTime? expiresAtUtc = null, string refreshToken = "refresh-1")
    {
        var expires = expiresAtUtc ?? DateTime.UtcNow.AddHours(24);
        return On(HttpMethod.Post, "/api/auth/login", HttpStatusCode.OK, new
        {
            userId,
            name = "Test User",
            email = "test@example.com",
            username = "testuser",
            role,
            token,
            expiresAt = expires,
            refreshToken,
            refreshTokenExpiresAt = DateTime.UtcNow.AddDays(7),
            twoFactorRequired = false,
        });
    }

    private FakeExodusHandler Enqueue(HttpMethod method, string pathAndQuery, Canned canned)
    {
        lock (_gate)
        {
            var key = (method.Method, pathAndQuery);
            if (!_responses.TryGetValue(key, out var queue))
            {
                _responses[key] = queue = new Queue<Canned>();
            }
            queue.Enqueue(canned);
        }
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var pathAndQuery = request.RequestUri!.PathAndQuery;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Canned? canned = null;

        lock (_gate)
        {
            _requests.Add(new RecordedRequest(request.Method.Method, pathAndQuery, request.Headers.Authorization?.ToString(), body));
            if (_responses.TryGetValue((request.Method.Method, pathAndQuery), out var queue) && queue.Count > 0)
            {
                // The last response repeats once the queue is down to one item.
                canned = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            }
        }

        if (canned is null)
        {
            canned = new Canned(HttpStatusCode.NotImplemented,
                JsonSerializer.Serialize(new { detail = $"No fake for {request.Method.Method} {pathAndQuery}" }, Json),
                "application/json", null);
        }

        if (canned.Delay is { } delay)
        {
            await Task.Delay(delay, cancellationToken);
        }

        var response = new HttpResponseMessage(canned.Status) { RequestMessage = request };
        if (canned.Body is not null)
        {
            response.Content = new StringContent(canned.Body, Encoding.UTF8, canned.ContentType);
        }
        return response;
    }
}
