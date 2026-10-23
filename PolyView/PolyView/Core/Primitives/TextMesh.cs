using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Media;

namespace PolyView.Primitives;

/// <summary>Flat text mesh (Blender Text object, size 1, no extrude) from system font glyph outlines, ear-clipped. Blender XY plane.</summary>
public static class TextMesh
{
    /// <summary>Returns polygons (Blender coords, z = 0) triangulated as a triangle list.</summary>
    public static List<Vector3> Triangulate(string text, string fontFamily = "Microsoft YaHei UI")
    {
        var tris = new List<Vector3>();
        if (string.IsNullOrEmpty(text))
        {
            return tris;
        }

        const double em = 100;
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(fontFamily), em, Brushes.White, 1.0);
        var geometry = ft.BuildGeometry(new Point(0, 0)).GetFlattenedPathGeometry(0.5, ToleranceType.Absolute);
        double baseline = ft.Baseline;
        float k = (float)(1.0 / em);
        var contours = new List<List<Vector2>>();
        foreach (var figure in geometry.Figures)
        {
            var pts = new List<Vector2>();
            void Add(Point p)
            {
                var v = new Vector2((float)p.X * k, (float)(baseline - p.Y) * k);
                if (pts.Count == 0 || Vector2.DistanceSquared(pts[^1], v) > 1e-10f)
                {
                    pts.Add(v);
                }
            }

            Add(figure.StartPoint);
            foreach (var seg in figure.Segments)
            {
                switch (seg)
                {
                    case LineSegment l: Add(l.Point); break;
                    case PolyLineSegment pl: foreach (var p in pl.Points) Add(p); break;
                }
            }

            if (pts.Count > 2 && Vector2.DistanceSquared(pts[0], pts[^1]) < 1e-10f)
            {
                pts.RemoveAt(pts.Count - 1);
            }

            if (pts.Count >= 3 && MathF.Abs(Area(pts)) > 1e-8f)
            {
                contours.Add(pts);
            }
        }

        // Even nesting depth = outer, odd = hole (attached to the smallest containing outer).
        int n = contours.Count;
        var depth = new int[n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (i != j && Inside(contours[i][0], contours[j]))
                {
                    depth[i]++;
                }
            }
        }

        for (int i = 0; i < n; i++)
        {
            if (depth[i] % 2 != 0)
            {
                continue;
            }

            var outer = new List<Vector2>(contours[i]);
            if (Area(outer) < 0)
            {
                outer.Reverse();
            }

            var holes = new List<List<Vector2>>();
            for (int j = 0; j < n; j++)
            {
                if (depth[j] == depth[i] + 1 && Inside(contours[j][0], contours[i]))
                {
                    var h = new List<Vector2>(contours[j]);
                    if (Area(h) > 0)
                    {
                        h.Reverse();
                    }

                    holes.Add(h);
                }
            }

            var poly = Bridge(outer, holes);
            EarClip(poly, tris);
        }

        return tris;
    }

    static float Area(List<Vector2> p)
    {
        float a = 0;
        for (int i = 0; i < p.Count; i++)
        {
            var u = p[i];
            var v = p[(i + 1) % p.Count];
            a += u.X * v.Y - v.X * u.Y;
        }

        return a * 0.5f;
    }

    static bool Inside(Vector2 q, List<Vector2> poly)
    {
        bool c = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[i];
            var b = poly[j];
            if ((a.Y > q.Y) != (b.Y > q.Y) && q.X < (b.X - a.X) * (q.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                c = !c;
            }
        }

        return c;
    }

    static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
        return ((d1 > 1e-9f && d2 < -1e-9f) || (d1 < -1e-9f && d2 > 1e-9f)) && ((d3 > 1e-9f && d4 < -1e-9f) || (d3 < -1e-9f && d4 > 1e-9f));
    }

    /// <summary>Merge holes into the outer polygon with zero-width bridges (rightmost hole first).</summary>
    static List<Vector2> Bridge(List<Vector2> outer, List<List<Vector2>> holes)
    {
        var poly = outer;
        holes.Sort((a, b) => b.Max(v => v.X).CompareTo(a.Max(v => v.X)));
        foreach (var hole in holes)
        {
            int hi = 0;
            for (int i = 1; i < hole.Count; i++)
            {
                if (hole[i].X > hole[hi].X)
                {
                    hi = i;
                }
            }

            var m = hole[hi];
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                float d = Vector2.DistanceSquared(poly[i], m);
                if (d >= bestD)
                {
                    continue;
                }

                bool ok = true;
                for (int e = 0; e < poly.Count && ok; e++)
                {
                    ok = !SegmentsCross(m, poly[i], poly[e], poly[(e + 1) % poly.Count]);
                }

                foreach (var other in holes)
                {
                    for (int e = 0; e < other.Count && ok; e++)
                    {
                        ok = !SegmentsCross(m, poly[i], other[e], other[(e + 1) % other.Count]);
                    }
                }

                if (ok)
                {
                    best = i;
                    bestD = d;
                }
            }

            if (best < 0)
            {
                best = 0;
            }

            var merged = new List<Vector2>(poly.Count + hole.Count + 2);
            merged.AddRange(poly.Take(best + 1));
            for (int i = 0; i <= hole.Count; i++)
            {
                merged.Add(hole[(hi + i) % hole.Count]);
            }

            merged.Add(poly[best]);
            merged.AddRange(poly.Skip(best + 1));
            poly = merged;
        }

        return poly;
    }

    static void EarClip(List<Vector2> poly, List<Vector3> tris)
    {
        var idx = Enumerable.Range(0, poly.Count).ToList();
        int guard = 0;
        while (idx.Count > 3 && guard < 100000)
        {
            guard++;
            bool clipped = false;
            for (int i = 0; i < idx.Count; i++)
            {
                var a = poly[idx[(i + idx.Count - 1) % idx.Count]];
                var b = poly[idx[i]];
                var c = poly[idx[(i + 1) % idx.Count]];
                if (Cross(a, b, c) <= 1e-12f)
                {
                    continue;
                }

                bool ear = true;
                for (int j = 0; j < idx.Count && ear; j++)
                {
                    var p = poly[idx[j]];
                    if (p == a || p == b || p == c)
                    {
                        continue;
                    }

                    ear = !(Cross(a, b, p) >= 0 && Cross(b, c, p) >= 0 && Cross(c, a, p) >= 0);
                }

                if (!ear)
                {
                    continue;
                }

                tris.Add(new Vector3(a, 0));
                tris.Add(new Vector3(b, 0));
                tris.Add(new Vector3(c, 0));
                idx.RemoveAt(i);
                clipped = true;
                break;
            }

            if (!clipped)
            {
                // Degenerate leftovers: drop one vertex to make progress.
                idx.RemoveAt(0);
            }
        }

        if (idx.Count == 3)
        {
            tris.Add(new Vector3(poly[idx[0]], 0));
            tris.Add(new Vector3(poly[idx[1]], 0));
            tris.Add(new Vector3(poly[idx[2]], 0));
        }
    }
}
