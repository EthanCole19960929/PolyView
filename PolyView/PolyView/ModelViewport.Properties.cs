using System.Numerics;

namespace PolyView;

/// <summary>Euler rotation orders (Blender "Rotation Mode"). Order "XYZ" = X applied first (matches ObjectTransform.FromEuler).</summary>
public static class EulerOrder
{
    public static readonly string[] Orders = ["XYZ", "XZY", "YXZ", "YZX", "ZXY", "ZYX"];

    static Vector3 Axis(char c) => c == 'X' ? Vector3.UnitX : c == 'Y' ? Vector3.UnitY : Vector3.UnitZ;
    static int Index(char c) => c - 'X';

    public static Quaternion ToQuaternion(Vector3 degrees, string order)
    {
        var r = degrees * (MathF.PI / 180f);
        float[] a = [r.X, r.Y, r.Z];
        var m = Matrix4x4.Identity;
        foreach (var c in order)
        {
            m *= Matrix4x4.CreateFromAxisAngle(Axis(c), a[Index(c)]);
        }

        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }

    /// <summary>Decompose a rotation into the given order; returns degrees in X/Y/Z slots.</summary>
    public static Vector3 FromQuaternion(Quaternion q, string order, Vector3? hint = null)
    {
        var m = Matrix4x4.CreateFromQuaternion(q);
        // Change of basis: rows of P are the order's axes, so M' = P M P^T is Rx Ry Rz (angles possibly sign-flipped).
        var e0 = Axis(order[0]); var e1 = Axis(order[1]); var e2 = Axis(order[2]);
        var p = new Matrix4x4(e0.X, e0.Y, e0.Z, 0, e1.X, e1.Y, e1.Z, 0, e2.X, e2.Y, e2.Z, 0, 0, 0, 0, 1);
        var mp = p * m * Matrix4x4.Transpose(p);
        float b = MathF.Asin(Math.Clamp(-mp.M13, -1f, 1f));
        float a = MathF.Atan2(mp.M23, mp.M33);
        float c = MathF.Atan2(mp.M12, mp.M11);
        Vector3 best = default;
        float bestErr = float.MaxValue;
        foreach (var s in new[] { 1f, -1f })
        {
            var slots = new float[3];
            slots[Index(order[0])] = a * s; slots[Index(order[1])] = b * s; slots[Index(order[2])] = c * s;
            var deg = new Vector3(slots[0], slots[1], slots[2]) * (180f / MathF.PI);
            var back = ToQuaternion(deg, order);
            float err = 1f - MathF.Abs(Quaternion.Dot(back, Quaternion.Normalize(q)));
            if (err < bestErr) { bestErr = err; best = deg; }
        }

        return Clean(best);
    }

    static Vector3 Clean(Vector3 v)
    {
        static float C(float f) => MathF.Abs(f) < 1e-4f ? 0f : f;
        return new Vector3(C(v.X), C(v.Y), C(v.Z));
    }
}

/// <summary>Editable material values for one slot (Principled BSDF subset). Base color is written to vertex colors.</summary>
public sealed class MaterialSlotProps
{
    public string Name { get; set; } = "Material";
    public Vector4 BaseColor { get; set; } = Vector4.One;
    public float Metallic { get; set; }
    public float Roughness { get; set; } = 0.5f;
    public Vector3 EmissionColor { get; set; } = Vector3.Zero;
    public float EmissionStrength { get; set; } = 1f;
}

/// <summary>Properties-editor backend: rotation mode, transform locks, live (scrub) transforms with one undo step, materials.</summary>
public partial class ModelViewport
{
    readonly Dictionary<int, string> _rotationModes = [];
    readonly Dictionary<int, bool[]> _locks = [];
    readonly Dictionary<MeshData, List<MaterialSlotProps>> _materials = [];
    int _liveId = -1;
    ObjectTransform _liveStart;

    public string GetRotationMode(int id) => _rotationModes.TryGetValue(id, out var m) ? m : "XYZ";

    public void SetRotationMode(int id, string mode)
    {
        if (!EulerOrder.Orders.Contains(mode) || !TryGetObject(id, out _)) return;
        _rotationModes[id] = mode;
        ObjectChanged?.Invoke(this, id);
    }

    public Vector3 GetEulerDegrees(int id) =>
        TryGetObject(id, out var o) ? EulerOrder.FromQuaternion(o.Transform.Rotation, GetRotationMode(id)) : Vector3.Zero;

    /// <summary>Lock flags: 0-2 location, 3-5 rotation, 6-8 scale.</summary>
    public bool[] GetLocks(int id) => _locks.TryGetValue(id, out var l) ? l : new bool[9];

    public void SetLock(int id, int index, bool locked)
    {
        if (index is < 0 or > 8 || !TryGetObject(id, out _)) return;
        if (!_locks.TryGetValue(id, out var l)) _locks[id] = l = new bool[9];
        l[index] = locked;
    }

    /// <summary>Re-apply locked channels from the pre-transform value (used after interactive gizmo / G R S transforms).</summary>
    internal ObjectTransform ApplyLocks(int id, ObjectTransform before, ObjectTransform after)
    {
        if (!_locks.TryGetValue(id, out var l)) return after;
        var p = after.Position; var s = after.Scale;
        if (l[0]) p.X = before.Position.X; if (l[1]) p.Y = before.Position.Y; if (l[2]) p.Z = before.Position.Z;
        if (l[6]) s.X = before.Scale.X; if (l[7]) s.Y = before.Scale.Y; if (l[8]) s.Z = before.Scale.Z;
        after.Position = p; after.Scale = s;
        if (l[3] || l[4] || l[5])
        {
            var mode = GetRotationMode(id);
            var eb = EulerOrder.FromQuaternion(before.Rotation, mode);
            var ea = EulerOrder.FromQuaternion(after.Rotation, mode);
            if (l[3]) ea.X = eb.X; if (l[4]) ea.Y = eb.Y; if (l[5]) ea.Z = eb.Z;
            after.Rotation = EulerOrder.ToQuaternion(ea, mode);
        }

        return after;
    }

    /// <summary>Set a transform while dragging a number field: no undo entry until EndLiveTransform.</summary>
    public void LiveTransform(int id, ObjectTransform transform)
    {
        if (!TryGetObject(id, out var o)) return;
        if (_liveId != id)
        {
            EndLiveTransform();
            _liveId = id;
            _liveStart = o.Transform;
        }

        ApplyTransform(o, transform, commit: false);
    }

    public void EndLiveTransform()
    {
        if (_liveId < 0) return;
        int id = _liveId;
        _liveId = -1;
        if (TryGetObject(id, out var o))
        {
            if (!o.Transform.Equals(_liveStart)) PushUndo(id, _liveStart, o.Transform);
            ApplyTransform(o, o.Transform, commit: true);
        }
    }

    public string GetMeshName(int id) => TryGetObject(id, out var o) ? o.Mesh.Name : "";

    public (int Vertices, int Edges, int Faces, int Triangles) GetMeshStats(int id)
    {
        if (!TryGetObject(id, out var o)) return default;
        var idx = o.Mesh.Indices;
        var edges = new HashSet<long>();
        for (int i = 0; i + 2 < idx.Length; i += 3)
        {
            AddEdge(edges, idx[i], idx[i + 1]); AddEdge(edges, idx[i + 1], idx[i + 2]); AddEdge(edges, idx[i + 2], idx[i]);
        }

        return (o.VertexCount, edges.Count, o.TriangleCount, o.TriangleCount);

        static void AddEdge(HashSet<long> set, uint a, uint b) => set.Add(a < b ? ((long)a << 32) | b : ((long)b << 32) | a);
    }

    /// <summary>Material slots, grouped like the outliner: one per distinct texture index of the mesh parts.</summary>
    List<int[]> SlotParts(MeshData mesh)
    {
        var groups = new List<int[]>();
        if (mesh.Parts.Length == 0) { groups.Add([-1]); return groups; }
        foreach (var key in mesh.Parts.Select(p => p.TextureIndex).Distinct())
        {
            groups.Add(Enumerable.Range(0, mesh.Parts.Length).Where(i => mesh.Parts[i].TextureIndex == key).ToArray());
        }

        return groups;
    }

    IEnumerable<uint> SlotIndices(MeshData mesh, int[] parts)
    {
        if (parts.Length == 1 && parts[0] < 0) return mesh.Indices;
        return parts.SelectMany(p => mesh.Indices.Skip(mesh.Parts[p].StartIndex).Take(mesh.Parts[p].IndexCount));
    }

    public IReadOnlyList<MaterialSlotProps> GetMaterials(int id)
    {
        if (!TryGetObject(id, out var o)) return [];
        if (_materials.TryGetValue(o.Mesh, out var list)) return list;
        list = [];
        var groups = SlotParts(o.Mesh);
        for (int i = 0; i < groups.Count; i++)
        {
            var first = SlotIndices(o.Mesh, groups[i]).Cast<uint?>().FirstOrDefault();
            var c = first is uint v && v < o.Mesh.Vertices.Length ? o.Mesh.Vertices[v].Color : 0xFFFFFFFF;
            list.Add(new MaterialSlotProps
            {
                Name = i == 0 ? "Material" : $"Material.{i:000}",
                BaseColor = new Vector4(c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF, (c >> 24) & 0xFF) / 255f,
            });
        }

        _materials[o.Mesh] = list;
        return list;
    }

    /// <summary>Apply a slot's values; base color is baked into the vertex colors and re-uploaded for every user of the mesh.</summary>
    public bool UpdateMaterial(int id, int slot, Action<MaterialSlotProps> edit)
    {
        var list = GetMaterials(id);
        if (slot < 0 || slot >= list.Count || !TryGetObject(id, out var o)) return false;
        var before = list[slot].BaseColor;
        edit(list[slot]);
        if (list[slot].BaseColor != before)
        {
            uint packed = Vertex.PackColor(list[slot].BaseColor);
            var verts = o.Mesh.Vertices;
            foreach (var i in SlotIndices(o.Mesh, SlotParts(o.Mesh)[slot]).Distinct())
            {
                if (i < verts.Length) verts[i].Color = packed;
            }

            if (_renderer != null)
            {
                foreach (var user in _objects.Where(x => ReferenceEquals(x.Mesh, o.Mesh)))
                {
                    _renderer.AddMesh(user.Id, user.Mesh);
                    _renderer.SetWorld(user.Id, user.World);
                    _renderer.SetVisible(user.Id, user.Visible);
                }
            }

            Invalidate();
        }

        ObjectChanged?.Invoke(this, id);
        return true;
    }
}
