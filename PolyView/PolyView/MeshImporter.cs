using System.Globalization;
using System.Numerics;

namespace PolyView;

public static class MeshImporter
{
    public static ImportResult Load(string path)
    {
        if (!File.Exists(path))
        {
            return ImportResult.Fail("找不到文件。");
        }

        try
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            MeshData mesh = extension switch
            {
                ".obj" => LoadObj(path),
                ".stl" => LoadStl(path),
                _ => throw new InvalidDataException("只支持 OBJ 和 STL。"),
            };
            return ImportResult.Success(mesh);
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or IOException or ArgumentException)
        {
            return ImportResult.Fail(ex.Message);
        }
    }

    private static MeshData LoadObj(string path)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var vertices = new List<Vertex>();
        var indices = new List<uint>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var parts = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
            if (parts[0] == "v" && parts.Length >= 4)
            {
                positions.Add(ParseVector(parts));
            }
            else if (parts[0] == "vn" && parts.Length >= 4)
            {
                normals.Add(ParseVector(parts));
            }
            else if (parts[0] == "f" && parts.Length >= 4)
            {
                AddFace(parts, positions, normals, vertices, indices);
            }
        }

        return Finish(Path.GetFileNameWithoutExtension(path), vertices, indices);
    }

    private static MeshData LoadStl(string path)
    {
        using var stream = File.OpenRead(path);
        if (IsAsciiStl(stream))
        {
            stream.Position = 0;
            return LoadAsciiStl(path, stream);
        }

        stream.Position = 0;
        return LoadBinaryStl(path, stream);
    }

    private static bool IsAsciiStl(Stream stream)
    {
        if (stream.Length < 84)
        {
            return true;
        }

        using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        reader.ReadBytes(80);
        uint count = reader.ReadUInt32();
        long binaryLength = 84L + count * 50L;
        return binaryLength != stream.Length;
    }

    private static MeshData LoadBinaryStl(string path, Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        reader.ReadBytes(80);
        uint count = reader.ReadUInt32();
        if (84L + count * 50L != stream.Length)
        {
            throw new InvalidDataException("STL 长度和三角形数量对不上。");
        }

        var vertices = new List<Vertex>((int)count * 3);
        var indices = new List<uint>((int)count * 3);
        for (uint i = 0; i < count; i++)
        {
            var normal = ReadVector(reader);
            var a = ReadVector(reader);
            var b = ReadVector(reader);
            var c = ReadVector(reader);
            reader.ReadUInt16();
            AddTriangle(vertices, indices, a, b, c, normal);
        }

        return Finish(Path.GetFileNameWithoutExtension(path), vertices, indices);
    }

    private static MeshData LoadAsciiStl(string path, Stream stream)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        var vertices = new List<Vertex>();
        var indices = new List<uint>();
        Vector3 normal = Vector3.UnitY;
        var corners = new List<Vector3>(3);
        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Trim();
            if (line.StartsWith("facet normal", StringComparison.Ordinal))
            {
                var parts = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5)
                {
                    normal = ParseVector(parts[2], parts[3], parts[4]);
                }
            }
            else if (line.StartsWith("vertex", StringComparison.Ordinal))
            {
                var parts = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4)
                {
                    corners.Add(ParseVector(parts[1], parts[2], parts[3]));
                }

                if (corners.Count == 3)
                {
                    AddTriangle(vertices, indices, corners[0], corners[1], corners[2], normal);
                    corners.Clear();
                }
            }
        }

        return Finish(Path.GetFileNameWithoutExtension(path), vertices, indices);
    }

    private static void AddFace(
        string[] parts,
        List<Vector3> positions,
        List<Vector3> normals,
        List<Vertex> vertices,
        List<uint> indices)
    {
        var corners = new (Vector3 Position, Vector3 Normal, bool HasNormal)[parts.Length - 1];
        for (int i = 1; i < parts.Length; i++)
        {
            var tokens = parts[i].Split('/');
            int positionIndex = Resolve(int.Parse(tokens[0], CultureInfo.InvariantCulture), positions.Count);
            var position = positions[positionIndex];
            bool hasNormal = false;
            var normal = Vector3.Zero;
            if (tokens.Length >= 3 && tokens[2].Length > 0)
            {
                int normalIndex = Resolve(int.Parse(tokens[2], CultureInfo.InvariantCulture), normals.Count);
                normal = normals[normalIndex];
                hasNormal = true;
            }

            corners[i - 1] = (position, normal, hasNormal);
        }

        for (int i = 1; i < corners.Length - 1; i++)
        {
            var a = corners[0];
            var b = corners[i];
            var c = corners[i + 1];
            var face = Vector3.Normalize(Vector3.Cross(b.Position - a.Position, c.Position - a.Position));
            if (face.LengthSquared() < 1e-12f)
            {
                continue;
            }

            AddCorner(vertices, indices, a, face);
            AddCorner(vertices, indices, b, face);
            AddCorner(vertices, indices, c, face);
        }
    }

    private static void AddCorner(List<Vertex> vertices, List<uint> indices, (Vector3 Position, Vector3 Normal, bool HasNormal) corner, Vector3 faceNormal)
    {
        var normal = corner.HasNormal && corner.Normal.LengthSquared() > 1e-12f
            ? Vector3.Normalize(corner.Normal)
            : faceNormal;
        indices.Add((uint)vertices.Count);
        vertices.Add(new Vertex(corner.Position, normal));
    }

    private static void AddTriangle(List<Vertex> vertices, List<uint> indices, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
    {
        if (normal.LengthSquared() < 1e-12f)
        {
            normal = Vector3.Cross(b - a, c - a);
        }

        if (normal.LengthSquared() < 1e-12f)
        {
            return;
        }

        normal = Vector3.Normalize(normal);
        indices.Add((uint)vertices.Count);
        vertices.Add(new Vertex(a, normal));
        indices.Add((uint)vertices.Count);
        vertices.Add(new Vertex(b, normal));
        indices.Add((uint)vertices.Count);
        vertices.Add(new Vertex(c, normal));
    }

    private static MeshData Finish(string name, List<Vertex> vertices, List<uint> indices)
    {
        if (indices.Count < 3)
        {
            throw new InvalidDataException("文件里没有可显示的三角形。");
        }

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var vertex in vertices)
        {
            min = Vector3.Min(min, vertex.Position);
            max = Vector3.Max(max, vertex.Position);
        }

        return new MeshData
        {
            Name = name,
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
            Min = min,
            Max = max,
        };
    }

    private static int Resolve(int index, int count)
    {
        if (index > 0)
        {
            return index - 1;
        }

        if (index < 0)
        {
            return count + index;
        }

        throw new InvalidDataException("OBJ 的索引不能是 0。");
    }

    private static Vector3 ParseVector(string[] parts) => ParseVector(parts[1], parts[2], parts[3]);

    private static Vector3 ParseVector(string x, string y, string z)
    {
        return new Vector3(
            float.Parse(x, NumberStyles.Float, CultureInfo.InvariantCulture),
            float.Parse(y, NumberStyles.Float, CultureInfo.InvariantCulture),
            float.Parse(z, NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    private static Vector3 ReadVector(BinaryReader reader)
    {
        return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }
}
