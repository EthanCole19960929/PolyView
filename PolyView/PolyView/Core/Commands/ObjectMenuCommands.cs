using System.ComponentModel;

namespace PolyView.Commands;

/// <summary>Viewport right-click Object context menu operators (Blender object.* names). They act on the current selection.</summary>
static class ObjectMenuCommands
{
    static object Sel(MainWindow w, int[]? ids)
    {
        if (ids is { Length: > 0 })
        {
            w.Viewport.Select(ids[^1]);
        }

        return w.Viewport.SelectedIds;
    }

    [PvCommand("object.shade_smooth", "object", "平滑着色", "Shade selected objects smooth (recompute normals)")]
    static object ShadeSmooth(MainWindow w, [Description("Object id (default: selection)")] int[]? ids = null)
    { Sel(w, ids); return new { ok = true, changed = w.Viewport.ShadeSelected(ModelViewport.ShadeMode.Smooth) }; }

    [PvCommand("object.shade_auto_smooth", "object", "自动平滑着色", "Shade smooth below an angle, sharp above (default 30°)")]
    static object ShadeAuto(MainWindow w, [Description("Object id (default: selection)")] int[]? ids = null, [Description("Angle in degrees")] float angle = 30f)
    { Sel(w, ids); return new { ok = true, changed = w.Viewport.ShadeSelected(ModelViewport.ShadeMode.Auto, angle) }; }

    [PvCommand("object.shade_flat", "object", "平直着色", "Shade selected objects flat (face normals)")]
    static object ShadeFlat(MainWindow w, [Description("Object id (default: selection)")] int[]? ids = null)
    { Sel(w, ids); return new { ok = true, changed = w.Viewport.ShadeSelected(ModelViewport.ShadeMode.Flat) }; }

    [PvCommand("object.origin_set", "object", "设置原点", "Set origin: origin_geometry | geometry_origin | origin_cursor | origin_center_of_mass")]
    static object OriginSet(MainWindow w, [Description("origin_geometry | geometry_origin | origin_cursor | origin_center_of_mass")] string type,
        [Description("Object id (default: selection)")] int[]? ids = null)
    {
        ModelViewport.OriginMode? mode = type.Trim().ToLowerInvariant() switch
        {
            "origin_geometry" => ModelViewport.OriginMode.OriginToGeometry,
            "geometry_origin" => ModelViewport.OriginMode.GeometryToOrigin,
            "origin_cursor" => ModelViewport.OriginMode.OriginToCursor,
            "origin_center_of_mass" or "origin_mass" => ModelViewport.OriginMode.OriginToMass,
            _ => null,
        };
        if (mode == null) return new { ok = false, error = "unknown type" };
        Sel(w, ids);
        return new { ok = true, changed = w.Viewport.SetOriginSelected(mode.Value) };
    }

    [PvCommand("object.copy", "object", "复制物体到剪贴板（Ctrl C）", "Copy selected objects to PolyView's object clipboard")]
    static object Copy(MainWindow w, [Description("Object id (default: selection)")] int[]? ids = null)
    { Sel(w, ids); w.Viewport.CopySelected(); return new { ok = true }; }

    [PvCommand("object.paste", "object", "粘贴物体（Ctrl V）", "Paste objects from PolyView's object clipboard", Undoable = true)]
    static object Paste(MainWindow w) => new { ok = true, ids = w.Viewport.PasteObjects() };

    [PvCommand("object.duplicate_move", "object", "复制物体（Shift D）", "Duplicate selected objects (any object, incl. imported models)", Undoable = true)]
    static object DuplicateMove(MainWindow w, [Description("Object id (default: selection)")] int[]? ids = null)
    { Sel(w, ids); return new { ok = true, ids = w.Viewport.DuplicateObjects(linked: false) }; }

    [PvCommand("object.duplicate_linked", "object", "关联复制（Alt D）", "Duplicate selected objects sharing the same mesh data", Undoable = true)]
    static object DuplicateLinked(MainWindow w, [Description("Object id (default: selection)")] int[]? ids = null)
    { Sel(w, ids); return new { ok = true, ids = w.Viewport.DuplicateObjects(linked: true) }; }

    [PvCommand("object.mirror", "object", "镜像", "Mirror selected objects across a global axis: x | y | z", Undoable = true)]
    static object Mirror(MainWindow w, [Description("x | y | z")] string axis, [Description("Object id (default: selection)")] int[]? ids = null)
    {
        int a = axis.Trim().ToLowerInvariant() switch { "x" => 0, "y" => 1, "z" => 2, _ => -1 };
        if (a < 0) return new { ok = false, error = "axis must be x | y | z" };
        Sel(w, ids);
        return new { ok = true, changed = w.Viewport.MirrorSelected(a) };
    }

    [PvCommand("view3d.snap", "view", "吸附", "Snap: selected_to_cursor | cursor_to_selected | cursor_to_world_origin", Undoable = true)]
    static object Snap(MainWindow w, [Description("selected_to_cursor | cursor_to_selected | cursor_to_world_origin")] string mode)
    {
        ModelViewport.SnapMode? m = mode.Trim().ToLowerInvariant() switch
        {
            "selected_to_cursor" => ModelViewport.SnapMode.SelectionToCursor,
            "cursor_to_selected" => ModelViewport.SnapMode.CursorToSelected,
            "cursor_to_world_origin" => ModelViewport.SnapMode.CursorToWorld,
            _ => null,
        };
        if (m == null) return new { ok = false, error = "unknown mode" };
        w.Viewport.Snap(m.Value);
        var c = w.Viewport.Cursor3D;
        return new { ok = true, cursor = new[] { c.X, c.Y, c.Z } };
    }

    [PvCommand("object.delete_selected", "object", "删除选中物体（X）", "Delete all selected objects", Destructive = true)]
    static object DeleteSelected(MainWindow w) => new { ok = true, deleted = w.Viewport.DeleteSelectedConfirm(ask: false) };
}
