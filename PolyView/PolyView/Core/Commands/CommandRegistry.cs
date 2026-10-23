using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

namespace PolyView.Commands;

/// <summary>
/// Marks a static method as a PolyView command (Blender-operator style, e.g. "object.add_primitive").
/// Signature: static object Name(MainWindow w, ...params). Params use [Description]; optional params get defaults.
/// Menus, shortcuts and MCP (run_command) all call commands through <see cref="CommandRegistry"/>. Rule: every new feature registers a command.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PvCommandAttribute(string id, string category, string zh, string en) : Attribute
{
    public string Id { get; } = id;
    public string Category { get; } = category;
    public string Zh { get; } = zh;
    public string En { get; } = en;
    public bool Undoable { get; init; }
    public bool Destructive { get; init; }
}

public sealed class CommandInfo
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Zh { get; init; }
    public required string En { get; init; }
    public bool Undoable { get; init; }
    public bool Destructive { get; init; }
    public required MethodInfo Method { get; init; }

    public JsonObject Summary() => new()
    {
        ["id"] = Id, ["category"] = Category, ["description"] = En, ["description_zh"] = Zh,
        ["undoable"] = Undoable, ["destructive"] = Destructive,
    };

    public JsonObject Describe()
    {
        var o = Summary();
        o["params"] = CommandRegistry.Schema(Method);
        return o;
    }
}

public static class CommandRegistry
{
    static readonly Dictionary<string, CommandInfo> _commands = Discover();
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static IEnumerable<CommandInfo> All => _commands.Values.OrderBy(c => c.Id);

    public static CommandInfo? Get(string id) => _commands.GetValueOrDefault(id);

    static Dictionary<string, CommandInfo> Discover()
    {
        var map = new Dictionary<string, CommandInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in typeof(CommandRegistry).Assembly.GetTypes())
        {
            foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.GetCustomAttribute<PvCommandAttribute>() is not { } a) continue;
                map[a.Id] = new CommandInfo
                {
                    Id = a.Id, Category = a.Category, Zh = a.Zh, En = a.En,
                    Undoable = a.Undoable, Destructive = a.Destructive, Method = m,
                };
            }
        }

        return map;
    }

    static string JsonType(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t == typeof(string)) return "string";
        if (t == typeof(bool)) return "boolean";
        if (t == typeof(int) || t == typeof(long)) return "integer";
        if (t == typeof(float) || t == typeof(double)) return "number";
        if (t.IsArray) return "array";
        return "object";
    }

    /// <summary>JSON Schema (object) for the command's parameters (first parameter = MainWindow is skipped).</summary>
    public static JsonObject Schema(MethodInfo m)
    {
        var props = new JsonObject();
        var required = new JsonArray();
        foreach (var p in m.GetParameters().Skip(1))
        {
            var s = new JsonObject { ["type"] = JsonType(p.ParameterType) };
            if (p.ParameterType.IsArray) s["items"] = new JsonObject { ["type"] = JsonType(p.ParameterType.GetElementType()!) };
            if (p.GetCustomAttribute<DescriptionAttribute>() is { } d) s["description"] = d.Description;
            if (p.HasDefaultValue && p.DefaultValue != null) s["default"] = JsonSerializer.SerializeToNode(p.DefaultValue, p.ParameterType);
            props[p.Name!] = s;
            if (!p.HasDefaultValue) required.Add(p.Name);
        }

        return new JsonObject { ["type"] = "object", ["properties"] = props, ["required"] = required };
    }

    /// <summary>Run a command on the UI thread. Destructive commands ask the user first. Returns a JSON string.</summary>
    public static async Task<string> RunAsync(MainWindow window, string id, JsonObject? args)
    {
        var cmd = Get(id);
        if (cmd == null) return Err($"unknown command '{id}'; call list_commands");
        object?[] values;
        try
        {
            var ps = cmd.Method.GetParameters();
            values = new object?[ps.Length];
            values[0] = window;
            for (int i = 1; i < ps.Length; i++)
            {
                var p = ps[i];
                var node = args?.FirstOrDefault(kv => string.Equals(kv.Key, p.Name, StringComparison.OrdinalIgnoreCase)).Value;
                if (node == null)
                {
                    if (!p.HasDefaultValue) return Err($"missing parameter '{p.Name}'");
                    values[i] = p.DefaultValue;
                }
                else
                {
                    values[i] = node.Deserialize(p.ParameterType, Json);
                }
            }
        }
        catch (Exception ex)
        {
            return Err("bad arguments: " + ex.Message);
        }

        try
        {
            var result = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (cmd.Destructive)
                {
                    var answer = PvMessageDialog.Show(window,
                        UiText.T($"AI 请求执行「{cmd.Zh}」（{cmd.Id}），是否允许？\n{args?.ToJsonString()}",
                            $"AI wants to run \"{cmd.En}\" ({cmd.Id}). Allow?\n{args?.ToJsonString()}"),
                        "PolyView MCP", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (answer != MessageBoxResult.Yes) return new { ok = false, cancelled = true, error = "user declined" };
                }

                return cmd.Method.Invoke(null, values);
            });
            return JsonSerializer.Serialize(result, Json);
        }
        catch (Exception ex)
        {
            return Err((ex is TargetInvocationException { InnerException: { } inner } ? inner : ex).Message);
        }
    }

    static string Err(string message) => JsonSerializer.Serialize(new { ok = false, error = message });
}
