using System.Collections.Concurrent;
using System.Diagnostics;
using Exodus.Mcp.Tests.Infrastructure;
using FluentAssertions;

namespace Exodus.Mcp.Tests.Stdio;

/// <summary>The real Exodus.Mcp.Stdio process, started the way Claude starts it: <c>dotnet Exodus.Mcp.Stdio.dll</c>.</summary>
public class StdioHostTests
{
    // Nothing listens on the discard port: the connection is refused at once, no Exodus needed.
    private const string UnreachableBaseUrl = "http://127.0.0.1:9";
    private const string Username = "ahmet@test.com";
    private const string Password = "stdio-test-secret-7f3a";

    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task StdioHost_StartsWithoutExodus_AndReportsUnreachable()
    {
        await using var server = await StdioServer.StartAsync(StdioServer.Environment(UnreachableBaseUrl, "customer", Username, Password));

        using (var timeout = new CancellationTokenSource(StdioServer.CallTimeout))
        {
            var tools = await server.Client.ListToolsAsync(cancellationToken: timeout.Token);
            tools.Should().HaveCount(13, server.StandardError);
        }

        var outcome = await server.CallAsync("search_listings", new() { ["maxPrice"] = 1500 });

        outcome.IsError.Should().BeTrue(outcome.Text);
        outcome.Text.Should().Contain("Exodus'a ulaşılamıyor").And.Contain("EXODUS_BASE_URL=http://127.0.0.1:9/");
        outcome.Text.Should().NotContain(Password);
        server.StandardError.Should().NotContain(Password);
    }

    [Fact]
    public async Task StdioHost_InvalidProfile_ExitsWithCode2AndMessage()
    {
        using var process = Start(StdioServer.Environment(UnreachableBaseUrl, "admin", Username, Password));
        try
        {
            // A host that started anyway would end its stdio session on EOF and exit 0, not hang.
            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            await WaitForExitAsync(process);

            var error = await stderr;
            process.ExitCode.Should().Be(2, error);
            (await stdout).Should().BeEmpty();
            error.Should().Contain("EXODUS_PROFILE").And.Contain("admin");
            error.Should().NotContain(Password);
        }
        finally
        {
            KillIfRunning(process);
        }
    }

    [Fact]
    public async Task StdioHost_LogsToStderr_AndKeepsStdoutForTheProtocol()
    {
        var stdout = new ConcurrentQueue<string>();
        var stderr = new ConcurrentQueue<string>();
        // Set by the host's "Application started" lifetime log line (the last startup line) or by any stdout output.
        var startedOrStdout = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var process = Start(StdioServer.Environment(UnreachableBaseUrl, "customer", Username, Password));
        try
        {
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    stdout.Enqueue(e.Data);
                    startedOrStdout.TrySetResult();
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    stderr.Enqueue(e.Data);
                    if (e.Data.Contains("Application started", StringComparison.Ordinal))
                    {
                        startedOrStdout.TrySetResult();
                    }
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await startedOrStdout.Task.WaitAsync(ProcessTimeout);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"The host did not start within {ProcessTimeout.TotalSeconds:0} s. stderr:\n{string.Join("\n", stderr)}");
            }

            // EOF on stdin ends the single stdio session; the host then stops by itself.
            process.StandardInput.Close();
            await WaitForExitAsync(process);
            process.WaitForExit(); // returns at once; also waits for the async readers to reach EOF

            var log = string.Join("\n", stderr);
            string.Join("\n", stdout).Should().BeEmpty("stdout carries only MCP messages and no request was sent");
            log.Should().Contain("Application started");
            process.ExitCode.Should().Be(0, log);
            log.Should().NotContain(Password);
        }
        finally
        {
            KillIfRunning(process);
        }
    }

    private static Process Start(IDictionary<string, string?> environment)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add(StdioServer.DllPath);
        foreach (var (name, value) in environment)
        {
            info.Environment[name] = value;
        }

        return Process.Start(info) ?? throw new InvalidOperationException("dotnet could not be started.");
    }

    private static async Task WaitForExitAsync(Process process)
    {
        using var timeout = new CancellationTokenSource(ProcessTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            KillIfRunning(process);
            throw new TimeoutException($"Exodus.Mcp.Stdio did not exit within {ProcessTimeout.TotalSeconds:0} s; it was killed.");
        }
    }

    private static void KillIfRunning(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }
}
