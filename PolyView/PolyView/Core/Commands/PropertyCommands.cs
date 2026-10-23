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
            materials = vp.GetMaterials(oid).Select((m, i) => new
            {
                slot = i, m.Name, base_color = new[] { m.BaseColor.X, m.BaseColor.Y, m.BaseColor.Z, m.BaseColor.W },
                metallic = m.Metallic, roughness = m.Roughness, emission = new[] { m.EmissionColor.X, m.EmissionColor.Y, m.EmissionColor.Z }, emission_strength = m.EmissionStrength,
            }).ToArray(),
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

    [PvCommand("material.set", "material", "设置材质", "Edit a material slot: base_color [r,g,b(,a)] 0-1 (shown in viewport), metallic, roughness, emission [r,g,b], emission_strength, name")]
    static object MaterialSet(MainWindow w, [Description("Object id")] int id, [Description("Slot index")] int slot = 0,
        [Description("[r,g,b] or [r,g,b,a], 0-1")] float[]? base_color = null, float? metallic = null, float? roughness = null,
        [Description("[r,g,b]")] float[]? emission = null, float? emission_strength = null, string? name = null)
    {
        bool ok = w.Viewport.UpdateMaterial(id, slot, m =>
        {
            if (base_color is { Length: >= 3 } c) m.BaseColor = new Vector4(c[0], c[1], c[2], c.Length > 3 ? c[3] : m.BaseColor.W);
            if (metallic is { } me) m.Metallic = Math.Clamp(me, 0, 1);
            if (roughness is { } r) m.Roughness = Math.Clamp(r, 0, 1);
            if (emission is { Length: >= 3 } em) m.EmissionColor = new Vector3(em[0], em[1], em[2]);
            if (emission_strength is { } s) m.EmissionStrength = Math.Max(0, s);
            if (!string.IsNullOrWhiteSpace(name)) m.Name = name.Trim();
        });
        return ok ? Get(w, id) : new { ok = false, error = $"no object {id} or slot {slot}" };
    }

    [PvCommand("ui.properties_tab", "view", "属性编辑器标签", "Switch the Properties editor tab: tool render output viewlayer scene world object modifiers particles physics constraints data material")]
    static object Tab(MainWindow w, [Description("Tab name")] string tab)
    {
        if (!Enum.TryParse<PropertiesEditor.Tab>(tab.Replace("_", ""), true, out var t)) return new { ok = false, error = "unknown tab" };
        w.Properties.ShowTab(t);
        return new { ok = true, tab = w.Properties.CurrentTab.ToString().ToLowerInvariant() };
    }
}
