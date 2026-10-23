using System.Numerics;

namespace PolyView;

public readonly record struct PickRay(Vector3 Origin, Vector3 Direction);

public readonly record struct PickHit(int ObjectId, float Distance, int TriangleIndex)
{
    public static readonly PickHit None = new(-1, float.PositiveInfinity, -1);
    public bool IsHit => ObjectId >= 0;
}

/// <summary>射线拾取：先包围盒粗筛，再逐三角形 Möller–Trumbore，取最近命中。</summary>
public static class Picking
{
    /// <summary>
    /// 由视口内坐标（0..width, 0..height，单位不限，只要和宽高一致）构造世界空间射线。
    /// 矩阵沿用 System.Numerics 行向量约定（v * M），与渲染用的 view * projection 一致。
    /// </summary>
    public static PickRay CreateRay(float x, float y, float width, float height, in Matrix4x4 view, in Matrix4x4 projection)
    {
        float ndcX = 2f * x / MathF.Max(width, 1f) - 1f;
        float ndcY = 1f - 2f * y / MathF.Max(height, 1f);
        Matrix4x4.Invert(view * projection, out var inverse);
        var near = Vector4.Transform(new Vector4(ndcX, ndcY, 0f, 1f), inverse);
        var far = Vector4.Transform(new Vector4(ndcX, ndcY, 1f, 1f), inverse);
        var a = new Vector3(near.X, near.Y, near.Z) / near.W;
        var b = new Vector3(far.X, far.Y, far.Z) / far.W;
        return new PickRay(a, Vector3.Normalize(b - a));
    }

    /// <summary>射线与 AABB（slab 法）。返回进入距离，未命中为 false。</summary>
    public static bool RayAabb(in PickRay ray, Vector3 min, Vector3 max, out float tEnter)
    {
        var inv = new Vector3(1f / ray.Direction.X, 1f / ray.Direction.Y, 1f / ray.Direction.Z);
        var t1 = (min - ray.Origin) * inv;
        var t2 = (max - ray.Origin) * inv;
        var tMin = Vector3.Min(t1, t2);
        var tMax = Vector3.Max(t1, t2);
        tEnter = MathF.Max(MathF.Max(tMin.X, tMin.Y), MathF.Max(tMin.Z, 0f));
        float tExit = MathF.Min(MathF.Min(tMax.X, tMax.Y), tMax.Z);
        return !float.IsNaN(tEnter) && tExit >= tEnter;
    }

    /// <summary>Möller–Trumbore，双面。</summary>
    public static bool RayTriangle(in PickRay ray, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
    {
        const float Epsilon = 1e-9f;
        t = 0f;
        var e1 = v1 - v0;
        var e2 = v2 - v0;
        var p = Vector3.Cross(ray.Direction, e2);
        float det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < Epsilon)
        {
            return false;
        }

        float invDet = 1f / det;
        var s = ray.Origin - v0;
        float u = Vector3.Dot(s, p) * invDet;
        if (u < 0f || u > 1f)
        {
            return false;
        }

        var q = Vector3.Cross(s, e1);
        float v = Vector3.Dot(ray.Direction, q) * invDet;
        if (v < 0f || u + v > 1f)
        {
            return false;
        }

        t = Vector3.Dot(e2, q) * invDet;
        return t > 0f;
    }

    /// <summary>
    /// 在若干对象里找最近命中。world 是所有对象共用的世界变换（目前只有缩放滑块），
    /// 射线先变回模型空间再测，避免逐顶点变换。
    /// </summary>
    public static PickHit Pick(in PickRay worldRay, IReadOnlyList<SceneObject> objects, in Matrix4x4 world)
    {
        if (!Matrix4x4.Invert(world, out var inverseWorld))
        {
            return PickHit.None;
        }

        var origin = Vector3.Transform(worldRay.Origin, inverseWorld);
        var direction = Vector3.TransformNormal(worldRay.Direction, inverseWorld);
        // 不归一化：模型空间的 t 与世界空间 t 一致，可直接跨对象比较远近。
        var ray = new PickRay(origin, direction);
        var best = PickHit.None;
        for (int o = 0; o < objects.Count; o++)
        {
            var obj = objects[o];
            if (!RayAabb(ray, obj.Min, obj.Max, out float enter) || enter > best.Distance)
            {
                continue;
            }

            var vertices = obj.Mesh.Vertices;
            var indices = obj.Mesh.Indices;
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                uint a = indices[i], b = indices[i + 1], c = indices[i + 2];
                if (a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
                {
                    continue;
                }

                if (RayTriangle(ray, vertices[a].Position, vertices[b].Position, vertices[c].Position, out float t)
                    && t < best.Distance)
                {
                    best = new PickHit(obj.Id, t, i / 3);
                }
            }
        }

        return best;
    }
}
