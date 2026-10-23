using System.Globalization;
using System.Numerics;
using System.Text;

namespace PolyView;

public static class MeshBuilder
{
    public static MeshData CreateFrontTriangle()
    {
        var normal = Vector3.UnitZ;
        var vertices = new[]
        {
            new Vertex(new Vector3(-0.8f, -0.6f, 0f), normal),
            new Vertex(new Vector3(0.8f, -0.6f, 0f), normal),
            new Vertex(new Vector3(0f, 0.8f, 0f), normal),
        };
        return FromVertices("正面三角形", vertices);
    }

    public static MeshData CreateBar()
    {
        var min = new Vector3(-1f, -0.2f, -0.2f);
        var max = new Vector3(1f, 0.2f, 0.2f);
        var corners = new[]
        {
            new Vector3(min.X, min.Y, max.Z),
            new Vector3(max.X, min.Y, max.Z),
            new Vector3(max.X, max.Y, max.Z),
            new Vector3(min.X, max.Y, max.Z),
            new Vector3(max.X, min.Y, min.Z),
            new Vector3(min.X, min.Y, min.Z),
            new Vector3(min.X, max.Y, min.Z),
            new Vector3(max.X, max.Y, min.Z),
            new Vector3(min.X, max.Y, max.Z),
            new Vector3(max.X, max.Y, max.Z),
            new Vector3(max.X, max.Y, min.Z),
            new Vector3(min.X, max.Y, min.Z),
            new Vector3(min.X, min.Y, min.Z),
            new Vector3(max.X, min.Y, min.Z),
            new Vector3(max.X, min.Y, max.Z),
            new Vector3(min.X, min.Y, max.Z),
            new Vector3(max.X, min.Y, max.Z),
            new Vector3(max.X, min.Y, min.Z),
            new Vector3(max.X, max.Y, min.Z),
            new Vector3(max.X, max.Y, max.Z),
            new Vector3(min.X, min.Y, min.Z),
            new Vector3(min.X, min.Y, max.Z),
            new Vector3(min.X, max.Y, max.Z),
            new Vector3(min.X, max.Y, min.Z),
        };

        var vertices = new Vertex[36];
        for (int face = 0; face < 6; face++)
        {
            int n = face * 4;
            var a = corners[n];
            var b = corners[n + 1];
            var c = corners[n + 2];
            var d = corners[n + 3];
            var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            int v = face * 6;
            vertices[v] = new Vertex(a, normal);
            vertices[v + 1] = new Vertex(b, normal);
            vertices[v + 2] = new Vertex(c, normal);
            vertices[v + 3] = new Vertex(a, normal);
            vertices[v + 4] = new Vertex(c, normal);
            vertices[v + 5] = new Vertex(d, normal);
        }

        return FromVertices("长方体", vertices);
    }

    public static MeshData CreateGrid(int cells)
    {
        int quads = cells * cells;
        var vertices = new Vertex[quads * 6];
        int write = 0;
        var normal = Vector3.UnitY;
        for (int z = 0; z < cells; z++)
        {
            for (int x = 0; x < cells; x++)
            {
                float x0 = x - cells * 0.5f;
                float z0 = z - cells * 0.5f;
                var a = new Vector3(x0, 0f, z0);
                var b = new Vector3(x0 + 1f, 0f, z0);
                var c = new Vector3(x0 + 1f, 0f, z0 + 1f);
                var d = new Vector3(x0, 0f, z0 + 1f);
                vertices[write++] = new Vertex(a, normal);
                vertices[write++] = new Vertex(c, normal);
                vertices[write++] = new Vertex(b, normal);
                vertices[write++] = new Vertex(a, normal);
                vertices[write++] = new Vertex(d, normal);
                vertices[write++] = new Vertex(c, normal);
            }
        }

        return FromVertices("网格", vertices);
    }

    public static void WriteObj(MeshData mesh, string path)
    {
        var text = new StringBuilder();
        text.Append("o ").AppendLine(mesh.Name);
        foreach (var vertex in mesh.Vertices)
        {
            var p = vertex.Position;
            text.Append("v ")
                .Append(p.X.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(p.Y.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .AppendLine(p.Z.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var vertex in mesh.Vertices)
        {
            var n = vertex.Normal;
            text.Append("vn ")
                .Append(n.X.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(n.Y.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .AppendLine(n.Z.ToString(CultureInfo.InvariantCulture));
        }

        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            int a = (int)mesh.Indices[i] + 1;
            int b = (int)mesh.Indices[i + 1] + 1;
            int c = (int)mesh.Indices[i + 2] + 1;
            text.Append("f ")
                .Append(a).Append('/').Append('/').Append(a).Append(' ')
                .Append(b).Append('/').Append('/').Append(b).Append(' ')
                .Append(c).Append('/').Append('/').Append(c).AppendLine();
        }

        File.WriteAllText(path, text.ToString());
    }

    public static void WriteBinaryStl(MeshData mesh, string path)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[80]);
        writer.Write((uint)mesh.TriangleCount);
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            var a = mesh.Vertices[mesh.Indices[i]];
            var b = mesh.Vertices[mesh.Indices[i + 1]];
            var c = mesh.Vertices[mesh.Indices[i + 2]];
            WriteVector(writer, a.Normal);
            WriteVector(writer, a.Position);
            WriteVector(writer, b.Position);
            WriteVector(writer, c.Position);
            writer.Write((ushort)0);
        }
    }

    private static MeshData FromVertices(string name, Vertex[] vertices)
    {
        var indices = new uint[vertices.Length];
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (int i = 0; i < vertices.Length; i++)
        {
            indices[i] = (uint)i;
            var p = vertices[i].Position;
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        return new MeshData
        {
            Name = name,
            Vertices = vertices,
            Indices = indices,
            Min = min,
            Max = max,
        };
    }

    private static void WriteVector(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }
}
