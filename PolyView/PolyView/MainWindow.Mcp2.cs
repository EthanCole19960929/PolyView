using System.IO;
using System.Numerics;
using System.Windows.Input;

namespace PolyView;

/// <summary>MCP tools added in phase 1b: primitives, duplicate, export, view, visibility.</summary>
public partial class MainWindow
{
    internal const string McpExportFolder = @"C:\Project\C#\PolyView\Export";

    internal object McpAddPrimitive(string type, Dictionary<string, float>? parameters, float[]? location, string? text, string? name)
    {
        Vector3? loc = location is { Length: 3 } ? new Vector3(location[0], location[1], location[2]) : null;
        int id = Viewport.AddPrimitive(type, parameters, loc, text, name);
        if (id < 0) return new { ok = false, error = "unknown type; valid: " + string.Join(" ", ModelViewport.PrimitiveKinds) };
        Viewport.TryGetObjectInfo(id, out var info);
        return new { ok = true, id, name = info.Name, type };
    }

    internal object McpDuplicate(int? id)
    {
        if (id is { } i)
        {
            if (!Viewport.ObjectIds.Contains(i)) return new { ok = false, error = $"no object {i}" };
            Viewport.Select(i);
        }

        int nid = Viewport.DuplicateSelected();
        return nid < 0 ? new { ok = false, error = "nothing duplicated (only added primitives can be duplicated for now)" } : new { ok = true, id = nid };
    }

    internal object McpExport(string format, string? path, bool selectionOnly)
    {
        var fmt = format.Trim().TrimStart('.').ToLowerInvariant();
        string[] ok = ["fbx", "obj", "gltf", "glb", "stl", "ply", "dae"];
        if (!ok.Contains(fmt)) return new { ok = false, error = "format must be " + string.Join(" | ", ok) };
        if (string.IsNullOrWhiteSpace(path)) path = "scene";
        if (!Path.IsPathRooted(path)) path = Path.Combine(McpExportFolder, path);
        if (!path.EndsWith("." + fmt, StringComparison.OrdinalIgnoreCase)) path += "." + fmt;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var r = ExportScene(path, fmt, selectionOnly);
        return new { ok = File.Exists(r.Path), result = r };
    }

    internal object McpSetView(string view)
    {
        switch (view.Trim().ToLowerInvariant())
        {
            case "front": Viewport.HandleKey(Key.NumPad1, ModifierKeys.None); break;
            case "back": Viewport.HandleKey(Key.NumPad1, ModifierKeys.Control); break;
            case "right": Viewport.HandleKey(Key.NumPad3, ModifierKeys.None); break;
            case "left": Viewport.HandleKey(Key.NumPad3, ModifierKeys.Control); break;
            case "top": Viewport.HandleKey(Key.NumPad7, ModifierKeys.None); break;
            case "bottom": Viewport.HandleKey(Key.NumPad7, ModifierKeys.Control); break;
            case "persp": if (Viewport.Orthographic) Viewport.HandleKey(Key.NumPad5, ModifierKeys.None); break;
            case "ortho": if (!Viewport.Orthographic) Viewport.HandleKey(Key.NumPad5, ModifierKeys.None); break;
            case "frame_all": Viewport.HandleKey(Key.Home, ModifierKeys.None); break;
            case "frame_selected": Viewport.FrameSelected(); break;
            default: return new { ok = false, error = "view must be front|back|right|left|top|bottom|persp|ortho|frame_all|frame_selected" };
        }

        return new { ok = true, view, orthographic = Viewport.Orthographic };
    }

    internal object McpOpen(string path)
    {
        if (!File.Exists(path)) return new { ok = false, error = "file not found" };
        OpenProject(Path.GetFullPath(path));
        return new { ok = true, project = _projectPath };
    }

    internal object McpSetVisibility(int id, bool visible)
    {
        if (!Viewport.ObjectIds.Contains(id)) return new { ok = false, error = $"no object {id}" };
        Viewport.SetObjectVisible(id, visible);
        return new { ok = true, id, visible };
    }
}
