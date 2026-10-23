using System.ComponentModel;
using System.Numerics;

namespace PolyView.Commands;

/// <summary>Material system commands for menus and MCP (docs/13). All edits are undoable.</summary>
static class MaterialCommands
{
    internal static object Describe(ModelViewport vp, PvMaterial m, int? slot = null) => new
    {
        slot, id = m.Id.ToString(), name = m.Name, users = vp.UserCount(m),
        base_color = new[] { m.BaseColor.X, m.BaseColor.Y, m.BaseColor.Z, m.BaseColor.W },
        metallic = m.Metallic, roughness = m.Roughness, ior = m.Ior,
        emission = new[] { m.EmissiveColor.X, m.EmissiveColor.Y, m.EmissiveColor.Z }, emission_strength = m.EmissiveStrength,
        blend_mode = m.AlphaMode.ToString().ToLowerInvariant(), alpha_cutoff = m.AlphaCutoff, backface_culling = !m.DoubleSided,
    };

    static PvMaterial? Find(ModelViewport vp, string key) =>
        Guid.TryParse(key, out var g) ? vp.MaterialLibrary.Find(g) : vp.MaterialLibrary.Materials.Find(m => m.Name == key);

    [PvCommand("material.list", "material", "列出材质", "List all material data-blocks with their users")]
    static object List(MainWindow w) =>
        new { ok = true, materials = w.Viewport.MaterialLibrary.Materials.Select(m => Describe(w.Viewport, m)).ToArray() };

    [PvCommand("material.new", "material", "新建材质", "Create a new material in an object's slot (default grey, or a copy of the slot's current material)", Undoable = true)]
    static object New(MainWindow w, [Description("Object id (default: selected)")] int? id = null, [Description("Slot index")] int slot = 0,
        [Description("Optional name")] string? name = null)
    {
        var vp = w.Viewport;
        int oid = id ?? vp.SelectedId;
        vp.BeginUndoGroup();
        try
        {
            var m = vp.NewMaterial(oid, slot);
            if (m == null) return new { ok = false, error = $"no object {oid} with slot {slot}" };
            if (!string.IsNullOrWhiteSpace(name)) vp.EditMaterial(m, x => x.Name = name.Trim());
            return new { ok = true, id = oid, material = Describe(vp, m, slot) };
        }
        finally
        {
            vp.EndUndoGroup();
        }
    }

    [PvCommand("material.assign", "material", "指定材质", "Put an existing material (by name or id) into an object's slot; empty material = unlink (slot becomes default grey)", Undoable = true)]
    static object Assign(MainWindow w, [Description("Object id (default: selected)")] int? id = null, [Description("Slot index")] int slot = 0,
        [Description("Material name or id; empty = unlink")] string? material = null)
    {
        var vp = w.Viewport;
        int oid = id ?? vp.SelectedId;
        PvMaterial? m = null;
        if (!string.IsNullOrWhiteSpace(material) && (m = Find(vp, material.Trim())) == null) return new { ok = false, error = $"no material '{material}'" };
        if (!vp.AssignMaterial(oid, slot, m)) return new { ok = false, error = $"no object {oid} with slot {slot}" };
        return new { ok = true, id = oid, slot, material = m == null ? null : Describe(vp, m, slot) };
    }

    [PvCommand("material.set", "material", "设置材质", "Edit the material in an object's slot: base_color [r,g,b(,a)] linear 0-1, metallic, roughness, ior, emission [r,g,b], emission_strength, blend_mode opaque|mask|blend, alpha_cutoff, backface_culling, name. An empty slot gets a new material first.", Undoable = true)]
    static object Set(MainWindow w, [Description("Object id (default: selected)")] int? id = null, [Description("Slot index")] int slot = 0,
        [Description("[r,g,b] or [r,g,b,a], linear 0-1")] float[]? base_color = null, float? metallic = null, float? roughness = null, [Description("Index of refraction 1-4")] float? ior = null,
        [Description("[r,g,b]")] float[]? emission = null, float? emission_strength = null,
        [Description("opaque | mask | blend")] string? blend_mode = null, float? alpha_cutoff = null, bool? backface_culling = null, string? name = null)
    {
        var vp = w.Viewport;
        int oid = id ?? vp.SelectedId;
        AlphaMode? mode = null;
        if (blend_mode != null)
        {
            if (!Enum.TryParse<AlphaMode>(blend_mode.Trim(), true, out var am)) return new { ok = false, error = "blend_mode must be opaque | mask | blend" };
            mode = am;
        }

        vp.BeginUndoGroup();
        try
        {
            var m = vp.GetSlotMaterial(oid, slot) ?? vp.NewMaterial(oid, slot);
            if (m == null) return new { ok = false, error = $"no object {oid} with slot {slot}" };
            vp.EditMaterial(m, x =>
            {
                if (base_color is { Length: >= 3 } c) x.BaseColor = new Vector4(c[0], c[1], c[2], c.Length > 3 ? c[3] : x.BaseColor.W);
                if (metallic is { } me) x.Metallic = Math.Clamp(me, 0, 1);
                if (roughness is { } r) x.Roughness = Math.Clamp(r, 0, 1);
                if (ior is { } io) x.Ior = Math.Clamp(io, 1, 4);
                if (emission is { Length: >= 3 } em) x.EmissiveColor = new Vector3(em[0], em[1], em[2]);
                if (emission_strength is { } s) x.EmissiveStrength = Math.Max(0, s);
                if (mode is { } md) x.AlphaMode = md;
                if (alpha_cutoff is { } ac) x.AlphaCutoff = Math.Clamp(ac, 0, 1);
                if (backface_culling is { } bc) x.DoubleSided = !bc;
                if (!string.IsNullOrWhiteSpace(name)) x.Name = name.Trim();
            });
            return new { ok = true, id = oid, material = Describe(vp, m, slot) };
        }
        finally
        {
            vp.EndUndoGroup();
        }
    }
}
