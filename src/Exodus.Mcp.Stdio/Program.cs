using Exodus.Mcp.Core;
using Exodus.Mcp.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// stdout carries only MCP messages. Every log line, the host's lifetime messages included, goes to stderr.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);

ExodusOptions options;
try
{
    // Validation only, no network: Exodus is first contacted by the first tool call.
    options = ExodusOptions.FromConfiguration(builder.Configuration);
}
catch (ExodusConfigurationException error)
{
    // The message names the bad variables and never contains the password. Without a valid profile the tool set
    // is unknown, so the host is not started.
    await Console.Error.WriteLineAsync(error.Message);
    return 2;
}

builder.Services.AddExodusMcp(options).WithStdioServerTransport();

await builder.Build().RunAsync();
return 0;
