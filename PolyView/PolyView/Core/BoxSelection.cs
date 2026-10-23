using System.Numerics;

namespace PolyView;

public enum BoxSelectMode
{
    /// <summary>Plain drag: the box result replaces the selection.</summary>
    Replace,

    /// <summary>Shift+drag: add to the selection.</summary>
    Extend,

    /// <summary>Ctrl+drag: remove from the selection.</summary>
    Subtract,
}

/// <summary>
/// Box select logic, kept free of WPF so it can be tested headless.
/// <para>
/// Hit rule (close to Blender object mode, which selects objects with any visible geometry inside the box):
/// an object is hit when its projected origin is inside the box, or when any of its vertices projects inside the box.
/// Quick paths: the projected world bounding box fully inside the box is a hit, no overlap is a miss.
/// Occlusion is ignored (hidden-behind objects are selected too, like Blender's X-ray box select); points behind the camera are skipped.
/// Objects without geometry (lights, empties) use the origin only.
/// </para>
/// </summary>
public static class BoxSelection
{
    /// <summary>Project a world point to viewport pixels (origin top-left). False when behind the camera.</summary>
    public static bool Project(Vector3 world, in Matrix4x4 viewProjection, float width, float height, out Vector2 screen)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        if (clip.W <= 1e-6f)
        {
            screen = default;
            return false;
        }

        screen = new Vector2((clip.X / clip.W * 0.5f + 0.5f) * width, (0.5f - clip.Y / clip.W * 0.5f) * height);
        return true;
    }

    static bool Inside(Vector2 p, Vector2 min, Vector2 max) => p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y;

    /// <summary>True when the object is inside the screen box (min/max in viewport pixels). sceneScale is the viewport's display scale matrix.</summary>
    public static bool Hits(SceneObject o, in Matrix4x4 sceneScale, in Matrix4x4 viewProjection, float width, float height, Vector2 boxMin, Vector2 boxMax)
    {
        var world = o.World * sceneScale;
        if (Project(Vector3.Transform(Vector3.Zero, world), viewProjection, width, height, out var origin) && Inside(origin, boxMin, boxMax))
        {
            return true;
        }

        var vertices = o.Mesh.Vertices;
        if (vertices.Length == 0)
        {
            return false;
        }

        // Projected bounding box: no overlap = miss, fully inside (all corners in front) = hit.
        var smin = new Vector2(float.MaxValue);
        var smax = new Vector2(float.MinValue);
        bool allFront = true;
        for (int i = 0; i < 8; i++)
        {
            var c = new Vector3(
                (i & 1) == 0 ? o.LocalMin.X : o.LocalMax.X,
                (i & 2) == 0 ? o.LocalMin.Y : o.LocalMax.Y,
                (i & 4) == 0 ? o.LocalMin.Z : o.LocalMax.Z);
            if (!Project(Vector3.Transform(c, world), viewProjection, width, height, out var s))
            {
                allFront = false;
                continue;
            }

            smin = Vector2.Min(smin, s);
            smax = Vector2.Max(smax, s);
        }

        if (allFront)
        {
            if (smax.X < boxMin.X || smin.X > boxMax.X || smax.Y < boxMin.Y || smin.Y > boxMax.Y)
            {
                return false;
            }

            if (Inside(smin, boxMin, boxMax) && Inside(smax, boxMin, boxMax))
            {
                return true;
            }
        }

        var m = world * viewProjection;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (Project(vertices[i].Position, m, width, height, out var s) && Inside(s, boxMin, boxMax))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Combine the box hits with the current selection. Active object: kept when still selected; otherwise the first
    /// hit (Replace / Extend) or the first remaining selected object (Subtract); -1 when nothing is selected.
    /// </summary>
    public static (List<int> Selected, int Active) Combine(IReadOnlyList<int> current, int active, IReadOnlyList<int> hits, BoxSelectMode mode)
    {
        var result = mode switch
        {
            BoxSelectMode.Replace => hits.Distinct().ToList(),
            BoxSelectMode.Extend => current.Concat(hits).Distinct().ToList(),
            _ => current.Where(id => !hits.Contains(id)).Distinct().ToList(),
        };

        int newActive = result.Contains(active) ? active
            : mode != BoxSelectMode.Subtract && hits.Count > 0 ? hits[0]
            : result.Count > 0 ? result[0] : -1;
        return (result, newActive);
    }

    public static BoxSelectMode ParseMode(string? mode) => (mode ?? "replace").Trim().ToLowerInvariant() switch
    {
        "replace" or "set" or "new" => BoxSelectMode.Replace,
        "extend" or "add" or "union" => BoxSelectMode.Extend,
        "subtract" or "sub" or "remove" or "deselect" => BoxSelectMode.Subtract,
        _ => throw new ArgumentException($"unknown mode '{mode}' (replace | extend | subtract)"),
    };
}
