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

    public int VertexCount => Mesh.Vertices.Length;
    public int TriangleCount => Mesh.TriangleCount;
    public Vector3 Min => Mesh.Min;
    public Vector3 Max => Mesh.Max;
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
