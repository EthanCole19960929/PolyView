using System.ComponentModel;
using System.Numerics;

namespace PolyView.Commands;

/// <summary>Properties-editor commands (Object / Material / Data tabs) for MCP.</summary>
static class PropertyCommands
{
    [PvCommand("object.properties_get", "object", "获取物体属性", "Object properties: name, transform (in its rotation mode), rotation_mode, locks, visibility, mesh stats, material slots")]
    static object Get(MainWindow w, [Description("Object id (default: selected)")] int? id = null)
    {
        var vp = w.Viewport;
        int oid = id ?? vp.SelectedId;
        if (!vp.TryGetObjectInfo(oid, out var info) || !vp.TryGetTransform(oid, out var t)) return new { ok = false, error = $"no object {oid}" };
        var e = vp.GetEulerDegrees(oid);
        var (verts, edges, faces, tris) = vp.GetMeshStats(oid);
        return new
        {
            ok = true, id = oid, name = info.Name, visible = info.Visible,
            location = new[] { t.Position.X, t.Position.Y, t.Position.Z },
            rotation_deg = new[] { e.X, e.Y, e.Z }, rotation_mode = vp.GetRotationMode(oid),
            scale = new[] { t.Scale.X, t.Scale.Y, t.Scale.Z }, locks = vp.GetLocks(oid),
            mesh = new { name = vp.GetMeshDataName(oid), vertices = verts, edges, faces, triangles = tris, users = vp.GetMeshUsers(oid) },
            materials = vp.GetSlots(oid).Select((s, i) => s.Material is { } m ? MaterialCommands.Describe(vp, m, i) : new { slot = i, empty = true }).ToArray(),
        };
    }

    [PvCommand("object.rename", "object", "重命名物体", "Rename an object", Undoable = false)]
    static object Rename(MainWindow w, [Description("Object id")] int id, [Description("New name")] string name)
        => w.Viewport.RenameObject(id, name) ? new { ok = true, id, name } : new { ok = false, error = $"no object {id} or empty name" };

    [PvCommand("object.rotation_mode_set", "object", "设置旋转模式", "Rotation mode: XYZ XZY YXZ YZX ZXY ZYX (Euler order; values keep the same orientation)")]
    static object RotationMode(MainWindow w, [Description("Object id")] int id, [Description("XYZ | XZY | YXZ | YZX | ZXY | ZYX")] string mode)
    {
        mode = mode.Trim().ToUpperInvariant();
        if (!EulerOrder.Orders.Contains(mode)) return new { ok = false, error = "mode must be XYZ | XZY | YXZ | YZX | ZXY | ZYX" };
        w.Viewport.SetRotationMode(id, mode);
        var e = w.Viewport.GetEulerDegrees(id);
        return new { ok = true, id, mode, rotation_deg = new[] { e.X, e.Y, e.Z } };
    }

    [PvCommand("object.transform_lock_set", "object", "锁定变换", "Lock / unlock transform channels (affects interactive transforms)")]
    static object Lock(MainWindow w, [Description("Object id")] int id,
        [Description("location | rotation | scale")] string channel, [Description("Axes, e.g. \"xyz\" or \"z\"")] string axes, [Description("true = lock")] bool locked)
    {
        int b = channel.Trim().ToLowerInvariant() switch { "location" => 0, "rotation" => 3, "scale" => 6, _ => -1 };
        if (b < 0) return new { ok = false, error = "channel must be location | rotation | scale" };
        foreach (var c in axes.ToLowerInvariant())
        {
            if (c is >= 'x' and <= 'z') w.Viewport.SetLock(id, b + (c - 'x'), locked);
        }

        return new { ok = true, id, locks = w.Viewport.GetLocks(id) };
    }

    [PvCommand("ui.properties_tab", "view", "属性编辑器标签", "Switch the Properties editor tab: tool render output viewlayer scene world object modifiers particles physics constraints data material")]
    static object Tab(MainWindow w, [Description("Tab name")] string tab)
    {
        if (!Enum.TryParse<PropertiesEditor.Tab>(tab.Replace("_", ""), true, out var t)) return new { ok = false, error = "unknown tab" };
        w.Properties.ShowTab(t);
        return new { ok = true, tab = w.Properties.CurrentTab.ToString().ToLowerInvariant() };
    }
}
