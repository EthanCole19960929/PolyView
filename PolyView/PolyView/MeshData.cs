using System.Numerics;
using System.Runtime.InteropServices;

namespace PolyView;

[StructLayout(LayoutKind.Sequential)]
public struct Vertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 TexCoord;
    public Vector4 Color;

    public Vertex(Vector3 position, Vector3 normal)
    {
        Position = position;
        Normal = normal;
        TexCoord = Vector2.Zero;
        Color = Vector4.One;
    }
}

/// <summary>一段用同一种材质绘制的索引区间。</summary>
public sealed class MeshPart
{
    public int StartIndex { get; init; }
    public int IndexCount { get; init; }

    /// <summary>MeshData.Textures 里的下标，-1 表示没有贴图。</summary>
    public int TextureIndex { get; init; } = -1;

    public bool DoubleSided { get; init; }
}

/// <summary>已解码的 BGRA32 贴图。</summary>
public sealed class MeshTexture
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required byte[] Bgra { get; init; }
}

public sealed class MeshData
{
    public required string Name { get; init; }
    public required Vertex[] Vertices { get; init; }
    public required uint[] Indices { get; init; }
    public Vector3 Min { get; init; }
    public Vector3 Max { get; init; }

    /// <summary>为空时整份索引当作一段、无贴图、单面绘制（OBJ/STL）。</summary>
    public MeshPart[] Parts { get; init; } = [];

    public MeshTexture[] Textures { get; init; } = [];

    public int TriangleCount => Indices.Length / 3;
}

public readonly struct ImportResult
{
    public bool Ok { get; init; }
    public string Error { get; init; }
    public MeshData? Mesh { get; init; }

    public static ImportResult Success(MeshData mesh) => new() { Ok = true, Error = "", Mesh = mesh };

    public static ImportResult Fail(string error) => new() { Ok = false, Error = error, Mesh = null };
}
