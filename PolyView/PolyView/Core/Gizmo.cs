using System.Numerics;

namespace PolyView;

public enum TransformTool
{
    Select,
    Move,
    Rotate,
    Scale,
}

/// <summary>
/// 变换手柄的几何（单位尺寸，启动时一次性生成）和屏幕空间拾取。
/// 部件编号：0 = X、1 = Y、2 = Z、3 = 中心（自由移动 / 等比缩放）。
/// </summary>
public static class Gizmo
{
    public const int PartCount = 7;
    public const int Center = 3;

    /// <summary>平面方块部件 4 = XY、5 = YZ、6 = XZ；排除的轴（锁平面时的法线轴）分别是 Z、X、Y。</summary>
    public static readonly int[] PlaneExcludedAxis = [2, 0, 1];
    static readonly (int U, int V)[] PlaneAxes = [(0, 1), (1, 2), (0, 2)];

    /// <summary>手柄目标屏幕尺寸（DIP，渲染时按物理像素等比换算）。</summary>
    public const float ScreenSize = 100f;

    public const float MoveCenterRadius = 0.08f;
    public const float ViewRingRadius = 1.2f;

    public static readonly Vector3[] Axes = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];

    static readonly Vector4[] Rgb =
    [
        new(0xE5 / 255f, 0x4B / 255f, 0x4B / 255f, 1f),
        new(0x8B / 255f, 0xC3 / 255f, 0x4A / 255f, 1f),
        new(0x4B / 255f, 0x8B / 255f, 0xE5 / 255f, 1f),
        new(0.95f, 0.95f, 0.95f, 1f),
    ];

    const int RingSegments = 64;

    /// <summary>一段绘制区间：线段（LineList）和三角形（TriangleList）各一段，都在同一个顶点缓冲里。</summary>
    public readonly record struct Range(int LineStart, int LineCount, int TriStart, int TriCount);

    /// <summary>
    /// 生成全部工具的顶点（Blender 风格）；ranges[(tool - 1) * PartCount + part]。
    /// 移动：细轴线 + 锥头、中心白色小圆（公告板，局部 XY 平面）、三个半透明平面方块；
    /// 旋转：三个轴向圆环 + 白色视角环（公告板）；缩放：轴线 + 末端方块 + 中心白圆。
    /// </summary>
    public static Vertex[] Build(out Range[] ranges)
    {
        var list = new List<Vertex>(8192);
        ranges = new Range[3 * PartCount];
        for (int tool = 0; tool < 3; tool++)
        {
            for (int part = 0; part < PartCount; part++)
            {
                int lineStart = list.Count;
                if (part < 3)
                {
                    uint color = Vertex.PackColor(Rgb[part]);
                    var a = Axes[part];
                    if (tool == 1)
                    {
                        AddRing(list, a, 1f, color);
                    }
                    else
                    {
                        Add(list, a * 0.12f, color);
                        Add(list, a * (tool == 0 ? 0.82f : 0.9f), color);
                    }
                }
                else if (part == Center)
                {
                    float radius = tool == 1 ? ViewRingRadius : tool == 0 ? MoveCenterRadius : 0.1f;
                    AddRing(list, Vector3.UnitZ, radius, Vertex.PackColor(Rgb[3]));
                }

                int lineCount = list.Count - lineStart;
                int triStart = list.Count;
                if (part < 3 && tool == 0)
                {
                    AddCone(list, Axes[part], 0.8f, 1f, 0.045f, Vertex.PackColor(Rgb[part]));
                }
                else if (part < 3 && tool == 2)
                {
                    AddCube(list, Axes[part] * 0.94f, 0.04f, Vertex.PackColor(Rgb[part]));
                }
                else if (part > Center && tool == 0)
                {
                    int plane = part - 4;
                    var color = Rgb[PlaneExcludedAxis[plane]];
                    color.W = 0.55f;
                    var (u, v) = PlaneAxes[plane];
                    AddQuad(list, Axes[u], Axes[v], 0.22f, 0.38f, Vertex.PackColor(color));
                }

                ranges[tool * PartCount + part] = new Range(lineStart, lineCount, triStart, list.Count - triStart);
            }
        }

        return list.ToArray();
    }

    static void AddQuad(List<Vertex> list, Vector3 u, Vector3 v, float from, float to, uint color)
    {
        var p0 = u * from + v * from;
        var p1 = u * to + v * from;
        var p2 = u * to + v * to;
        var p3 = u * from + v * to;
        Add(list, p0, color);
        Add(list, p1, color);
        Add(list, p2, color);
        Add(list, p0, color);
        Add(list, p2, color);
        Add(list, p3, color);
    }

    static void Add(List<Vertex> list, Vector3 p, uint color) => list.Add(new Vertex(p, Vector3.UnitY) { Color = color });

    static void Basis(Vector3 a, out Vector3 u, out Vector3 v)
    {
        u = MathF.Abs(a.Y) < 0.9f ? Vector3.Normalize(Vector3.Cross(a, Vector3.UnitY)) : Vector3.Normalize(Vector3.Cross(a, Vector3.UnitX));
        v = Vector3.Cross(a, u);
    }

    static void AddRing(List<Vertex> list, Vector3 axis, float radius, uint color)
    {
        Basis(axis, out var u, out var v);
        for (int i = 0; i < RingSegments; i++)
        {
            float t0 = i * MathF.Tau / RingSegments;
            float t1 = (i + 1) * MathF.Tau / RingSegments;
            Add(list, (u * MathF.Cos(t0) + v * MathF.Sin(t0)) * radius, color);
            Add(list, (u * MathF.Cos(t1) + v * MathF.Sin(t1)) * radius, color);
        }
    }

    static void AddCone(List<Vertex> list, Vector3 axis, float from, float to, float radius, uint color)
    {
        Basis(axis, out var u, out var v);
        const int segments = 12;
        var tip = axis * to;
        var baseCenter = axis * from;
        for (int i = 0; i < segments; i++)
        {
            float t0 = i * MathF.Tau / segments;
            float t1 = (i + 1) * MathF.Tau / segments;
            var p0 = baseCenter + (u * MathF.Cos(t0) + v * MathF.Sin(t0)) * radius;
            var p1 = baseCenter + (u * MathF.Cos(t1) + v * MathF.Sin(t1)) * radius;
            Add(list, tip, color);
            Add(list, p0, color);
            Add(list, p1, color);
            Add(list, baseCenter, color);
            Add(list, p1, color);
            Add(list, p0, color);
        }
    }

    static void AddCube(List<Vertex> list, Vector3 center, float half, uint color)
    {
        Span<Vector3> c = stackalloc Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            c[i] = center + new Vector3((i & 1) == 0 ? -half : half, (i & 2) == 0 ? -half : half, (i & 4) == 0 ? -half : half);
        }

        ReadOnlySpan<int> faces = [0, 1, 3, 2, 4, 6, 7, 5, 0, 4, 5, 1, 2, 3, 7, 6, 0, 2, 6, 4, 1, 5, 7, 3];
        for (int f = 0; f < 6; f++)
        {
            int a = faces[f * 4], b = faces[f * 4 + 1], cc = faces[f * 4 + 2], d = faces[f * 4 + 3];
            Add(list, c[a], color);
            Add(list, c[b], color);
            Add(list, c[cc], color);
            Add(list, c[a], color);
            Add(list, c[cc], color);
            Add(list, c[d], color);
        }
    }

    /// <summary>点到线段的屏幕距离。</summary>
    public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float len2 = ab.LengthSquared();
        float t = len2 < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }

    /// <summary>
    /// 屏幕空间拾取手柄部件。project 把世界点投影到视口 DIP 坐标（失败返回 null，例如在相机后面）。
    /// viewRight 用来算公告板圆的屏幕半径。返回部件编号，没打中返回 -1。tolerance 单位 DIP。
    /// </summary>
    public static int HitTest(TransformTool tool, Vector3 center, float size, Vector3 viewRight, Vector2 mouse, Func<Vector3, Vector2?> project, float tolerance = 8f)
    {
        if (tool == TransformTool.Select)
        {
            return -1;
        }

        var c = project(center);
        if (c == null)
        {
            return -1;
        }

        float centerRadius = tool == TransformTool.Rotate ? ViewRingRadius : tool == TransformTool.Move ? MoveCenterRadius : 0.1f;
        var edge = project(center + viewRight * centerRadius * size);
        float screenRadius = edge == null ? 0f : Vector2.Distance(edge.Value, c.Value);
        float toCenter = Vector2.Distance(mouse, c.Value);
        if (tool != TransformTool.Rotate && toCenter <= MathF.Max(screenRadius, 4f) + 4f)
        {
            return Center;
        }

        int best = -1;
        float bestDistance = tolerance;
        if (tool == TransformTool.Rotate && MathF.Abs(toCenter - screenRadius) < bestDistance)
        {
            best = Center;
            bestDistance = MathF.Abs(toCenter - screenRadius);
        }

        if (tool == TransformTool.Move)
        {
            for (int plane = 0; plane < 3; plane++)
            {
                var (u, v) = PlaneAxes[plane];
                var mid = project(center + (Axes[u] + Axes[v]) * 0.3f * size);
                var corner = project(center + (Axes[u] + Axes[v]) * 0.38f * size);
                if (mid != null && corner != null)
                {
                    float r = Vector2.Distance(mid.Value, corner.Value) + 4f;
                    float d = Vector2.Distance(mouse, mid.Value);
                    if (d < r)
                    {
                        return 4 + plane;
                    }
                }
            }
        }

        for (int axis = 0; axis < 3; axis++)
        {
            float d = float.MaxValue;
            if (tool == TransformTool.Rotate)
            {
                Basis(Axes[axis], out var u, out var v);
                Vector2? previous = null;
                for (int i = 0; i <= 48; i++)
                {
                    float t = i * MathF.Tau / 48;
                    var p = project(center + (u * MathF.Cos(t) + v * MathF.Sin(t)) * size);
                    if (p != null && previous != null)
                    {
                        d = MathF.Min(d, DistanceToSegment(mouse, previous.Value, p.Value));
                    }

                    previous = p;
                }
            }
            else
            {
                var end = project(center + Axes[axis] * size);
                if (end != null)
                {
                    d = DistanceToSegment(mouse, c.Value, end.Value);
                }
            }

            if (d < bestDistance)
            {
                bestDistance = d;
                best = axis;
            }
        }

        return best;
    }

    /// <summary>射线与平面求交，返回交点；几乎平行时返回 false。</summary>
    public static bool RayPlane(in PickRay ray, Vector3 planePoint, Vector3 normal, out Vector3 hit)
    {
        float denom = Vector3.Dot(ray.Direction, normal);
        hit = default;
        if (MathF.Abs(denom) < 1e-6f)
        {
            return false;
        }

        float t = Vector3.Dot(planePoint - ray.Origin, normal) / denom;
        hit = ray.Origin + ray.Direction * t;
        return true;
    }
}
