using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PolyView.Mcp;

/// <summary>Data-driven AI client registry (mcp-clients.json): detect, connect, disconnect, verify. Never removes other servers; no backup files.</summary>
public sealed class McpClient
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string[] Paths { get; set; } = [];
    public string[] InstallDirs { get; set; } = [];
    public string Format { get; set; } = "json";
    public string Key { get; set; } = "mcpServers";
    public string[] Processes { get; set; } = [];
    public JsonObject Entry { get; set; } = new();

    public bool Installed { get; set; }
    public bool ProcessRunning { get; set; }
    public bool Connected { get; set; }
    public string? ConfigPath { get; set; }
    public bool CanAuto => Format is "json" or "toml";
}

public static class McpClients
{
    public const string ServerName = "polyview";

    public static List<McpClient> Load()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "mcp-clients.json");
        var root = JsonNode.Parse(File.ReadAllText(file))!;
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return root["clients"]!.Deserialize<List<McpClient>>(opts) ?? [];
    }

    static string Expand(string p) => Environment.ExpandEnvironmentVariables(p);

    public static JsonObject EntryFor(McpClient c)
    {
        var s = c.Entry.ToJsonString().Replace("{url}", McpServerHost.Url);
        return JsonNode.Parse(s)!.AsObject();
    }

    public static List<McpClient> Scan()
    {
        var list = Load();
        HashSet<string> procs = new(StringComparer.OrdinalIgnoreCase);
        var nodeCmd = new StringBuilder();
        foreach (var p in Process.GetProcesses())
        {
            try { procs.Add(p.ProcessName); } catch { }
        }

        foreach (var c in list)
        {
            var existing = c.Paths.Select(Expand).FirstOrDefault(File.Exists);
            var dirOk = c.InstallDirs.Select(Expand).Any(Directory.Exists);
            c.ConfigPath = existing ?? c.Paths.Select(Expand).FirstOrDefault(p => Directory.Exists(Path.GetDirectoryName(p)!)) ?? (dirOk ? c.Paths.Select(Expand).FirstOrDefault() : null);
            c.Installed = existing != null || dirOk;
            c.ProcessRunning = c.Processes.Any(procs.Contains);
            c.Connected = c.ConfigPath != null && IsConnected(c);
            if (!c.Connected && c.Id == "claude-code") c.Connected = ClaudeProjectConnected();
        }

        return list;
    }

    static bool IsConnected(McpClient c)
    {
        try
        {
            if (!File.Exists(c.ConfigPath)) return false;
            var text = File.ReadAllText(c.ConfigPath!);
            if (text.Contains("127.0.0.1:47800")) return true;
            if (c.Format == "toml") return text.Contains($"[{c.Key}.{ServerName}]");
            var root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject;
            return root?[c.Key] is JsonObject servers && servers[ServerName] != null;
        }
        catch { return false; }
    }

    /// <summary>Claude Code also stores servers per project in ~/.claude.json.</summary>
    static bool ClaudeProjectConnected()
    {
        try
        {
            foreach (var p in new[] { Expand("%USERPROFILE%\\.claude.json"), Expand("%USERPROFILE%\\.claude\\settings.json") })
                if (File.Exists(p) && File.ReadAllText(p).Contains("127.0.0.1:47800")) return true;
        }
        catch { }
        return false;
    }

    /// <summary>Per-client paste instructions for manual clients.</summary>
    public static string ManualSteps(McpClient c) => c.Id switch
    {
        "deepseek-harness" => UiText.T($"DeepSeek Harness\uff1a\u5de6\u4fa7\u300c\u63d2\u4ef6\u300d\u2192 \u6dfb\u52a0 MCP \u670d\u52a1\u5668 \u2192 \u7c7b\u578b\u9009 HTTP\uff0c\u540d\u79f0 polyview\uff0c\u5730\u5740 {McpServerHost.Url}\uff08\u6216\u7c98\u8d34\u5df2\u590d\u5236\u7684 JSON\uff09\u3002", $"DeepSeek Harness: sidebar \u63d2\u4ef6 (Plugins) \u2192 add MCP server \u2192 HTTP, name polyview, URL {McpServerHost.Url} (or paste the copied JSON)."),
        "cherry" or "deepseek" => UiText.T($"Cherry Studio\uff1a\u8bbe\u7f6e \u2192 MCP \u670d\u52a1\u5668 \u2192 \u6dfb\u52a0 \u2192 \u4ece JSON \u5bfc\u5165\u5e76\u7c98\u8d34\uff1b\u6216\u7c7b\u578b\u9009 Streamable HTTP\uff0c\u5730\u5740 {McpServerHost.Url}\u3002", $"Cherry Studio: Settings \u2192 MCP Servers \u2192 Add \u2192 import from JSON and paste, or Streamable HTTP with URL {McpServerHost.Url}."),
        "chatbox" => UiText.T($"Chatbox\uff1a\u8bbe\u7f6e \u2192 MCP \u2192 \u6dfb\u52a0\u670d\u52a1\u5668\uff0c\u9009 HTTP\uff0c\u5730\u5740 {McpServerHost.Url}\u3002", $"Chatbox: Settings \u2192 MCP \u2192 add server, HTTP, URL {McpServerHost.Url}."),
        _ => UiText.T($"{c.Name}\uff1a\u5728\u5b83\u7684 MCP \u8bbe\u7f6e\u91cc\u6dfb\u52a0\u670d\u52a1\u5668\uff0c\u7c98\u8d34\u5df2\u590d\u5236\u7684\u914d\u7f6e\uff0c\u6216\u586b\u5730\u5740 {McpServerHost.Url}\u3002", $"{c.Name}: add a server in its MCP settings and paste the copied config, or use URL {McpServerHost.Url}."),
    };

    /// <summary>Add or update the polyview entry. Returns null on success, else an error.</summary>
    public static string? Connect(McpClient c)
    {
        if (!c.CanAuto || c.ConfigPath == null) return "manual";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(c.ConfigPath)!);
            if (c.Format == "toml")
            {
                var text = File.Exists(c.ConfigPath) ? File.ReadAllText(c.ConfigPath) : "";
                text = RemoveTomlTable(text, $"[{c.Key}.{ServerName}]");
                var e = EntryFor(c);
                var args = string.Join(", ", e["args"]!.AsArray().Select(a => JsonSerializer.Serialize(a!.GetValue<string>())));
                text = text.TrimEnd() + $"\n\n[{c.Key}.{ServerName}]\ncommand = \"{e["command"]}\"\nargs = [{args}]\n";
                File.WriteAllText(c.ConfigPath, text);
                return null;
            }

            JsonObject root = new();
            if (File.Exists(c.ConfigPath))
            {
                var text = File.ReadAllText(c.ConfigPath);
                if (text.Trim().Length > 0)
                {
                    root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                        ?? throw new InvalidDataException("config root is not an object");
                }
            }

            if (root[c.Key] is not JsonObject servers)
            {
                servers = new JsonObject();
                root[c.Key] = servers;
            }

            servers[ServerName] = EntryFor(c);
            WriteAtomic(c.ConfigPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public static string? Disconnect(McpClient c)
    {
        if (c.ConfigPath == null || !File.Exists(c.ConfigPath)) return null;
        try
        {
            var text = File.ReadAllText(c.ConfigPath);
            if (c.Format == "toml")
            {
                File.WriteAllText(c.ConfigPath, RemoveTomlTable(text, $"[{c.Key}.{ServerName}]"));
                return null;
            }

            var root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject;
            if (root?[c.Key] is JsonObject servers && servers.Remove(ServerName))
            {
                WriteAtomic(c.ConfigPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            }

            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public static bool AutoEnabled(McpClient c) => !AppSettings.Current.McpAutoConnectOff.Contains(c.Id);

    /// <summary>On every launch (background): write/refresh the polyview entry into every installed auto-capable client. Idempotent, silent, merges existing config, skips on error.</summary>
    public static void AutoConnectAll()
    {
        List<McpClient> list;
        try { list = Scan(); }
        catch (Exception ex) { App.LogException("McpClients.Auto", ex); return; }
        foreach (var c in list)
        {
            try
            {
                if (!c.Installed || !c.CanAuto || c.ConfigPath == null || !AutoEnabled(c) || UpToDate(c)) continue;
                var err = Connect(c);
                if (err != null) App.LogException("McpClients.Auto " + c.Id, new IOException(err));
            }
            catch (Exception ex) { App.LogException("McpClients.Auto " + c.Id, ex); }
        }
    }

    /// <summary>True when the config already has our current entry (or another entry pointing at our port).</summary>
    public static bool UpToDate(McpClient c)
    {
        if (c.ConfigPath == null || !File.Exists(c.ConfigPath)) return false;
        var text = File.ReadAllText(c.ConfigPath);
        if (text.Trim().Length == 0) return false;
        if (c.Format == "toml")
        {
            if (!text.Contains($"[{c.Key}.{ServerName}]")) return text.Contains("127.0.0.1:47800");
            return text.Contains(McpServerHost.Url);
        }

        var root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
            ?? throw new InvalidDataException("config root is not an object");
        var current = root[c.Key]?[ServerName];
        if (current == null) return text.Contains("127.0.0.1:47800");
        return current.ToJsonString() == EntryFor(c).ToJsonString();
    }
    static void WriteAtomic(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    static string RemoveTomlTable(string text, string header)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        int i = lines.FindIndex(l => l.Trim() == header);
        if (i < 0) return text;
        int j = i + 1;
        while (j < lines.Count && !lines[j].TrimStart().StartsWith('[')) j++;
        lines.RemoveRange(i, j - i);
        return string.Join("\n", lines);
    }

    public static string SnippetFor(McpClient c) => JsonSerializer.Serialize(new Dictionary<string, object> { ["mcpServers"] = new Dictionary<string, object> { [ServerName] = EntryFor(c) } }, new JsonSerializerOptions { WriteIndented = true });

    public static string HttpSnippet => JsonSerializer.Serialize(new { mcpServers = new Dictionary<string, object> { [ServerName] = new { url = McpServerHost.Url } } }, new JsonSerializerOptions { WriteIndented = true });

    public static string StdioSnippet => JsonSerializer.Serialize(new { mcpServers = new Dictionary<string, object> { [ServerName] = new { command = "npx", args = new[] { "-y", "mcp-remote", McpServerHost.Url } } } }, new JsonSerializerOptions { WriteIndented = true });
}
