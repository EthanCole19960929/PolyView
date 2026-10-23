using System.ComponentModel;

namespace PolyView.Commands;

/// <summary>Built-in commands wrapping existing features. Add new features here (or in their own class) with [PvCommand].</summary>
static class BuiltinCommands
{
    [PvCommand("scene.get", "scene", "获取场景信息", "List objects, lights, transforms, shading and selection")]
    static object SceneGet(MainWindow w) => w.McpGetScene();

    [PvCommand("object.add_primitive", "object", "添加基本体（Shift+A）", "Add a Blender-style primitive: plane cube circle uvsphere icosphere cylinder cone torus grid text pointcloud camera curve:* empty:*", Undoable = true)]
    static object AddPrimitive(MainWindow w,
        [Description("plane | cube | circle | uvsphere | icosphere | cylinder | cone | torus | grid | text | pointcloud | camera | curve:bezier | curve:circle | curve:nurbs | curve:nurbscircle | curve:path | empty:plain_axes | empty:arrows | empty:single_arrow | empty:circle | empty:cube | empty:sphere | empty:cone | empty:image")] string type,
        [Description("Blender params: size radius vertices segments rings subdivisions depth radius1 radius2 major_radius minor_radius major_segments minor_segments x_subdivisions")] Dictionary<string, float>? parameters = null,
        [Description("Location [x,y,z]")] float[]? location = null,
        [Description("Body text for type=text")] string? text = null,
        [Description("Object name")] string? name = null)
        => w.McpAddPrimitive(type, parameters, location, text, name);

    [PvCommand("object.add_light", "object", "添加灯光", "Add a light: point | sun | spot | area", Undoable = true)]
    static object AddLight(MainWindow w, [Description("point | sun | spot | area")] string type, [Description("Position [x,y,z]")] float[]? position = null)
        => w.McpAddLight(type, position);

    [PvCommand("object.delete", "object", "删除物体", "Delete an object", Destructive = true)]
    static object Delete(MainWindow w, [Description("Object id")] int id) => w.McpDelete(id);

    [PvCommand("object.duplicate", "object", "复制物体（Shift+D）", "Duplicate an object (selects id first if given)", Undoable = true)]
    static object Duplicate(MainWindow w, [Description("Object id")] int? id = null) => w.McpDuplicate(id);

    [PvCommand("object.transform_set", "object", "设置变换", "Set absolute position / rotation (euler degrees) / scale", Undoable = true)]
    static object TransformSet(MainWindow w, [Description("Object id")] int id,
        [Description("[x,y,z]")] float[]? position = null, [Description("Euler degrees [x,y,z]")] float[]? rotation_deg = null, [Description("[x,y,z]")] float[]? scale = null)
        => w.McpSetTransform(id, position, rotation_deg, scale);

    [PvCommand("object.visibility_set", "object", "显示/隐藏", "Show or hide an object")]
    static object Visibility(MainWindow w, [Description("Object id")] int id, [Description("true = visible")] bool visible) => w.McpSetVisibility(id, visible);

    [PvCommand("object.select", "object", "选择物体", "Select an object by id (-1 clears)")]
    static object Select(MainWindow w, [Description("Object id or -1")] int id) => w.McpSelect(id);

    [PvCommand("view.shading", "view", "着色模式", "Viewport shading: wireframe | solid | material | rendered")]
    static object Shading(MainWindow w, [Description("wireframe | solid | material | rendered")] string mode) => w.McpSetShading(mode);

    [PvCommand("view.set", "view", "视图", "View preset: front back right left top bottom persp ortho frame_all frame_selected")]
    static object View(MainWindow w, [Description("View preset")] string view) => w.McpSetView(view);

    [PvCommand("file.save", "file", "保存工程", "Save the open .pvproj in place (fails if never saved; use file.save_as)")]
    static object Save(MainWindow w) => w.McpSave(null);

    [PvCommand("file.save_as", "file", "工程另存为", "Save the project to a new .pvproj path and make it current")]
    static object SaveAs(MainWindow w, [Description("Absolute .pvproj path")] string path) => w.McpSave(path);

    [PvCommand("file.open", "file", "打开工程", "Open a .pvproj (replaces the current scene)", Destructive = true)]
    static object Open(MainWindow w, [Description("Absolute .pvproj path")] string path) => w.McpOpen(path);

    [PvCommand("file.import", "file", "导入模型", "Import a model file into the scene")]
    static object Import(MainWindow w, [Description("Absolute model path")] string path) => w.McpImport(path);

    [PvCommand("file.export", "file", "导出模型", "Export to fbx | obj | gltf | glb | stl | ply | dae (default folder C:\\Project\\C#\\PolyView\\Export)")]
    static object Export(MainWindow w, [Description("fbx | obj | gltf | glb | stl | ply | dae")] string format,
        [Description("File path or name")] string? path = null, [Description("Only the selection")] bool selection_only = false)
        => w.McpExport(format, path, selection_only);
}
