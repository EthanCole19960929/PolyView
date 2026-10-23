using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PolyView.Mcp;

/// <summary>Data-driven AI client registry (mcp-clients.json): detect, connect, disconnect, verify. Never removes other servers; writes a .bak once.</summary>
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
        }

        return list;
    }

    static bool IsConnected(McpClient c)
    {
        try
        {
            if (!File.Exists(c.ConfigPath)) return false;
            var text = File.ReadAllText(c.ConfigPath!);
            if (c.Format == "toml") return text.Contains($"[{c.Key}.{ServerName}]");
            var root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject;
            return root?[c.Key] is JsonObject servers && servers[ServerName] != null;
        }
        catch { return false; }
    }

    static void Backup(string path)
    {
        var bak = path + ".polyview.bak";
        if (File.Exists(path) && !File.Exists(bak)) File.Copy(path, bak);
    }

    /// <summary>Add or update the polyview entry. Returns null on success, else an error.</summary>
    public static string? Connect(McpClient c)
    {
        if (!c.CanAuto || c.ConfigPath == null) return "manual";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(c.ConfigPath)!);
            Backup(c.ConfigPath);
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
            Backup(c.ConfigPath);
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

    /// <summary>On startup: for clients already connected, silently refresh the entry if it no longer matches.</summary>
    public static void VerifyConnected()
    {
        try
        {
            foreach (var c in Scan().Where(c => c.Connected && c.Format == "json"))
            {
                var root = JsonNode.Parse(File.ReadAllText(c.ConfigPath!), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var current = root?[c.Key]?[ServerName]?.ToJsonString();
                if (current != EntryFor(c).ToJsonString()) Connect(c);
            }
        }
        catch (Exception ex) { App.LogException("McpClients.Verify", ex); }
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
