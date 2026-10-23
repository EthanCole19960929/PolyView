using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace PolyView.Mcp;

/// <summary>MCP tools. Every call is marshalled onto the WPF UI thread, where the viewport and D3D11 renderer live.</summary>
[McpServerToolType]
public sealed class PolyViewTools
{
    internal static MainWindow? Window;

    static readonly JsonSerializerOptions Json = new() { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    static async Task<string> Ui(Func<MainWindow, object> f)
    {
        var w = Window;
        if (w == null) return Err("PolyView window not ready");
        try
        {
            var r = await Application.Current.Dispatcher.InvokeAsync(() => f(w));
            return JsonSerializer.Serialize(r, Json);
        }
        catch (Exception ex)
        {
            return Err(ex.Message);
        }
    }

    internal static string Err(string message) => JsonSerializer.Serialize(new { ok = false, error = message });

    [McpServerTool(Name = "get_scene"), Description("List all scene objects (meshes and lights) with id, name, kind, visibility, transform (position meters, rotation euler degrees, scale), bounds, plus shading mode and selected id. Coordinates are PolyView internal world space (same as get_scene positions).")]
    public static Task<string> GetScene() => Ui(w => w.McpGetScene());

    [McpServerTool(Name = "select_object"), Description("Select an object by id (from get_scene). Use -1 to clear the selection.")]
    public static Task<string> SelectObject([Description("Object id, or -1")] int id) => Ui(w => w.McpSelect(id));

    [McpServerTool(Name = "set_transform"), Description("Set the absolute transform of an object. Omitted fields stay unchanged. Undoable with Ctrl+Z.")]
    public static Task<string> SetTransform(
        [Description("Object id")] int id,
        [Description("Position [x,y,z] in meters")] float[]? position = null,
        [Description("Rotation as euler angles [x,y,z] in degrees")] float[]? rotation_deg = null,
        [Description("Scale [x,y,z]")] float[]? scale = null)
        => Ui(w => w.McpSetTransform(id, position, rotation_deg, scale));

    [McpServerTool(Name = "add_light"), Description("Add a light (max 16). type: point | sun | spot | area. Returns the new object id.")]
    public static Task<string> AddLight(
        [Description("point | sun | spot | area")] string type,
        [Description("Optional position [x,y,z] in meters")] float[]? position = null)
        => Ui(w => w.McpAddLight(type, position));

    [McpServerTool(Name = "delete_object"), Description("Delete an object by id. PolyView asks the user to confirm first; returns cancelled if declined.")]
    public static Task<string> DeleteObject([Description("Object id")] int id) => Ui(w => w.McpDelete(id));

    [McpServerTool(Name = "set_shading_mode"), Description("Set viewport shading: wireframe | solid | material | rendered.")]
    public static Task<string> SetShadingMode([Description("wireframe | solid | material | rendered")] string mode) => Ui(w => w.McpSetShading(mode));

    [McpServerTool(Name = "import_model"), Description("Import a model file into the scene (adds to existing objects). Supported: .obj .stl .glb .gltf .fbx .dae .3ds .ply .json")]
    public static Task<string> ImportModel([Description("Absolute file path on this PC")] string path) => Ui(w => w.McpImport(path));

    [McpServerTool(Name = "save_project"), Description("Save the project (.pvproj). Without path: overwrite the currently open project file (fails if never saved). With path: save to that file and make it the current project.")]
    public static Task<string> SaveProject([Description("Optional absolute .pvproj path")] string? path = null) => Ui(w => w.McpSave(path));

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

        if (png == null) return [new TextContentBlock { Text = Err(error ?? "capture failed") }];
        return [ImageContentBlock.FromBytes(png, "image/png")];
    }
}
