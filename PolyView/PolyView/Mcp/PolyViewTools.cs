using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using PolyView.Commands;

namespace PolyView.Mcp;

/// <summary>
/// MCP tools: a few generic ones over the command registry (list_commands / describe_command / run_command),
/// scene + screenshot, and shortcuts for the most common commands. Everything runs on the WPF UI thread.
/// </summary>
[McpServerToolType]
public sealed class PolyViewTools
{
    internal static MainWindow? Window;

    static readonly Queue<string> _calls = new();
    static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal static List<string> RecentCalls() { lock (_calls) return _calls.Reverse().ToList(); }

    internal static void LogCall(string tool, string result)
    {
        bool ok = !result.Contains("\"ok\":false");
        var mark = ok ? "✓" : "✗";
        lock (_calls)
        {
            _calls.Enqueue($"{DateTime.Now:HH:mm:ss} {mark} {tool}  {(result.Length > 70 ? result[..70] : result)}");
            while (_calls.Count > 20) _calls.Dequeue();
        }
    }

    static async Task<string> Run(string id, JsonObject? args, string? label = null)
    {
        var w = Window;
        var r = w == null ? Err("PolyView window not ready") : await CommandRegistry.RunAsync(w, id, args);
        LogCall(label ?? id, r);
        return r;
    }

    static string Err(string message) => JsonSerializer.Serialize(new { ok = false, error = message });

    static JsonObject Args(object o) => JsonSerializer.SerializeToNode(o, Json)!.AsObject();

    [McpServerTool(Name = "list_commands"), Description("List PolyView commands (Blender-operator style ids like object.add_primitive). Optional category: scene | object | view | file. Use describe_command for parameters, run_command to execute.")]
    public static string ListCommands([Description("Optional category filter")] string? category = null)
    {
        var arr = new JsonArray(CommandRegistry.All.Where(c => category == null || c.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).Select(c => (JsonNode)c.Summary()).ToArray());
        LogCall("list_commands", "ok");
        return arr.ToJsonString(Json);
    }

    [McpServerTool(Name = "describe_command"), Description("Get a command's description and JSON Schema of its parameters.")]
    public static string DescribeCommand([Description("Command id, e.g. file.export")] string id)
        => CommandRegistry.Get(id)?.Describe().ToJsonString(Json) ?? Err($"unknown command '{id}'");

    [McpServerTool(Name = "run_command"), Description("Run a PolyView command by id with JSON arguments. Destructive commands (delete, open) ask the user to confirm in PolyView. Undoable ones can be undone with Ctrl+Z.")]
    public static Task<string> RunCommand([Description("Command id")] string id, [Description("Arguments object matching describe_command params")] JsonObject? args = null)
        => Run(id, args, "run " + id);

    [McpServerTool(Name = "get_scene"), Description("List all scene objects (meshes, lights, primitives) with id, name, kind, visibility, transform (position, rotation euler degrees, scale), bounds, plus shading mode and selected id.")]
    public static Task<string> GetScene() => Run("scene.get", null, "get_scene");

    [McpServerTool(Name = "add_primitive"), Description("Shortcut for object.add_primitive. type: plane | cube | circle | uvsphere | icosphere | cylinder | cone | torus | grid | text | pointcloud | camera | curve:bezier | curve:circle | curve:nurbs | curve:nurbscircle | curve:path | empty:plain_axes | empty:arrows | empty:single_arrow | empty:circle | empty:cube | empty:sphere | empty:cone | empty:image")]
    public static Task<string> AddPrimitive(string type,
        [Description("Optional Blender params e.g. {\"radius\":0.5}")] Dictionary<string, float>? parameters = null,
        [Description("Location [x,y,z]")] float[]? location = null, string? text = null, string? name = null)
        => Run("object.add_primitive", Args(new { type, parameters, location, text, name }), "add_primitive");

    [McpServerTool(Name = "add_light"), Description("Shortcut for object.add_light. type: point | sun | spot | area.")]
    public static Task<string> AddLight([Description("point | sun | spot | area")] string type, [Description("Position [x,y,z]")] float[]? position = null)
        => Run("object.add_light", Args(new { type, position }), "add_light");

    [McpServerTool(Name = "set_transform"), Description("Shortcut for object.transform_set: absolute position (m), rotation_deg (euler degrees), scale. Omitted fields unchanged. Undoable.")]
    public static Task<string> SetTransform(int id, float[]? position = null, float[]? rotation_deg = null, float[]? scale = null)
        => Run("object.transform_set", Args(new { id, position, rotation_deg, scale }), "set_transform");

    [McpServerTool(Name = "import_model"), Description("Import a model file into the scene (adds to existing objects). Supported: .obj .stl .glb .gltf .fbx .dae .3ds .ply .json")]
    public static Task<string> ImportModel([Description("Absolute file path on this PC")] string path) => Run("file.import", Args(new { path }), "import_model");

    [McpServerTool(Name = "get_viewport_screenshot"), Description("Capture the current 3D viewport as a PNG image so you can see the scene.")]
    public static async Task<IEnumerable<ContentBlock>> GetViewportScreenshot()
    {
        var w = Window;
        byte[]? png = null;
        string? error = null;
        if (w != null)
        {
            try { png = await Application.Current.Dispatcher.InvokeAsync(w.McpScreenshot); }
            catch (Exception ex) { error = ex.Message; }
        }

        LogCall("get_viewport_screenshot", png == null ? "{\"ok\":false}" : "png " + png.Length + " B");
        if (png == null) return [new TextContentBlock { Text = Err(error ?? "capture failed") }];
        return [ImageContentBlock.FromBytes(png, "image/png")];
    }
}
