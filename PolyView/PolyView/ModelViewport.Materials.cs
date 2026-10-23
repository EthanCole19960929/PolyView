namespace PolyView;

/// <summary>
/// Material system, viewport side (docs/13 part 2): material library, per-object slots (Link = Object), new / assign /
/// unlink, undoable edits (number-field scrubs collapse into one step), and pushing material values to the renderer.
/// </summary>
public sealed partial class ModelViewport
{
    public MaterialLibrary MaterialLibrary { get; } = new();

    /// <summary>Raised when slots, assignments or the library change, and after a material undo / redo (rebuild UI).</summary>
    public event EventHandler? MaterialsChanged;

    readonly Dictionary<MeshData, int[]> _partSlots = [];
    readonly Dictionary<int, SceneRenderer.MaterialParams[]> _slotParams = [];
    PvMaterial? _liveMaterial;
    PvMaterial? _liveBefore;

    /// <summary>Slot index of each mesh part: one slot per distinct texture group, in order of first use.</summary>
    int[] PartSlots(MeshData mesh)
    {
        if (_partSlots.TryGetValue(mesh, out var map)) return map;
        var keys = new List<int>();
        map = new int[mesh.Parts.Length];
        for (int i = 0; i < mesh.Parts.Length; i++)
        {
            int k = keys.IndexOf(mesh.Parts[i].TextureIndex);
            if (k < 0) { keys.Add(mesh.Parts[i].TextureIndex); k = keys.Count - 1; }
            map[i] = k;
        }

        _partSlots[mesh] = map;
        return map;
    }

    /// <summary>Sub-meshes of an object (index ranges with their slot index).</summary>
    public IReadOnlyList<SubMesh> GetSubMeshes(int id)
    {
        if (!TryGetObject(id, out var o)) return [];
        if (o.Mesh.Parts.Length == 0) return [new SubMesh { IndexStart = 0, IndexCount = o.Mesh.Indices.Length, MaterialIndex = 0 }];
        var map = PartSlots(o.Mesh);
        return o.Mesh.Parts.Select((p, i) => new SubMesh { IndexStart = p.StartIndex, IndexCount = p.IndexCount, MaterialIndex = map[i] }).ToList();
    }

    static bool HasGeometry(SceneObject o) => o.Mesh.Indices.Length > 0 && o.Mesh.Vertices.Length > 0;

    List<MaterialSlot> EnsureSlots(SceneObject o)
    {
        if (o.MaterialSlots.Count == 0 && HasGeometry(o))
        {
            int count = o.Mesh.Parts.Length == 0 ? 1 : Math.Max(1, PartSlots(o.Mesh).DefaultIfEmpty(0).Max() + 1);
            for (int i = 0; i < count; i++) o.MaterialSlots.Add(new MaterialSlot());
        }

        return o.MaterialSlots;
    }

    public IReadOnlyList<MaterialSlot> GetSlots(int id) => TryGetObject(id, out var o) ? EnsureSlots(o) : [];

    /// <summary>Material used by a slot, or null when the slot is empty (renders as the default grey).</summary>
    public PvMaterial? GetSlotMaterial(int id, int slot)
    {
        var slots = GetSlots(id);
        return slot >= 0 && slot < slots.Count ? slots[slot].Material : null;
    }

    /// <summary>Display name for the outliner: material name, or Blender-style "Material" names for empty slots.</summary>
    public string GetSlotDisplayName(int id, int slot) =>
        GetSlotMaterial(id, slot)?.Name ?? (slot == 0 ? "Material" : $"Material.{slot:000}");

    /// <summary>Number of slots using the material (Blender "users"), plus 1 for a fake user.</summary>
    public int UserCount(PvMaterial m) =>
        _objects.Sum(o => o.MaterialSlots.Count(s => ReferenceEquals(s.Material, m))) + (m.FakeUser ? 1 : 0);

    void RaiseMaterialsChanged(int id = -1)
    {
        Invalidate();
        MaterialsChanged?.Invoke(this, EventArgs.Empty);
        if (id >= 0) ObjectChanged?.Invoke(this, id);
        SceneChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Blender "New": a new default material in the slot (a copy of the current one when the slot is filled). Undoable.</summary>
    public PvMaterial? NewMaterial(int id, int slot)
    {
        if (!TryGetObject(id, out var o)) return null;
        var slots = EnsureSlots(o);
        if (slot < 0 || slot >= slots.Count) return null;
        var old = slots[slot].Material;
        var m = new PvMaterial();
        if (old != null) m.CopyFrom(old);
        m.Name = MaterialLibrary.UniqueName(old?.Name is { } n ? BaseMaterialName(n) : "Material");
        m.FakeUser = false;
        void Do()
        {
            if (!MaterialLibrary.Materials.Contains(m)) MaterialLibrary.Materials.Add(m);
            slots[slot].Material = m;
            RaiseMaterialsChanged(id);
        }

        void Undo()
        {
            slots[slot].Material = old;
            MaterialLibrary.Materials.Remove(m);
            RaiseMaterialsChanged(id);
        }

        Do();
        PushCustomUndo(Undo, Do);
        return m;
    }

    static string BaseMaterialName(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot > 0 && name.Length - dot == 4 && name[(dot + 1)..].All(char.IsDigit) ? name[..dot] : name;
    }

    /// <summary>Put an existing material (or null = Unlink) into a slot. Undoable.</summary>
    public bool AssignMaterial(int id, int slot, PvMaterial? m)
    {
        if (!TryGetObject(id, out var o)) return false;
        var slots = EnsureSlots(o);
        if (slot < 0 || slot >= slots.Count) return false;
        var old = slots[slot].Material;
        if (ReferenceEquals(old, m)) return true;
        if (m != null && !MaterialLibrary.Materials.Contains(m)) MaterialLibrary.Materials.Add(m);
        void Set(PvMaterial? v) { slots[slot].Material = v; RaiseMaterialsChanged(id); }
        Set(m);
        PushCustomUndo(() => Set(old), () => Set(m));
        return true;
    }

    /// <summary>Blender "+" Add Material Slot (empty). Undoable.</summary>
    public bool AddMaterialSlot(int id)
    {
        if (!TryGetObject(id, out var o) || !HasGeometry(o)) return false;
        var slots = EnsureSlots(o);
        var added = new MaterialSlot();
        void Do() { slots.Add(added); RaiseMaterialsChanged(id); }
        void Undo() { slots.Remove(added); RaiseMaterialsChanged(id); }
        Do();
        PushCustomUndo(Undo, Do);
        return true;
    }

    /// <summary>
    /// Blender "-" Remove Material Slot (the material itself stays in the library). The last slot can't be removed.
    /// P0: parts that used a removed / shifted slot keep their index (later slots shift down, as in Blender's list).
    /// </summary>
    public bool RemoveMaterialSlot(int id, int slot)
    {
        if (!TryGetObject(id, out var o)) return false;
        var slots = EnsureSlots(o);
        if (slots.Count <= 1 || slot < 0 || slot >= slots.Count) return false;
        var removed = slots[slot];
        void Do() { slots.Remove(removed); RaiseMaterialsChanged(id); }
        void Undo() { slots.Insert(Math.Min(slot, slots.Count), removed); RaiseMaterialsChanged(id); }
        Do();
        PushCustomUndo(Undo, Do);
        return true;
    }

    /// <summary>
    /// Change material values. live = true while a number field is being dragged (no undo entry yet);
    /// the final live = false call records one undo step from the value before the drag.
    /// </summary>
    public bool EditMaterial(PvMaterial m, Action<PvMaterial> edit, bool live = false)
    {
        if (_liveMaterial != null && !ReferenceEquals(_liveMaterial, m)) CommitLiveMaterial();
        _liveBefore ??= m.CloneValues();
        _liveMaterial = m;
        edit(m);
        m.Version++;
        Invalidate();
        if (_selectedId >= 0) ObjectChanged?.Invoke(this, _selectedId);
        if (!live) CommitLiveMaterial();
        return true;
    }

    void CommitLiveMaterial()
    {
        var m = _liveMaterial;
        var before = _liveBefore;
        _liveMaterial = null;
        _liveBefore = null;
        if (m == null || before == null) return;
        var after = m.CloneValues();
        if (SameValues(before, after)) return;
        void Apply(PvMaterial v) { m.CopyFrom(v); RaiseMaterialsChanged(_selectedId); }
        PushCustomUndo(() => Apply(before), () => Apply(after));
        if (before.Name != after.Name) SceneChanged?.Invoke(this, EventArgs.Empty);
    }

    static bool SameValues(PvMaterial a, PvMaterial b) =>
        a.Name == b.Name && a.BaseColor == b.BaseColor && a.Metallic == b.Metallic && a.Roughness == b.Roughness && a.Ior == b.Ior
        && a.EmissiveColor == b.EmissiveColor && a.EmissiveStrength == b.EmissiveStrength && a.AlphaMode == b.AlphaMode
        && a.AlphaCutoff == b.AlphaCutoff && a.DoubleSided == b.DoubleSided && a.FakeUser == b.FakeUser
        && a.NormalScale == b.NormalScale && a.OcclusionStrength == b.OcclusionStrength;

    /// <summary>Copy slot assignments (Shift+D / Alt+D share the same materials, like Blender).</summary>
    internal void CopySlots(SceneObject from, SceneObject to)
    {
        to.MaterialSlots.Clear();
        foreach (var s in from.MaterialSlots) to.MaterialSlots.Add(new MaterialSlot { Material = s.Material });
    }

    /// <summary>Called before every frame: material values of all slots to the renderer.</summary>
    void PushMaterials()
    {
        if (_renderer == null) return;
        foreach (var o in _objects)
        {
            if (!HasGeometry(o)) continue;
            var slots = EnsureSlots(o);
            if (!_slotParams.TryGetValue(o.Id, out var arr) || arr.Length != slots.Count)
            {
                _slotParams[o.Id] = arr = new SceneRenderer.MaterialParams[slots.Count];
            }

            for (int i = 0; i < slots.Count; i++)
            {
                arr[i] = slots[i].Material is { } m ? SceneRenderer.MaterialParams.From(m) : SceneRenderer.MaterialParams.Empty;
            }

            _renderer.SetMaterials(o.Id, arr, o.Mesh.Parts.Length > 0 ? PartSlots(o.Mesh) : null);
        }
    }

    // ---------------- Project (.pvproj) ----------------

    internal List<Project.ProjectMaterial> CaptureMaterials()
    {
        var used = MaterialLibrary.Materials.Where(m => m.FakeUser || UserCount(m) > 0).ToList();
        return used.Select(m => new Project.ProjectMaterial
        {
            Id = m.Id.ToString(), Name = m.Name,
            BaseColor = [m.BaseColor.X, m.BaseColor.Y, m.BaseColor.Z, m.BaseColor.W],
            Metallic = m.Metallic, Roughness = m.Roughness, Ior = m.Ior,
            Emission = [m.EmissiveColor.X, m.EmissiveColor.Y, m.EmissiveColor.Z], EmissionStrength = m.EmissiveStrength,
            AlphaMode = m.AlphaMode.ToString(), AlphaCutoff = m.AlphaCutoff, DoubleSided = m.DoubleSided, FakeUser = m.FakeUser,
        }).ToList();
    }

    internal static List<string?> CaptureSlots(SceneObject o) =>
        o.MaterialSlots.Select(s => s.Material?.Id.ToString()).ToList();

    internal void RestoreMaterials(List<Project.ProjectMaterial>? list)
    {
        MaterialLibrary.Materials.Clear();
        _partSlots.Clear();
        _slotParams.Clear();
        if (list == null) return;
        foreach (var pm in list)
        {
            var m = new PvMaterial
            {
                Id = Guid.TryParse(pm.Id, out var g) ? g : Guid.NewGuid(),
                Name = string.IsNullOrWhiteSpace(pm.Name) ? "Material" : pm.Name,
                BaseColor = pm.BaseColor is { Length: 4 } c ? new System.Numerics.Vector4(c[0], c[1], c[2], c[3]) : new(0.8f, 0.8f, 0.8f, 1f),
                Metallic = Math.Clamp(pm.Metallic, 0, 1),
                Roughness = Math.Clamp(pm.Roughness, 0, 1),
                Ior = pm.Ior is >= 1f and <= 4f ? pm.Ior : 1.5f,
                EmissiveColor = pm.Emission is { Length: 3 } e ? new System.Numerics.Vector3(e[0], e[1], e[2]) : System.Numerics.Vector3.One,
                EmissiveStrength = Math.Max(0, pm.EmissionStrength),
                AlphaMode = Enum.TryParse<AlphaMode>(pm.AlphaMode, true, out var am) ? am : AlphaMode.Opaque,
                AlphaCutoff = Math.Clamp(pm.AlphaCutoff, 0, 1),
                DoubleSided = pm.DoubleSided,
                FakeUser = pm.FakeUser,
            };
            MaterialLibrary.Materials.Add(m);
        }
    }

    internal void RestoreSlots(SceneObject o, List<string?>? ids)
    {
        if (ids == null || !HasGeometry(o)) return;
        var slots = EnsureSlots(o);
        for (int i = 0; i < ids.Count && i < slots.Count; i++)
        {
            slots[i].Material = Guid.TryParse(ids[i], out var g) ? MaterialLibrary.Find(g) : null;
        }
    }
}
