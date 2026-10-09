using System.IO.Pipelines;
using System.Text.Json.Nodes;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Beutl.HeadlessUITests.Demos;

// The production MCP tools and live editor binding, connected over in-process streams.
// No installed Beutl, listening port, desktop session, or personal MCP configuration is used.
internal sealed class DemoMcpClient(IHost host, McpClient client, Stream[] streams) : IAsyncDisposable
{
    public static async Task<DemoMcpClient> StartAsync(string workspace)
    {
        var requests = new Pipe();
        var responses = new Pipe();
        Stream[] streams =
        [
            requests.Writer.AsStream(), requests.Reader.AsStream(),
            responses.Writer.AsStream(), responses.Reader.AsStream()
        ];
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services
            .AddSingleton(TestShell.Project)
            .AddSingleton(TestShell.Editor)
            .AddSingleton<LiveSessionSource>()
            .AddSingleton<AgentSessionManager>()
            .AddSingleton<IWorkspaceGuard>(new WorkspaceGuard(workspace))
            .AddSingleton<DestructiveGuard>()
            .AddSingleton<RenderJobManager>()
            .AddSingleton<IProjectSessionGateway, EditorProjectSessionGateway>();
        builder.Services.AddMcpServer()
            .WithStreamServerTransport(streams[1], streams[2])
            .WithRequestFilters(filters => filters.AddToolkitCallToolErrorFilter())
            .WithTools<AgentHostTools>()
            .WithTools<SessionTools>()
            .WithTools<QueryTools>()
            .WithTools<EditTools>()
            .WithTools<HistoryTools>();
        IHost host = builder.Build();
        try
        {
            await host.StartAsync();
            McpClient client = await McpClient.CreateAsync(new StreamClientTransport(streams[0], streams[3]));
            return new DemoMcpClient(host, client, streams);
        }
        catch
        {
            await host.StopAsync();
            host.Dispose();
            foreach (Stream stream in streams) stream.Dispose();
            throw;
        }
    }

    public async Task<JsonNode> CallAsync(string tool, Dictionary<string, object?>? arguments = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        CallToolResult result = await client.CallToolAsync(tool, arguments, cancellationToken: timeout.Token);
        string text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(x => x.Text));
        JsonNode? response = JsonNode.Parse(text);
        if (result.IsError == true || response?["isSuccess"]?.GetValue<bool>() != true)
            throw new InvalidOperationException($"MCP {tool} failed: {text}");
        return response["value"]!;
    }

    public Task<JsonNode> EditAsync(JsonObject patch) => CallAsync("apply_edit", new()
    {
        ["schemaVersion"] = "1",
        ["patch"] = patch,
        ["quiet"] = true
    });

    public async ValueTask DisposeAsync()
    {
        try
        {
            await client.DisposeAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await host.StopAsync(timeout.Token);
        }
        finally
        {
            host.Dispose();
            foreach (Stream stream in streams) stream.Dispose();
        }
    }
}
