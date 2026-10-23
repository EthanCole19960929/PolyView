using System.Numerics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PolyView.Primitives;

namespace PolyView;

/// <summary>What a generated object was made from; saved in .pvproj (ProjectObject.Kind = "primitive", Builtin = JSON).</summary>
public sealed class PrimitiveSpec
{
    public string Type { get; set; } = "cube";
    public Dictionary<string, float>? Params { get; set; }
    public string? Text { get; set; }
}

/// <summary>
/// Blender 4.2 Add menu (Shift+A): mesh primitives, empties, camera, curves, text, point cloud.
/// Line-only objects (empties, camera, curves, point cloud) are drawn with the light-icon overlay and picked by their lines.
/// </summary>
public sealed partial class ModelViewport
{
    readonly Dictionary<int, PrimitiveSpec> _primitiveSpecs = [];
    readonly Dictionary<int, Vector3[]> _helperLines = []; // object-local line pairs

    /// <summary>Kinds accepted by AddPrimitive (AI / MCP callable).</summary>
    public static readonly string[] PrimitiveKinds =
    [
        "plane", "cube", "circle", "uvsphere", "icosphere", "cylinder", "cone", "torus", "grid", "text", "pointcloud", "camera",
        "curve:bezier", "curve:circle", "curve:nurbs", "curve:nurbscircle", "curve:path",
        "empty:plain_axes", "empty:arrows", "empty:single_arrow", "empty:circle", "empty:cube", "empty:sphere", "empty:cone", "empty:image",
    ];

    /// <summary>
    /// Add a Blender-default object (AI / MCP API). type: see PrimitiveKinds. parameters: e.g. size, radius, vertices, segments,
    /// rings, subdivisions, depth, radius1, radius2, major_radius, minor_radius, major_segments, minor_segments, x_subdivisions.
    /// Returns the new object id, or -1 for an unknown type.
    /// </summary>
    public int AddPrimitive(string type, IReadOnlyDictionary<string, float>? parameters = null, Vector3? location = null, string? text = null, string? name = null)
    {
        var spec = new PrimitiveSpec
        {
            Type = type.ToLowerInvariant(),
            Params = parameters == null ? null : new Dictionary<string, float>(parameters),
            Text = text,
        };
        var obj = CreatePrimitiveObject(spec, UniqueName(name ?? UiText.PrimitiveObjectName(spec.Type)));
        if (obj == null)
        {
            return -1;
        }

        obj.Transform.Position = location ?? Vector3.Zero;
        if (spec.Type == "camera")
        {
            // Blender "Align: World": the camera looks straight down its local -Z.
            obj.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f);
        }

        _renderer?.SetWorld(obj.Id, obj.World);
        PushUndo(AddEntryId(obj.Id), obj.Transform, obj.Transform);
        UpdateSceneBounds();
        Select(obj.Id);
        Invalidate();
        SceneChanged?.Invoke(this, EventArgs.Empty);
        ViewChanged?.Invoke(this, EventArgs.Empty);
        return obj.Id;
    }

    /// <summary>The spec of a generated object (null for imported models and lights).</summary>
    public PrimitiveSpec? GetPrimitiveSpec(int id) => _primitiveSpecs.TryGetValue(id, out var s) ? s : null;

    internal static string SerializeSpec(PrimitiveSpec spec) => JsonSerializer.Serialize(spec);

    internal static PrimitiveSpec? DeserializeSpec(string? json)
    {
        try
        {
            return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<PrimitiveSpec>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    string UniqueName(string baseName)
    {
        var names = new HashSet<string>(_objects.Select(o => o.Name));
        if (!names.Contains(baseName))
        {
            return baseName;
        }

        for (int i = 1; ; i++)
        {
            string n = $"{baseName}.{i:000}";
            if (!names.Contains(n))
            {
                return n;
            }
        }
    }

    /// <summary>Builds the object and adds it to the scene (no undo / selection). Used by AddPrimitive and project restore.</summary>
    internal SceneObject? CreatePrimitiveObject(PrimitiveSpec spec, string name)
    {
        MeshData? mesh;
        Vector3[]? lines = null;
        if (spec.Type == "text")
        {
            mesh = PrimitiveFactory.CreateText(name, string.IsNullOrEmpty(spec.Text) ? "Text" : spec.Text);
        }
        else if (Array.IndexOf(PrimitiveFactory.MeshTypes, spec.Type) >= 0)
        {
            mesh = PrimitiveFactory.Create(spec.Type, name, spec.Params);
        }
        else
        {
            lines = HelperGeometry(spec.Type);
            if (lines == null)
            {
                return null;
            }

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var p in lines)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            mesh = new MeshData { Name = name, Vertices = [], Indices = [], Min = min, Max = max };
            var node = spec.Type == "camera"
                ? new SceneNode { Index = 0, Name = name, Kind = SceneNodeKind.Camera, Camera = new SceneCamera { YFov = 2f * MathF.Atan(18f / 50f * 9f / 16f), AspectRatio = 16f / 9f, Near = 0.1f, Far = 100f } }
                : new SceneNode { Index = 0, Name = name, Kind = SceneNodeKind.Empty };
            mesh.Nodes = SceneNode.Link([node]);
        }

        if (mesh == null)
        {
            return null;
        }

        var obj = new SceneObject { Id = _nextId++, Name = name, Mesh = mesh };
        _objects.Add(obj);
        _primitiveSpecs[obj.Id] = spec;
        if (lines != null)
        {
            _helperLines[obj.Id] = lines;
        }
        else
        {
            _renderer?.AddMesh(obj.Id, mesh);
            _renderer?.SetWorld(obj.Id, obj.World);
        }

        return obj;
    }

    // ---------------- line objects ----------------

    static Vector3 Bl(float x, float y, float z) => new(x, z, -y); // Blender Z-up -> PolyView Y-up

    static void Circle(List<Vector3> l, Func<float, float, Vector3> at, int seg = 32)
    {
        for (int i = 0; i < seg; i++)
        {
            float a = i * MathF.Tau / seg, b = (i + 1) * MathF.Tau / seg;
            l.Add(at(MathF.Cos(a), MathF.Sin(a)));
            l.Add(at(MathF.Cos(b), MathF.Sin(b)));
        }
    }

    static void Polyline(List<Vector3> l, IReadOnlyList<Vector3> p, bool closed)
    {
        for (int i = 0; i + 1 < p.Count; i++)
        {
            l.Add(p[i]);
            l.Add(p[i + 1]);
        }

        if (closed && p.Count > 2)
        {
            l.Add(p[^1]);
            l.Add(p[0]);
        }
    }

    static void Arrow(List<Vector3> l, Vector3 dir, Vector3 side1, Vector3 side2, float len)
    {
        var tip = dir * len;
        l.Add(Vector3.Zero);
        l.Add(tip);
        foreach (var s in new[] { side1, -side1, side2, -side2 })
        {
            l.Add(tip);
            l.Add(tip - dir * 0.15f * len + s * 0.05f * len);
        }
    }

    /// <summary>Object-local line pairs (PolyView coords) for empties, camera, curves and point cloud; null for unknown kinds.</summary>
    static Vector3[]? HelperGeometry(string kind)
    {
        var l = new List<Vector3>();
        Vector3 X = Bl(1, 0, 0), Yb = Bl(0, 1, 0), Zb = Bl(0, 0, 1);
        switch (kind)
        {
            case "empty:plain_axes":
                l.AddRange([-X, X, -Yb, Yb, -Zb, Zb]);
                break;
            case "empty:arrows":
                Arrow(l, X, Yb, Zb, 1f);
                Arrow(l, Yb, X, Zb, 1f);
                Arrow(l, Zb, X, Yb, 1f);
                break;
            case "empty:single_arrow":
                Arrow(l, Zb, X, Yb, 1f);
                break;
            case "empty:circle":
                Circle(l, (c, s) => Bl(c, s, 0)); // Blender draws the circle empty in its local XY plane
                break;
            case "empty:cube":
            {
                var c = new[] { Bl(-1, -1, -1), Bl(1, -1, -1), Bl(1, 1, -1), Bl(-1, 1, -1), Bl(-1, -1, 1), Bl(1, -1, 1), Bl(1, 1, 1), Bl(-1, 1, 1) };
                int[] e = [0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7];
                foreach (int i in e) l.Add(c[i]);
                break;
            }

            case "empty:sphere":
                Circle(l, (c, s) => Bl(c, s, 0));
                Circle(l, (c, s) => Bl(c, 0, s));
                Circle(l, (c, s) => Bl(0, c, s));
                break;
            case "empty:cone":
            {
                // Blender: base circle (r 1) at the origin, tip 2 along local +Y.
                Circle(l, (c, s) => Bl(c, 0, s), 16);
                for (int i = 0; i < 4; i++)
                {
                    float a = i * MathF.PI / 2f;
                    l.Add(Bl(MathF.Cos(a), 0, MathF.Sin(a)));
                    l.Add(Bl(0, 2, 0));
                }

                break;
            }

            case "empty:image":
                Polyline(l, [Bl(-0.5f, -0.5f, 0), Bl(0.5f, -0.5f, 0), Bl(0.5f, 0.5f, 0), Bl(-0.5f, 0.5f, 0)], closed: true);
                break;
            case "camera":
            {
                // Blender default camera: 50 mm lens, 36 mm sensor, 16:9, display size 1. Looks down local -Z, up = local +Y.
                float d = 1f, w = 0.5f * 36f / 50f * 2f * d * 0.5f, h = w * 9f / 16f;
                w = 0.5f; h = w * 9f / 16f; d = 0.5f / (18f / 50f) * 0.5f;
                var r = new[] { new Vector3(-w, -h, -d), new Vector3(w, -h, -d), new Vector3(w, h, -d), new Vector3(-w, h, -d) };
                Polyline(l, r, closed: true);
                foreach (var p in r)
                {
                    l.Add(Vector3.Zero);
                    l.Add(p);
                }

                // Up triangle above the frame.
                Polyline(l, [new Vector3(-w * 0.7f, h * 1.1f, -d), new Vector3(w * 0.7f, h * 1.1f, -d), new Vector3(0, h * 1.1f + w * 0.6f, -d)], closed: true);
                break;
            }

            case "curve:bezier":
            {
                // Blender default Bezier: points (-1,0,0) and (1,0,0), handles (-1.5,-0.5)(-0.5,0.5) and (0,0)(2,0).
                Vector3 p0 = Bl(-1, 0, 0), h0 = Bl(-0.5f, 0.5f, 0), h1 = Bl(0, 0, 0), p1 = Bl(1, 0, 0);
                var pts = new List<Vector3>();
                for (int i = 0; i <= 24; i++)
                {
                    float t = i / 24f, u = 1 - t;
                    pts.Add(u * u * u * p0 + 3 * u * u * t * h0 + 3 * u * t * t * h1 + t * t * t * p1);
                }

                Polyline(l, pts, closed: false);
                break;
            }

            case "curve:circle":
            case "curve:nurbscircle":
                Circle(l, (c, s) => Bl(c, s, 0));
                break;
            case "curve:nurbs":
            {
                // Control points (-2,0)(-1,1)(1,1)(2,0): drawn as a smooth B-spline-like polyline.
                Vector3 a = Bl(-1.5f, 0, 0), b = Bl(-0.5f, 1, 0), c = Bl(0.5f, 1, 0), e = Bl(1.5f, 0, 0);
                var pts = new List<Vector3>();
                for (int i = 0; i <= 24; i++)
                {
                    float t = i / 24f, u = 1 - t;
                    pts.Add(u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * e);
                }

                Polyline(l, pts, closed: false);
                break;
            }

            case "curve:path":
                Polyline(l, [Bl(-2, 0, 0), Bl(-1, 0, 0), Bl(0, 0, 0), Bl(1, 0, 0), Bl(2, 0, 0)], closed: false);
                break;
            case "pointcloud":
            {
                var rng = new Random(1);
                for (int i = 0; i < 64; i++)
                {
                    var p = new Vector3(rng.NextSingle() * 2 - 1, rng.NextSingle() * 2 - 1, rng.NextSingle() * 2 - 1);
                    const float s = 0.03f;
                    l.AddRange([p - Vector3.UnitX * s, p + Vector3.UnitX * s, p - Vector3.UnitY * s, p + Vector3.UnitY * s, p - Vector3.UnitZ * s, p + Vector3.UnitZ * s]);
                }

                break;
            }

            default:
                return null;
        }

        return [.. l];
    }

    /// <summary>Called from UpdateLights each frame: overlay lines of the line-only objects.</summary>
    void AddHelperOverlays()
    {
        foreach (var o in _objects)
        {
            if (!o.Visible || !_helperLines.TryGetValue(o.Id, out var lines) || o.HiddenNodes.Contains(0))
            {
                continue;
            }

            var m = o.World * Matrix4x4.CreateScale(_scale);
            uint color = o.Id == _selectedId ? IconSelectedColor : IconColor;
            for (int i = 0; i + 1 < lines.Length; i += 2)
            {
                AddLine(Vector3.Transform(lines[i], m), Vector3.Transform(lines[i + 1], m), color);
            }
        }
    }

    /// <summary>Pick a line-only object within 6 DIP of one of its lines.</summary>
    bool TryPickHelper(Point p, out int objectId, out int nodeIndex)
    {
        objectId = -1;
        nodeIndex = -1;
        float best = 6f;
        var mouse = new Vector2((float)p.X, (float)p.Y);
        foreach (var o in _objects)
        {
            if (!o.Visible || !_helperLines.TryGetValue(o.Id, out var lines))
            {
                continue;
            }

            var m = o.World * Matrix4x4.CreateScale(_scale);
            for (int i = 0; i + 1 < lines.Length; i += 2)
            {
                var a = Project(Vector3.Transform(lines[i], m));
                var b = Project(Vector3.Transform(lines[i + 1], m));
                if (a == null || b == null)
                {
                    continue;
                }

                var ab = b.Value - a.Value;
                float t = ab.LengthSquared() < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(mouse - a.Value, ab) / ab.LengthSquared(), 0f, 1f);
                float d = Vector2.Distance(mouse, a.Value + ab * t);
                if (d < best)
                {
                    best = d;
                    objectId = o.Id;
                }
            }
        }

        return objectId >= 0;
    }

    /// <summary>Copy a generated object in place (Blender Shift+D without the move). Imported models are not duplicated yet.</summary>
    public int DuplicateSelected()
    {
        if (!TryGetObject(_selectedId, out var src) || !_primitiveSpecs.TryGetValue(src.Id, out var spec))
        {
            return -1;
        }

        var copy = CreatePrimitiveObject(spec, UniqueName(BaseName(src.Name)));
        if (copy == null)
        {
            return -1;
        }

        copy.Transform = src.Transform;
        _renderer?.SetWorld(copy.Id, copy.World);
        PushUndo(AddEntryId(copy.Id), copy.Transform, copy.Transform);
        UpdateSceneBounds();
        Select(copy.Id);
        Invalidate();
        SceneChanged?.Invoke(this, EventArgs.Empty);
        return copy.Id;
    }

    static string BaseName(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot > 0 && name.Length - dot == 4 && name[(dot + 1)..].All(char.IsDigit) ? name[..dot] : name;
    }

    // ---------------- Add menu ----------------

    static readonly SolidColorBrush MenuIconBrush = new(Color.FromRgb(0xE6, 0xE6, 0xE6));
    static readonly SolidColorBrush MenuGreyBrush = new(Color.FromRgb(0x80, 0x80, 0x80));

    static string C(double cx, double cy, double r) =>
        FormattableString.Invariant($"M{cx - r},{cy} A{r},{r} 0 1 1 {cx + r},{cy} A{r},{r} 0 1 1 {cx - r},{cy} ");

    static string IconData(string key) => key switch
    {
        "search" => C(6, 6, 4) + "M9,9 L13,13",
        "mesh" or "cube" => "M2,4.5 L7,2 L12,4.5 L12,9.5 L7,12 L2,9.5 Z M2,4.5 L7,7 L12,4.5 M7,7 V12",
        "plane" => "M1,10 L4.5,4 H13 L9.5,10 Z",
        "circle" => C(7, 7, 5),
        "uvsphere" => C(7, 7, 5) + "M2,7 H12 M7,2 C4,4 4,10 7,12 M7,2 C10,4 10,10 7,12",
        "icosphere" => "M7,1.5 L12,4.5 L12,9.5 L7,12.5 L2,9.5 L2,4.5 Z M2,4.5 H12 L7,12.5 Z",
        "cylinder" => "M2,3.5 A5,1.5 0 1 0 12,3.5 A5,1.5 0 1 0 2,3.5 M2,3.5 V10.5 A5,1.5 0 0 0 12,10.5 V3.5",
        "cone" => "M7,1.5 L2,10.5 M7,1.5 L12,10.5 M2,10.5 A5,1.5 0 0 0 12,10.5 A5,1.5 0 0 0 2,10.5",
        "torus" => "M1.5,7 A5.5,3.2 0 1 0 12.5,7 A5.5,3.2 0 1 0 1.5,7 M4.5,7 A2.5,1 0 1 0 9.5,7 A2.5,1 0 1 0 4.5,7",
        "grid" => "M2,2 H12 V12 H2 Z M5.3,2 V12 M8.7,2 V12 M2,5.3 H12 M2,8.7 H12",
        "monkey" => C(7, 7.5, 4.5) + C(2.2, 5, 1.4) + C(11.8, 5, 1.4) + C(5.3, 7, 0.6) + C(8.7, 7, 0.6),
        "curve" => "M1.5,11.5 C4,1 10,13 12.5,2.5",
        "surface" => "M1.5,10 C4,6 6,12 9,8 L12.5,4 C10,6 8,1 5,5 Z",
        "metaball" => C(5, 7, 3.2) + C(10.5, 7, 2),
        "text" => "M2.5,2.5 H11.5 M7,2.5 V12 M2.5,2.5 V4 M11.5,2.5 V4",
        "pointcloud" => C(3, 4, 0.8) + C(8, 3, 0.8) + C(11, 7, 0.8) + C(5, 9, 0.8) + C(9, 11, 0.8) + C(3, 12, 0.8),
        "volume" => "M3,11 A2.5,2.5 0 0 1 3.5,6 A3.5,3.5 0 0 1 10.5,6 A2.5,2.5 0 0 1 11,11 Z",
        "greasepencil" => "M2,12 L3,9 L10,2 L12,4 L5,11 Z M9,3 L11,5",
        "armature" => "M2.5,11.5 L4.5,6 L11.5,2.5 L8,9.5 Z M2.5,11.5 L11.5,2.5",
        "lattice" => "M7,1.5 L12.5,7 L7,12.5 L1.5,7 Z M4.25,4.25 L9.75,9.75 M9.75,4.25 L4.25,9.75",
        "empty" or "empty:plain_axes" => "M7,1.5 V12.5 M1.5,7 H12.5 M3,11 L11,3",
        "empty:arrows" => "M2,12 V2 M2,12 H12 M2,12 L9,5 M0.5,3.5 L2,2 L3.5,3.5 M10.5,10.5 L12,12 L10.5,13.5",
        "empty:single_arrow" => "M7,13 V1.5 M4.5,4 L7,1.5 L9.5,4",
        "empty:circle" => C(7, 7, 5),
        "empty:cube" => "M2,2 H12 V12 H2 Z",
        "empty:sphere" => C(7, 7, 5) + "M2,7 A5,2 0 0 0 12,7",
        "empty:cone" => "M7,1.5 L2,11 M7,1.5 L12,11 M2,11 A5,1.5 0 0 0 12,11 A5,1.5 0 0 0 2,11",
        "image" or "empty:image" => "M2,3 H12 V11 H2 Z M2,9.5 L5,6.5 L8,9.5 L10,7.5 L12,9.5",
        "light" => C(7, 6, 3.5) + "M5.5,10 H8.5 M5.8,12 H8.2",
        "probe" => C(7, 7, 5) + C(7, 7, 1.5),
        "camera" => "M1.5,4 H9 V10.5 H1.5 Z M9,6 L12.5,4 V10.5 L9,8.5",
        "speaker" => "M2,5 H4.5 L8.5,2 V12 L4.5,9 H2 Z M10.5,5 A3,3 0 0 1 10.5,9",
        "force" => C(7, 7, 5.5) + C(7, 7, 3) + C(7, 7, 0.8),
        "collection" => "M1.5,4 H5.5 L6.5,5 H12.5 V11.5 H1.5 Z",
        "curve:bezier" => "M1.5,11.5 C4,1 10,13 12.5,2.5",
        "curve:circle" or "curve:nurbscircle" => C(7, 7, 5),
        "curve:nurbs" => "M1.5,11 C3,2 11,2 12.5,11",
        "curve:path" => "M1.5,7 H12.5 M1.5,5.5 V8.5 M12.5,5.5 V8.5",
        _ => "",
    };

    static UIElement LineIcon(string key)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(IconData(key) is { Length: > 0 } d ? d : "M2,2 H12 V12 H2 Z"),
            Stroke = MenuIconBrush,
            StrokeThickness = 1,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stretch = Stretch.None,
            SnapsToDevicePixels = true,
        };
        return new Canvas { Width = 14, Height = 14, Children = { path } };
    }

    MenuItem Leaf(string key, string header, Action? click)
    {
        var item = new MenuItem { Header = header, Icon = LineIcon(key), IsEnabled = click != null, Tag = key };
        if (click != null)
        {
            item.Click += (_, _) => click();
        }

        return item;
    }

    /// <summary>A submenu; when none of its children work it is drawn grey but still opens so the items can be seen.</summary>
    MenuItem Sub(string key, string header, params MenuItem[] children)
    {
        var item = new MenuItem { Header = header, Icon = LineIcon(key) };
        foreach (var c in children)
        {
            item.Items.Add(c);
        }

        if (children.All(c => !c.IsEnabled))
        {
            item.Foreground = MenuGreyBrush;
            if (item.Icon is Canvas { Children: [System.Windows.Shapes.Path p] })
            {
                p.Opacity = 0.5;
            }
        }

        return item;
    }

    MenuItem Off(string key, string header) => Leaf(key, header, null);

    MenuItem Prim(string kind, string? icon = null) => Leaf(icon ?? kind, UiText.PrimitiveName(kind), () => AddPrimitive(kind));

    /// <summary>Full Blender 4.2 Add menu (shared by Shift+A and the viewport header Add menu).</summary>
    void PopulateBlenderAddMenu(ItemCollection items)
    {
        items.Add(new MenuItem { Header = UiText.AddTitle, IsEnabled = false, InputGestureText = "Shift+A", Tag = "header" });
        items.Add(new Separator());
        var search = new MenuItem { Header = UiText.AddSearch, Icon = LineIcon("search") };
        items.Add(search);
        int searchIndex = items.Count - 1;

        items.Add(Sub("mesh", UiText.AddMesh,
            Prim("plane"), Prim("cube"), Prim("circle"), Prim("uvsphere"), Prim("icosphere"), Prim("cylinder"), Prim("cone"), Prim("torus"),
            Prim("grid"), Off("monkey", UiText.PrimitiveName("monkey"))));
        items.Add(Sub("curve", UiText.AddCurve,
            Prim("curve:bezier"), Prim("curve:circle"), Prim("curve:nurbs"), Prim("curve:nurbscircle"), Prim("curve:path")));
        items.Add(Sub("surface", UiText.AddSurface,
            Off("surface", "NURBS 曲线"), Off("surface", "NURBS 圆环"), Off("surface", "NURBS 表面"), Off("surface", "NURBS 柱面"),
            Off("surface", "NURBS 球面"), Off("surface", "NURBS 环面")));
        items.Add(Sub("metaball", UiText.AddMetaball,
            Off("metaball", UiText.Chinese ? "球" : "Ball"), Off("metaball", UiText.Chinese ? "胶囊" : "Capsule"), Off("metaball", UiText.Chinese ? "平面" : "Plane"),
            Off("metaball", UiText.Chinese ? "椭球" : "Ellipsoid"), Off("metaball", UiText.Chinese ? "立方体" : "Cube")));
        items.Add(Leaf("text", UiText.AddText, () => AddPrimitive("text", text: "Text")));
        items.Add(Leaf("pointcloud", UiText.AddPointCloud, () => AddPrimitive("pointcloud")));
        items.Add(Sub("volume", UiText.AddVolume, Off("volume", UiText.Chinese ? "导入 OpenVDB…" : "Import OpenVDB..."), Off("volume", UiText.Chinese ? "空" : "Empty")));
        items.Add(Sub("greasepencil", UiText.AddGreasePencil,
            Off("greasepencil", UiText.Chinese ? "空白" : "Blank"), Off("greasepencil", UiText.Chinese ? "笔画" : "Stroke"),
            Off("monkey", UiText.Chinese ? "猴头" : "Monkey"), Off("greasepencil", UiText.Chinese ? "场景线条画" : "Scene Line Art")));
        items.Add(new Separator());
        items.Add(Sub("armature", UiText.AddArmature, Off("armature", UiText.Chinese ? "单块骨骼" : "Single Bone")));
        items.Add(Off("lattice", UiText.AddLattice));
        items.Add(new Separator());
        items.Add(Sub("empty", UiText.AddEmpty,
            Prim("empty:plain_axes"), Prim("empty:arrows"), Prim("empty:single_arrow"), Prim("empty:circle"), Prim("empty:cube"),
            Prim("empty:sphere"), Prim("empty:cone"), Prim("empty:image")));
        items.Add(Sub("image", UiText.AddImage, Off("image", UiText.Chinese ? "参考" : "Reference"), Off("image", UiText.Chinese ? "背景" : "Background")));
        items.Add(new Separator());
        var lights = new MenuItem { Header = UiText.AddLight, Icon = LineIcon("light") };
        foreach (var type in new[] { SceneLightType.Point, SceneLightType.Directional, SceneLightType.Spot, SceneLightType.Area })
        {
            var item = new MenuItem { Header = LightTypeName(type), Icon = LightMenuIcon(type) };
            item.Click += (_, _) => AddLight(type);
            lights.Items.Add(item);
        }

        items.Add(lights);
        items.Add(Sub("probe", UiText.AddLightProbe,
            Off("probe", UiText.Chinese ? "球形" : "Sphere"), Off("probe", UiText.Chinese ? "平面" : "Plane"), Off("probe", UiText.Chinese ? "体积" : "Volume")));
        items.Add(new Separator());
        items.Add(Leaf("camera", UiText.AddCamera, () => AddPrimitive("camera")));
        items.Add(new Separator());
        items.Add(Off("speaker", UiText.AddSpeaker));
        items.Add(new Separator());
        string[] forces = UiText.Chinese
            ? ["力", "风", "涡流", "磁力", "谐振", "电荷", "兰纳-琼斯", "纹理", "曲线引导", "拖曳", "湍流", "流体流"]
            : ["Force", "Wind", "Vortex", "Magnetic", "Harmonic", "Charge", "Lennard-Jones", "Texture", "Curve Guide", "Drag", "Turbulence", "Fluid Flow"];
        items.Add(Sub("force", UiText.AddForceField, [.. forces.Select(f => Off("force", f))]));
        items.Add(new Separator());
        items.Add(Sub("collection", UiText.AddCollectionInstance, Off("collection", "Collection")));

        BuildSearch(search, items, searchIndex);
    }

    /// <summary>"Search…": a text box over a flat list of every working item, filtered as you type.</summary>
    void BuildSearch(MenuItem search, ItemCollection items, int searchIndex)
    {
        var box = new TextBox
        {
            Width = 200,
            Background = new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x1D)),
            Foreground = MenuIconBrush,
            CaretBrush = MenuIconBrush,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D)),
            Padding = new Thickness(3, 1, 3, 1),
        };
        var boxItem = new MenuItem { Header = box, StaysOpenOnClick = true, Focusable = false };
        search.Items.Add(boxItem);
        var flat = new List<(MenuItem Item, string Text)>();
        void Collect(ItemCollection from, string path)
        {
            foreach (var o in from)
            {
                if (o is not MenuItem mi || mi == search || mi.Header is not string h || Equals(mi.Tag, "header"))
                {
                    continue;
                }

                if (mi.Items.Count > 0)
                {
                    Collect(mi.Items, h);
                    continue;
                }

                if (!mi.IsEnabled)
                {
                    continue;
                }

                string label = path.Length > 0 ? $"{path} ▸ {h}" : h;
                var copy = new MenuItem { Header = label, Icon = LineIcon(mi.Tag as string ?? "") };
                var source = mi;
                copy.Click += (_, _) => source.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, source));
                flat.Add((copy, label));
            }
        }

        Collect(items, "");
        foreach (var (item, _) in flat)
        {
            search.Items.Add(item);
        }

        box.TextChanged += (_, _) =>
        {
            string q = box.Text.Trim();
            foreach (var (item, text) in flat)
            {
                item.Visibility = q.Length == 0 || text.Contains(q, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
            }
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && flat.FirstOrDefault(f => f.Item.Visibility == Visibility.Visible).Item is { } first)
            {
                first.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, first));
                if (ItemsControl.ItemsControlFromItemContainer(search) is ContextMenu cm)
                {
                    cm.IsOpen = false;
                }

                e.Handled = true;
            }
        };
        search.SubmenuOpened += (_, _) => Dispatcher.BeginInvoke(() => Keyboard.Focus(box), System.Windows.Threading.DispatcherPriority.Input);
    }
}
