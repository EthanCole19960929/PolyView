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

    /// <summary>
    /// 场景节点层级（给大纲视图用），按深度优先顺序排列，父节点总在子节点前面；Nodes[i].Index == i。
    /// 顶点已经是世界坐标（变换烘焙过），节点的 Local / World 只用来显示和以后做变换，不要再乘一次。
    /// 导入器没有填时由 MeshImporter.Load 补一个包含全部 Parts 的根节点，所以成功导入后至少有一个节点。
    /// </summary>
    public SceneNode[] Nodes { get; internal set; } = [];
}

public enum SceneNodeKind
{
    Empty,
    Mesh,
    Camera,
    Light,
}

public enum SceneLightType
{
    Unknown,
    Directional,
    Point,
    Spot,
    Ambient,
    Area,
}

/// <summary>相机参数（没有的字段为 0）。</summary>
public sealed class SceneCamera
{
    public bool Orthographic { get; init; }

    /// <summary>垂直视场角（弧度），透视相机才有；Assimp 只给水平视场角时按宽高比换算，没有宽高比时就是水平视场角。</summary>
    public float YFov { get; init; }

    /// <summary>正交相机的半高（glTF ymag；Assimp 的 mOrthographicWidth 为半宽，这里按宽高比换成半高）。</summary>
    public float OrthoHalfHeight { get; init; }

    public float AspectRatio { get; init; }
    public float Near { get; init; }
    public float Far { get; init; }
}

/// <summary>灯光参数。Color 为线性 RGB（0..1），亮度单独放在 Intensity。</summary>
public sealed class SceneLight
{
    public SceneLightType Type { get; init; }
    public Vector3 Color { get; init; } = Vector3.One;
    public float Intensity { get; init; } = 1f;

    /// <summary>照射范围，0 表示无限。</summary>
    public float Range { get; init; }

    /// <summary>聚光灯内 / 外锥角（弧度，半角）。</summary>
    public float InnerConeAngle { get; init; }
    public float OuterConeAngle { get; init; }
}

/// <summary>场景树里的一个节点。</summary>
public sealed class SceneNode
{
    public required int Index { get; init; }
    public required string Name { get; init; }

    /// <summary>父节点在 MeshData.Nodes 里的下标，-1 表示根（可以有多个根）。</summary>
    public int Parent { get; init; } = -1;

    public SceneNodeKind Kind { get; init; }

    /// <summary>相对父节点的变换（System.Numerics 行向量约定：v * Local * Parent.World）。</summary>
    public Matrix4x4 Local { get; init; } = Matrix4x4.Identity;

    public Matrix4x4 World { get; init; } = Matrix4x4.Identity;

    /// <summary>属于这个节点的 MeshData.Parts 下标（只含这个节点本身，不含子节点）。</summary>
    public int[] PartIndices { get; init; } = [];

    /// <summary>子节点下标，按文件里的顺序。</summary>
    public int[] Children { get; internal set; } = [];

    public SceneCamera? Camera { get; init; }
    public SceneLight? Light { get; init; }

    /// <summary>按 Parent 填好每个节点的 Children。</summary>
    internal static SceneNode[] Link(List<SceneNode> nodes)
    {
        var children = new List<int>[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            children[i] = [];
        }

        foreach (var node in nodes)
        {
            if (node.Parent >= 0)
            {
                children[node.Parent].Add(node.Index);
            }
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            nodes[i].Children = children[i].ToArray();
        }

        return nodes.ToArray();
    }
}

public readonly struct ImportResult
{
    public bool Ok { get; init; }
    public string Error { get; init; }
    public MeshData? Mesh { get; init; }

    public static ImportResult Success(MeshData mesh) => new() { Ok = true, Error = "", Mesh = mesh };

    public static ImportResult Fail(string error) => new() { Ok = false, Error = error, Mesh = null };
}
