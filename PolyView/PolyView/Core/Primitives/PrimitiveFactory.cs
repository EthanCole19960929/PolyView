using System.Numerics;

namespace PolyView.Primitives;

/// <summary>Blender 4.2 default mesh primitives (Blender Z-up coordinates converted to PolyView Y-up). Flat shading, UVs.</summary>
public static class PrimitiveFactory
{
    public static readonly string[] MeshTypes = ["plane", "cube", "circle", "uvsphere", "icosphere", "cylinder", "cone", "torus", "grid"];

    sealed class Builder
    {
        public readonly List<Vertex> V = [];
        public readonly List<uint> I = [];

        static Vector3 Y(Vector3 b) => new(b.X, b.Z, -b.Y);

        /// <summary>Polygon in Blender coords, counter-clockwise seen from outside.</summary>
        public void Face(ReadOnlySpan<Vector3> p, ReadOnlySpan<Vector2> uv)
        {
            var n = Vector3.Zero;
            for (int i = 0; i < p.Length; i++)
            {
                var a = p[i];
                var b = p[(i + 1) % p.Length];
                n += new Vector3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
            }

            n = n.LengthSquared() > 1e-20f ? Vector3.Normalize(Y(n)) : Vector3.UnitY;
            uint start = (uint)V.Count;
            for (int i = 0; i < p.Length; i++)
            {
                V.Add(new Vertex(Y(p[i]), n) { TexCoord = new Vector2(uv[i].X, 1f - uv[i].Y) });
            }

            for (int i = 1; i + 1 < p.Length; i++)
            {
                I.Add(start);
                I.Add(start + (uint)i);
                I.Add(start + (uint)i + 1);
            }
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud) =>
            Face([a, b, c, d], [ua, ub, uc, ud]);

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc) => Face([a, b, c], [ua, ub, uc]);

        public MeshData Build(string name)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var v in V)
            {
                min = Vector3.Min(min, v.Position);
                max = Vector3.Max(max, v.Position);
            }

            var mesh = new MeshData { Name = name, Vertices = [.. V], Indices = [.. I], Min = min, Max = max };
            MeshImporter.EnsureNodes(mesh);
            return mesh;
        }
    }

    static float P(IReadOnlyDictionary<string, float>? p, string key, float fallback) =>
        p != null && p.TryGetValue(key, out var v) && float.IsFinite(v) ? v : fallback;

    /// <summary>type: plane, cube, circle, uvsphere, icosphere, cylinder, cone, torus, grid. Returns null for unknown types.</summary>
    public static MeshData? Create(string type, string name, IReadOnlyDictionary<string, float>? p = null)
    {
        var b = new Builder();
        switch (type.ToLowerInvariant())
        {
            case "plane":
            {
                float s = P(p, "size", 2f) / 2f;
                b.Quad(new(-s, -s, 0), new(s, -s, 0), new(s, s, 0), new(-s, s, 0), new(0, 0), new(1, 0), new(1, 1), new(0, 1));
                break;
            }

            case "cube":
            {
                float s = P(p, "size", 2f) / 2f;
                Vector3 C(int x, int y, int z) => new(x * s, y * s, z * s);
                Vector2 u0 = new(0, 0), u1 = new(1, 0), u2 = new(1, 1), u3 = new(0, 1);
                b.Quad(C(-1, -1, 1), C(1, -1, 1), C(1, 1, 1), C(-1, 1, 1), u0, u1, u2, u3);       // top
                b.Quad(C(-1, 1, -1), C(1, 1, -1), C(1, -1, -1), C(-1, -1, -1), u0, u1, u2, u3);   // bottom
                b.Quad(C(-1, -1, -1), C(1, -1, -1), C(1, -1, 1), C(-1, -1, 1), u0, u1, u2, u3);   // front -Y
                b.Quad(C(1, 1, -1), C(-1, 1, -1), C(-1, 1, 1), C(1, 1, 1), u0, u1, u2, u3);       // back
                b.Quad(C(1, -1, -1), C(1, 1, -1), C(1, 1, 1), C(1, -1, 1), u0, u1, u2, u3);       // +X
                b.Quad(C(-1, 1, -1), C(-1, -1, -1), C(-1, -1, 1), C(-1, 1, 1), u0, u1, u2, u3);   // -X
                break;
            }

            case "circle":
            {
                int n = Math.Max(3, (int)P(p, "vertices", 32));
                float r = P(p, "radius", 1f);
                var pts = new Vector3[n];
                var uvs = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    float t = i * MathF.Tau / n + MathF.PI / 2f;
                    pts[i] = new Vector3(MathF.Cos(t) * r, MathF.Sin(t) * r, 0);
                    uvs[i] = new Vector2(0.5f + MathF.Cos(t) * 0.5f, 0.5f + MathF.Sin(t) * 0.5f);
                }

                b.Face(pts, uvs);
                break;
            }

            case "uvsphere":
            {
                int seg = Math.Max(3, (int)P(p, "segments", 32));
                int rings = Math.Max(3, (int)P(p, "rings", 16));
                float r = P(p, "radius", 1f);
                Vector3 S(int i, int j)
                {
                    float th = j * MathF.PI / rings, ph = i * MathF.Tau / seg;
                    return new Vector3(MathF.Sin(th) * MathF.Cos(ph) * r, MathF.Sin(th) * MathF.Sin(ph) * r, MathF.Cos(th) * r);
                }

                Vector2 U(int i, int j) => new((float)i / seg, 1f - (float)j / rings);
                for (int j = 0; j < rings; j++)
                {
                    for (int i = 0; i < seg; i++)
                    {
                        if (j == 0)
                        {
                            b.Tri(S(i, 0), S(i, 1), S(i + 1, 1), U(i, 0), U(i, 1), U(i + 1, 1));
                        }
                        else if (j == rings - 1)
                        {
                            b.Tri(S(i, j), S(i, j + 1), S(i + 1, j), U(i, j), U(i, j + 1), U(i + 1, j));
                        }
                        else
                        {
                            b.Quad(S(i, j), S(i, j + 1), S(i + 1, j + 1), S(i + 1, j), U(i, j), U(i, j + 1), U(i + 1, j + 1), U(i + 1, j));
                        }
                    }
                }

                break;
            }

            case "icosphere":
                Ico(b, Math.Clamp((int)P(p, "subdivisions", 2), 1, 7), P(p, "radius", 1f));
                break;
            case "cylinder":
            case "cone":
            {
                bool cone = type.Equals("cone", StringComparison.OrdinalIgnoreCase);
                int n = Math.Max(3, (int)P(p, "vertices", 32));
                float r1 = P(p, cone ? "radius1" : "radius", 1f);
                float r2 = cone ? P(p, "radius2", 0f) : r1;
                float h = P(p, "depth", 2f) / 2f;
                Vector3 Ring(int i, float r, float z)
                {
                    float t = i * MathF.Tau / n + MathF.PI / 2f;
                    return new Vector3(MathF.Cos(t) * r, MathF.Sin(t) * r, z);
                }

                for (int i = 0; i < n; i++)
                {
                    Vector2 a = new((float)i / n, 0), c = new((float)(i + 1) / n, 0);
                    if (r2 <= 1e-6f)
                    {
                        b.Tri(Ring(i, r1, -h), Ring(i + 1, r1, -h), new Vector3(0, 0, h), a, c, new((i + 0.5f) / n, 1));
                    }
                    else
                    {
                        b.Quad(Ring(i, r1, -h), Ring(i + 1, r1, -h), Ring(i + 1, r2, h), Ring(i, r2, h), a, c, c with { Y = 1 }, a with { Y = 1 });
                    }
                }

                CapFace(b, n, r1, -h, bottom: true);
                if (r2 > 1e-6f)
                {
                    CapFace(b, n, r2, h, bottom: false);
                }

                break;
            }

            case "torus":
            {
                int maj = Math.Max(3, (int)P(p, "major_segments", 48));
                int min = Math.Max(3, (int)P(p, "minor_segments", 12));
                float R = P(p, "major_radius", 1f), r = P(p, "minor_radius", 0.25f);
                Vector3 T(int i, int j)
                {
                    float u = i * MathF.Tau / maj, v = j * MathF.Tau / min;
                    float d = R + r * MathF.Cos(v);
                    return new Vector3(d * MathF.Cos(u), d * MathF.Sin(u), r * MathF.Sin(v));
                }

                Vector2 U(int i, int j) => new((float)i / maj, (float)j / min);
                for (int i = 0; i < maj; i++)
                {
                    for (int j = 0; j < min; j++)
                    {
                        b.Quad(T(i, j), T(i + 1, j), T(i + 1, j + 1), T(i, j + 1), U(i, j), U(i + 1, j), U(i + 1, j + 1), U(i, j + 1));
                    }
                }

                break;
            }

            case "grid":
            {
                int nx = Math.Max(1, (int)P(p, "x_subdivisions", 10));
                int ny = Math.Max(1, (int)P(p, "y_subdivisions", 10));
                float s = P(p, "size", 2f) / 2f;
                for (int y = 0; y < ny; y++)
                {
                    for (int x = 0; x < nx; x++)
                    {
                        float x0 = -s + 2 * s * x / nx, x1 = -s + 2 * s * (x + 1) / nx;
                        float y0 = -s + 2 * s * y / ny, y1 = -s + 2 * s * (y + 1) / ny;
                        Vector2 a = new((float)x / nx, (float)y / ny), c = new((float)(x + 1) / nx, (float)(y + 1) / ny);
                        b.Quad(new(x0, y0, 0), new(x1, y0, 0), new(x1, y1, 0), new(x0, y1, 0), a, new(c.X, a.Y), c, new(a.X, c.Y));
                    }
                }

                break;
            }

            default:
                return null;
        }

        return b.Build(name);
    }

    /// <summary>Blender Text object (size 1, flat, one face per glyph region, normal +Z).</summary>
    public static MeshData? CreateText(string name, string text)
    {
        var tris = TextMesh.Triangulate(text);
        if (tris.Count < 3)
        {
            return null;
        }

        var b = new Builder();
        for (int i = 0; i + 2 < tris.Count; i += 3)
        {
            Vector2 U(Vector3 q) => new(q.X, q.Y);
            b.Tri(tris[i], tris[i + 1], tris[i + 2], U(tris[i]), U(tris[i + 1]), U(tris[i + 2]));
        }

        return b.Build(name);
    }

    static void CapFace(Builder b, int n, float r, float z, bool bottom)
    {
        var pts = new Vector3[n];
        var uvs = new Vector2[n];
        for (int k = 0; k < n; k++)
        {
            int i = bottom ? n - 1 - k : k;
            float t = i * MathF.Tau / n + MathF.PI / 2f;
            pts[k] = new Vector3(MathF.Cos(t) * r, MathF.Sin(t) * r, z);
            uvs[k] = new Vector2(0.5f + MathF.Cos(t) * 0.5f, 0.5f + MathF.Sin(t) * 0.5f);
        }

        b.Face(pts, uvs);
    }

    static void Ico(Builder b, int subdiv, float radius)
    {
        float t = (1f + MathF.Sqrt(5f)) / 2f;
        var v = new List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        var f = new List<(int, int, int)>
        {
            (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
            (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
        };
        for (int i = 0; i < v.Count; i++)
        {
            v[i] = Vector3.Normalize(v[i]);
        }

        // Blender: subdivisions = 1 is the plain icosahedron (20 faces), 2 = 80 faces.
        for (int s = 1; s < subdiv; s++)
        {
            var cache = new Dictionary<long, int>();
            int Mid(int a, int c)
            {
                long key = a < c ? ((long)a << 32) | (uint)c : ((long)c << 32) | (uint)a;
                if (!cache.TryGetValue(key, out int m))
                {
                    m = v.Count;
                    v.Add(Vector3.Normalize((v[a] + v[c]) * 0.5f));
                    cache[key] = m;
                }

                return m;
            }

            var nf = new List<(int, int, int)>(f.Count * 4);
            foreach (var (a, c, d) in f)
            {
                int ab = Mid(a, c), bc = Mid(c, d), ca = Mid(d, a);
                nf.Add((a, ab, ca));
                nf.Add((c, bc, ab));
                nf.Add((d, ca, bc));
                nf.Add((ab, bc, ca));
            }

            f = nf;
        }

        // Generated in Y-up; convert to Blender coords so Builder maps back (x, y, z) -> (x, -z, y).
        Vector3 Bl(Vector3 p) => new Vector3(p.X, -p.Z, p.Y) * radius;
        Vector2 Uv(Vector3 p) => new(0.5f + MathF.Atan2(p.Z, p.X) / MathF.Tau, 0.5f + MathF.Asin(Math.Clamp(p.Y, -1f, 1f)) / MathF.PI);
        foreach (var (a, c, d) in f)
        {
            b.Tri(Bl(v[a]), Bl(v[c]), Bl(v[d]), Uv(v[a]), Uv(v[c]), Uv(v[d]));
        }
    }
}
