using System.Numerics;

namespace PolyView;

/// <summary>场景里的一个已加载对象（一个文件 = 一个对象）。Id 在本次运行内唯一且不复用。</summary>
public sealed class SceneObject
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public string? SourcePath { get; init; }
    public required MeshData Mesh { get; init; }

    /// <summary>只影响显示和拾取，不删除对象。</summary>
    public bool Visible { get; set; } = true;

    /// <summary>被隐藏的节点下标（MeshData.Nodes）。</summary>
    public HashSet<int> HiddenNodes { get; } = [];

    /// <summary>对象变换（不烘焙进顶点）。导入后顶点已平移到以包围盒中心为原点，Position = 原中心。</summary>
    public ObjectTransform Transform = ObjectTransform.Identity;

    public int VertexCount => Mesh.Vertices.Length;
    public int TriangleCount => Mesh.TriangleCount;

    /// <summary>局部（模型空间）包围盒。</summary>
    public Vector3 LocalMin => Mesh.Min;
    public Vector3 LocalMax => Mesh.Max;

    public Matrix4x4 World => Transform.Matrix;

    /// <summary>世界包围盒：局部包围盒 8 个角点乘 World 后取最小 / 最大。</summary>
    public Vector3 Min => WorldBounds().Min;
    public Vector3 Max => WorldBounds().Max;

    public (Vector3 Min, Vector3 Max) WorldBounds()
    {
        var world = World;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? LocalMin.X : LocalMax.X,
                (i & 2) == 0 ? LocalMin.Y : LocalMax.Y,
                (i & 4) == 0 ? LocalMin.Z : LocalMax.Z);
            var p = Vector3.Transform(corner, world);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        return (min, max);
    }
}

/// <summary>位置 / 旋转（四元数）/ 缩放。World = Scale * Rotation * Translation（System.Numerics 行向量约定）。</summary>
public struct ObjectTransform : IEquatable<ObjectTransform>
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;

    public static ObjectTransform Identity => new() { Position = Vector3.Zero, Rotation = Quaternion.Identity, Scale = Vector3.One };

    public readonly Matrix4x4 Matrix =>
        Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);

    /// <summary>欧拉角 XYZ（度），与 FromEuler 互逆：R = Rx * Ry * Rz。</summary>
    public readonly Vector3 EulerDegrees
    {
        get
        {
            var m = Matrix4x4.CreateFromQuaternion(Rotation);
            float y = MathF.Asin(Math.Clamp(-m.M13, -1f, 1f));
            float x = MathF.Atan2(m.M23, m.M33);
            float z = MathF.Atan2(m.M12, m.M11);
            return new Vector3(x, y, z) * (180f / MathF.PI);
        }
    }

    public static Quaternion FromEuler(Vector3 degrees)
    {
        var r = degrees * (MathF.PI / 180f);
        var m = Matrix4x4.CreateRotationX(r.X) * Matrix4x4.CreateRotationY(r.Y) * Matrix4x4.CreateRotationZ(r.Z);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }

    public readonly bool Equals(ObjectTransform other) =>
        Position == other.Position && Rotation == other.Rotation && Scale == other.Scale;

    public override readonly bool Equals(object? obj) => obj is ObjectTransform t && Equals(t);

    public override readonly int GetHashCode() => HashCode.Combine(Position, Rotation, Scale);
}

/// <summary>给场景树 / 属性栏用的只读快照。</summary>
public readonly record struct SceneObjectInfo(
    int Id,
    string Name,
    string? SourcePath,
    int VertexCount,
    int TriangleCount,
    Vector3 Min,
    Vector3 Max,
    bool Visible)
{
    public Vector3 Size => Max - Min;
}
