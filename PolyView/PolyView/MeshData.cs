using System.Numerics;
using System.Runtime.InteropServices;

namespace PolyView;

[StructLayout(LayoutKind.Sequential)]
public struct Vertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 TexCoord;

    /// <summary>RGBA8 打包的顶点色（R 在最低字节），着色器里按 R8G8B8A8_UNorm 读。</summary>
    public uint Color;

    public Vertex(Vector3 position, Vector3 normal)
    {
        Position = position;
        Normal = normal;
        TexCoord = Vector2.Zero;
        Color = 0xFFFFFFFF;
    }

    public static uint PackColor(Vector4 color)
    {
        var c = Vector4.Clamp(color, Vector4.Zero, Vector4.One) * 255f + new Vector4(0.5f);
        return (uint)c.X | ((uint)c.Y << 8) | ((uint)c.Z << 16) | ((uint)c.W << 24);
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

    /// <summary>导入时的提示（例如跳过了哪些内容），显示在状态栏；空字符串表示没有。</summary>
    public string Notice { get; init; } = "";

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
