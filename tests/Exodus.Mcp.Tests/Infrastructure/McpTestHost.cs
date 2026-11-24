using System.IO.Pipelines;
using Exodus.Mcp.Core;
using Exodus.Mcp.Core.Configuration;
using Exodus.Mcp.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Exodus.Mcp.Tests.Infrastructure;

/// <summary>Result of one <c>tools/call</c>: <see cref="Text"/> joins every text block of the result.</summary>
public sealed record ToolCallOutcome(bool IsError, string Text);

/// <summary>
/// The production server wiring (<c>AddExodusMcp</c> + stream transport) talking to a real <see cref="McpClient"/>
/// over two in-memory pipes. Every Exodus HTTP call goes to the given <see cref="FakeExodusHandler"/>.
/// </summary>
/// <remarks>
/// There is no Host: <c>WithStreamServerTransport</c> registers the server as an <see cref="IHostedService"/>
/// (the SDK's single-session service, which runs <c>McpServer.RunAsync</c>), so this class starts and stops
/// the registered hosted services itself, the way a Host would.
/// </remarks>
public sealed class McpTestHost : IAsyncDisposable
{
    public const string BaseUrl = "http://exodus.test/";
    public const string Password = "pw";
    public const string CustomerUsername = "ahmet@test.com";
    public const string SellerUsername = "zeynep@test.com";

    // Upper bounds so a broken server fails the test instead of hanging the run; normal calls take milliseconds.
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly ServiceProvider _services;
    private readonly IHostedService[] _hostedServices;
    private readonly Pipe _clientToServer;
    private readonly Pipe _serverToClient;
    private McpClient? _client;

    private McpTestHost(ServiceProvider services, IHostedService[] hostedServices, Pipe clientToServer, Pipe serverToClient)
    {
        _services = services;
        _hostedServices = hostedServices;
        _clientToServer = clientToServer;
        _serverToClient = serverToClient;
    }

    public McpClient Client => _client ?? throw new InvalidOperationException("The MCP client is not connected.");

    public static async Task<McpTestHost> StartAsync(ExodusProfile profile, FakeExodusHandler fake, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(fake);

        var options = new ExodusOptions
        {
            BaseUrl = new Uri(BaseUrl),
            Profile = profile,
            Username = profile == ExodusProfile.Seller ? SellerUsername : CustomerUsername,
            Password = Password,
        };

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var services = new ServiceCollection();
        services.AddLogging();
        // Registered before AddExodusMcp: its TryAdd keeps this clock.
        services.AddSingleton(time ?? TimeProvider.System);
        services.AddExodusMcp(options)
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => fake));

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var host = new McpTestHost(provider, provider.GetServices<IHostedService>().ToArray(), clientToServer, serverToClient);
        try
        {
            using var startup = new CancellationTokenSource(CallTimeout);
            foreach (var hosted in host._hostedServices)
            {
                await hosted.StartAsync(startup.Token);
            }

            host._client = await McpClient.CreateAsync(
                new StreamClientTransport(serverInput: clientToServer.Writer.AsStream(), serverOutput: serverToClient.Reader.AsStream()),
                new McpClientOptions { DiscoverProbeTimeout = CallTimeout },
                cancellationToken: startup.Token);
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    public async Task<ToolCallOutcome> CallAsync(string tool, Dictionary<string, object?>? args = null)
    {
        using var timeout = new CancellationTokenSource(CallTimeout);
        var result = await Client.CallToolAsync(tool, args, cancellationToken: timeout.Token);
        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        return new ToolCallOutcome(result.IsError == true, text);
    }

    public async ValueTask DisposeAsync()
    {
        // Disposing the client closes its pipe ends; the server then sees end of input and its run loop ends.
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }

        using (var stop = new CancellationTokenSource(StopTimeout))
        {
            foreach (var hosted in _hostedServices.Reverse())
            {
                await hosted.StopAsync(stop.Token);
            }
        }

        await _services.DisposeAsync();

        // Completing an already completed pipe end is a no-op.
        await _clientToServer.Writer.CompleteAsync();
        await _clientToServer.Reader.CompleteAsync();
        await _serverToClient.Writer.CompleteAsync();
        await _serverToClient.Reader.CompleteAsync();
    }
}
