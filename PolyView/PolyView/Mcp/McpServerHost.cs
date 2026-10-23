using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PolyView.Mcp;

/// <summary>Built-in MCP server (Streamable HTTP) on http://127.0.0.1:47800/mcp. Localhost only. See docs/11.</summary>
public static class McpServerHost
{
    public const int Port = 47800;
    public static string Url => $"http://127.0.0.1:{Port}/mcp";
    public static bool Running { get; private set; }
    public static string? Error { get; private set; }
    static WebApplication? _app;

    public static async Task StartAsync()
    {
        if (_app != null) return;
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{Port}");
            builder.Logging.ClearProviders();
            builder.Services.AddMcpServer(o => o.ServerInfo = new() { Name = "polyview", Version = "0.1.0" })
                .WithHttpTransport(o => o.Stateless = true)
                .WithTools<PolyViewTools>();
            var app = builder.Build();
            // Block browser pages from other origins (DNS rebinding); MCP clients send no Origin.
            app.Use(async (ctx, next) =>
            {
                var origin = ctx.Request.Headers.Origin.ToString();
                if (origin.Length > 0 && !origin.StartsWith("http://127.0.0.1") && !origin.StartsWith("http://localhost"))
                {
                    ctx.Response.StatusCode = 403;
                    return;
                }
                await next();
            });
            app.MapMcp("/mcp");
            await app.StartAsync();
            _app = app;
            Running = true;
            Error = null;
        }
        catch (Exception ex)
        {
            Running = false;
            Error = ex is System.IO.IOException or SocketException || ex.InnerException is SocketException
                ? $"port {Port} in use (another PolyView?)" : ex.Message;
            App.LogException("McpServerHost.Start", ex);
        }
    }

    public static async Task StopAsync()
    {
        var app = _app;
        _app = null;
        Running = false;
        if (app != null)
        {
            try { await app.StopAsync(new CancellationTokenSource(1000).Token); } catch { }
        }
    }
}
