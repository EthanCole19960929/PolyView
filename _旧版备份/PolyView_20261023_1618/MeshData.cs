using System.Numerics;

namespace PolyView;

public struct Vertex
{
    public Vector3 Position;
    public Vector3 Normal;

    public Vertex(Vector3 position, Vector3 normal)
    {
        Position = position;
        Normal = normal;
    }
}

public sealed class MeshData
{
    public required string Name { get; init; }
    public required Vertex[] Vertices { get; init; }
    public required uint[] Indices { get; init; }
    public Vector3 Min { get; init; }
    public Vector3 Max { get; init; }

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
