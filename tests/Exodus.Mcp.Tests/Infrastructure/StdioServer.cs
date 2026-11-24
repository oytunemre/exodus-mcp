using System.Collections.Concurrent;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Exodus.Mcp.Tests.Infrastructure;

/// <summary>
/// The real stdio host (<c>dotnet Exodus.Mcp.Stdio.dll</c>) running as a child process, with an <see cref="McpClient"/>
/// connected to its stdin/stdout. The test project's reference to Exodus.Mcp.Stdio copies the dll, its
/// runtimeconfig.json and deps.json next to the test assembly.
/// </summary>
public sealed class StdioServer : IAsyncDisposable
{
    // Upper bound so a broken process fails the test instead of hanging the run.
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

    private readonly McpClient _client;
    private readonly ConcurrentQueue<string> _stderr;

    private StdioServer(McpClient client, ConcurrentQueue<string> stderr)
    {
        _client = client;
        _stderr = stderr;
    }

    public static string DllPath { get; } = Path.Combine(AppContext.BaseDirectory, "Exodus.Mcp.Stdio.dll");

    public McpClient Client => _client;

    /// <summary>The stderr lines read so far (the host's logs).</summary>
    public string StandardError => string.Join("\n", _stderr);

    /// <summary>
    /// Every <c>EXODUS_*</c> variable the host reads, set explicitly so nothing leaks in from the test runner's
    /// environment. Everything else (PATH with <c>dotnet</c>, DOTNET_ROOT, HOME) is inherited.
    /// </summary>
    public static Dictionary<string, string?> Environment(string baseUrl, string profile, string username, string password) => new()
    {
        ["EXODUS_BASE_URL"] = baseUrl,
        ["EXODUS_PROFILE"] = profile,
        ["EXODUS_USERNAME"] = username,
        ["EXODUS_PASSWORD"] = password,
    };

    public static async Task<StdioServer> StartAsync(IDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var stderr = new ConcurrentQueue<string>();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "exodus-mcp-stdio",
            Command = "dotnet",
            Arguments = [DllPath],
            EnvironmentVariables = environment,
            StandardErrorLines = stderr.Enqueue,
        });

        using var timeout = new CancellationTokenSource(CallTimeout);
        try
        {
            var client = await McpClient.CreateAsync(
                transport,
                new McpClientOptions { InitializationTimeout = CallTimeout, DiscoverProbeTimeout = CallTimeout },
                cancellationToken: timeout.Token);
            return new StdioServer(client, stderr);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(
                $"Could not connect to '{DllPath}'. Server stderr:\n{string.Join("\n", stderr)}", error);
        }
    }

    public async Task<ToolCallOutcome> CallAsync(string tool, Dictionary<string, object?>? args = null)
    {
        using var timeout = new CancellationTokenSource(CallTimeout);
        var result = await _client.CallToolAsync(tool, args, cancellationToken: timeout.Token);
        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        return new ToolCallOutcome(result.IsError == true, text);
    }

    /// <summary>Disposing the client closes the child's stdin; the host then stops (or is killed after a grace period).</summary>
    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
