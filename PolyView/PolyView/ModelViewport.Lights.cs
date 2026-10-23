using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PolyView;

/// <summary>
/// Scene lights per docs/07: gathers file and added lights (max 16) for the shader, builds Blender-style
/// viewport light icons, picks lights by their icon, and the Shift+A Add menu.
/// </summary>
public sealed partial class ModelViewport
{
    readonly SceneRenderer.LightData[] _lightData = new SceneRenderer.LightData[SceneRenderer.MaxLights];
    Vertex[] _iconVertices = new Vertex[4096];
    int _iconCount;
    static readonly Vector3 DefaultAmbient = new SceneWorld().Color;

    static readonly uint IconColor = Vertex.PackColor(new Vector4(0.02f, 0.02f, 0.02f, 1f));
    static readonly uint IconSelectedColor = Vertex.PackColor(new Vector4(1f, 0.667f, 0.251f, 1f));

    /// <summary>Display name of a light subtype (UiText LightPoint / Sun / Spot / Area).</summary>
    public static string LightTypeName(SceneLightType type) => type switch
    {
        SceneLightType.Directional => UiText.LightSun,
        SceneLightType.Point => UiText.LightPoint,
        SceneLightType.Spot => UiText.LightSpot,
        SceneLightType.Area => UiText.LightArea,
        SceneLightType.Ambient => UiText.LightAmbient,
        _ => UiText.KindLight,
    };

    /// <summary>Light node world matrix: node.World is in original file space; vertices were recentered by ImportOffset.</summary>
    Matrix4x4 LightMatrix(SceneObject o, SceneNode node) =>
        node.World * Matrix4x4.CreateTranslation(-o.ImportOffset) * o.World * Matrix4x4.CreateScale(_scale);

    /// <summary>Called before each rendered frame: lights to the shader and icons to the overlay. No allocations.</summary>
    void UpdateLights()
    {
        if (_renderer == null)
        {
            return;
        }

        int count = 0;
        var ambient = Vector3.Zero;
        bool worldFromFile = false;
        _iconCount = 0;
        GetViewBasis(out var right, out var up);
        for (int i = 0; i < _objects.Count; i++)
        {
            var o = _objects[i];
            if (!o.Visible)
            {
                continue;
            }

            if (o.Mesh.World.FromFile && !worldFromFile)
            {
                ambient = o.Mesh.World.Color * o.Mesh.World.Strength;
                worldFromFile = true;
            }

            var nodes = o.Mesh.Nodes;
            for (int n = 0; n < nodes.Length; n++)
            {
                var light = nodes[n].Light;
                if (light == null || o.HiddenNodes.Contains(n) || light.Type is SceneLightType.Unknown or SceneLightType.Ambient)
                {
                    continue;
                }

                var m = LightMatrix(o, nodes[n]);
                var position = m.Translation;
                var direction = Vector3.TransformNormal(-Vector3.UnitZ, m);
                direction = direction.LengthSquared() < 1e-12f ? -Vector3.UnitY : Vector3.Normalize(direction);
                bool selected = o.Id == _selectedId && (_selectedNode < 0 || _selectedNode == n);
                AddLightIcon(light, m, position, direction, right, up, selected ? IconSelectedColor : IconColor);
                if (count >= SceneRenderer.MaxLights)
                {
                    continue;
                }

                _lightData[count++] = ToLightData(light, position, direction);
            }
        }

        if (!worldFromFile)
        {
            ambient = DefaultAmbient;
        }

        _renderer.SetLights(_lightData, count, ambient, ShadingCode);
        _renderer.SetLightIcons(_iconVertices, _iconCount);
    }

    static SceneRenderer.LightData ToLightData(SceneLight light, Vector3 position, Vector3 direction)
    {
        // Radiance scale (viewport, Eevee-like): point/spot I = P / (4 pi) W/sr, area I0 = P / pi, Lambert BRDF 1/pi; sun E = strength.
        float power = MathF.Max(light.Power, 0f);
        float scale = light.Type switch
        {
            SceneLightType.Directional => power,
            SceneLightType.Area => power / (MathF.PI * MathF.PI),
            _ => power / (4f * MathF.PI * MathF.PI),
        };
        float type = light.Type switch
        {
            SceneLightType.Directional => 1f,
            SceneLightType.Point => 2f,
            SceneLightType.Spot => 3f,
            _ => 4f,
        };
        float outer = light.OuterConeAngle > 0 ? light.OuterConeAngle : SceneLight.DefaultSpotSize / 2f;
        float inner = light.InnerConeAngle > 0 && light.InnerConeAngle < outer
            ? light.InnerConeAngle
            : outer * (1f - Math.Clamp(light.SpotBlend, 0f, 1f));
        return new SceneRenderer.LightData
        {
            PositionType = new Vector4(position, type),
            DirectionRange = new Vector4(direction, MathF.Max(light.Range, 0f)),
            ColorPower = new Vector4(light.Color * scale, light.Radius),
            Cone = new Vector4(MathF.Cos(inner), MathF.Cos(outer), 0f, 0f),
        };
    }

    void GetViewBasis(out Vector3 right, out Vector3 up)
    {
        var toTarget = _camera.Target - _camera.Eye;
        var forward = toTarget.LengthSquared() < 1e-12f ? -Vector3.UnitZ : Vector3.Normalize(toTarget);
        right = Vector3.Cross(forward, Vector3.UnitY);
        right = right.LengthSquared() < 1e-8f ? Vector3.UnitX : Vector3.Normalize(right);
        up = Vector3.Cross(right, forward);
    }

    void AddLine(Vector3 a, Vector3 b, uint color)
    {
        if (_iconCount + 2 > _iconVertices.Length)
        {
            if (_iconVertices.Length >= 1 << 16)
            {
                return;
            }

            Array.Resize(ref _iconVertices, _iconVertices.Length * 2);
        }

        _iconVertices[_iconCount++] = new Vertex(a, Vector3.UnitY) { Color = color };
        _iconVertices[_iconCount++] = new Vertex(b, Vector3.UnitY) { Color = color };
    }

    void AddCircle(Vector3 center, Vector3 u, Vector3 v, float ru, float rv, uint color, int segments = 32)
    {
        var previous = center + u * ru;
        for (int i = 1; i <= segments; i++)
        {
            float t = i * MathF.Tau / segments;
            var p = center + u * (ru * MathF.Cos(t)) + v * (rv * MathF.Sin(t));
            AddLine(previous, p, color);
            previous = p;
        }
    }

    /// <summary>Blender viewport light icons (docs/07 §3). Point / Sun icons keep a constant screen size.</summary>
    void AddLightIcon(SceneLight light, in Matrix4x4 m, Vector3 position, Vector3 direction, Vector3 right, Vector3 up, uint color)
    {
        float px = GizmoWorldSize(position) / Gizmo.ScreenSize; // world units per DIP
        if (!float.IsFinite(px) || px <= 0)
        {
            return;
        }

        // Every type gets Blender's small center dot + outer circle.
        AddCircle(position, right, up, 2.5f * px, 2.5f * px, color, 12);
        AddCircle(position, right, up, 9f * px, 9f * px, color, 24);
        switch (light.Type)
        {
            case SceneLightType.Point:
                if (light.Radius > 0)
                {
                    float r = light.Radius * MathF.Max(_scale.X, MathF.Max(_scale.Y, _scale.Z));
                    AddCircle(position, right, up, r, r, color, 32);
                }

                break;
            case SceneLightType.Directional:
            {
                for (int i = 0; i < 8; i++)
                {
                    float t = i * MathF.Tau / 8;
                    var d = right * MathF.Cos(t) + up * MathF.Sin(t);
                    AddLine(position + d * (12f * px), position + d * (17f * px), color);
                }

                AddLine(position, position + direction * (90f * px), color);
                break;
            }

            case SceneLightType.Spot:
            {
                float outer = light.OuterConeAngle > 0 ? light.OuterConeAngle : SceneLight.DefaultSpotSize / 2f;
                float inner = outer * (1f - Math.Clamp(light.SpotBlend, 0f, 1f));
                float length = 1.5f;
                Basis(direction, out var u, out var v);
                var baseCenter = position + direction * length;
                float ro = length * MathF.Tan(MathF.Min(outer, 1.5f));
                float ri = length * MathF.Tan(MathF.Min(inner, 1.5f));
                AddCircle(baseCenter, u, v, ro, ro, color, 32);
                if (ri > 1e-4f && light.SpotBlend > 0)
                {
                    AddCircle(baseCenter, u, v, ri, ri, color, 32);
                }

                for (int i = 0; i < 8; i++)
                {
                    float t = i * MathF.Tau / 8;
                    AddLine(position, baseCenter + (u * MathF.Cos(t) + v * MathF.Sin(t)) * ro, color);
                }

                break;
            }

            case SceneLightType.Area:
            {
                // Local X / Y axes carry the object's scale, so the shape has its real size.
                var ux = Vector3.TransformNormal(Vector3.UnitX, m);
                var uy = Vector3.TransformNormal(Vector3.UnitY, m);
                float sx = light.Size.X > 0 ? light.Size.X : 1f;
                float sy = light.Shape is AreaShape.Rectangle or AreaShape.Ellipse && light.Size.Y > 0 ? light.Size.Y : sx;
                if (light.Shape is AreaShape.Disk or AreaShape.Ellipse)
                {
                    AddCircle(position, ux, uy, sx * 0.5f, sy * 0.5f, color, 32);
                }
                else
                {
                    var a = ux * (sx * 0.5f);
                    var b = uy * (sy * 0.5f);
                    AddLine(position - a - b, position + a - b, color);
                    AddLine(position + a - b, position + a + b, color);
                    AddLine(position + a + b, position - a + b, color);
                    AddLine(position - a + b, position - a - b, color);
                }

                AddLine(position, position + direction * MathF.Max(0.5f, MathF.Max(sx, sy) * 0.5f), color);
                break;
            }
        }
    }

    static void Basis(Vector3 a, out Vector3 u, out Vector3 v)
    {
        u = MathF.Abs(a.Y) < 0.9f ? Vector3.Normalize(Vector3.Cross(a, Vector3.UnitY)) : Vector3.Normalize(Vector3.Cross(a, Vector3.UnitX));
        v = Vector3.Cross(a, u);
    }

    /// <summary>Pick a light by its icon (within 10 DIP of the center). Returns false when none is close.</summary>
    bool TryPickLight(Point p, out int objectId, out int nodeIndex)
    {
        objectId = -1;
        nodeIndex = -1;
        float best = 10f;
        var mouse = new Vector2((float)p.X, (float)p.Y);
        foreach (var o in _objects)
        {
            if (!o.Visible)
            {
                continue;
            }

            var nodes = o.Mesh.Nodes;
            for (int n = 0; n < nodes.Length; n++)
            {
                if (nodes[n].Light == null || o.HiddenNodes.Contains(n))
                {
                    continue;
                }

                var screen = Project(LightMatrix(o, nodes[n]).Translation);
                if (screen == null)
                {
                    continue;
                }

                float d = Vector2.Distance(screen.Value, mouse);
                if (d < best)
                {
                    best = d;
                    objectId = o.Id;
                    nodeIndex = nodes.Length == 1 ? -1 : n;
                }
            }
        }

        return objectId >= 0;
    }

    // ---------------- Shift+A ----------------

    /// <summary>Blender Shift+A Add menu at the mouse. Only Light is implemented; the rest are shown disabled.</summary>
    void ShowAddMenu()
    {
        var menu = new ContextMenu
        {
            PlacementTarget = this,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
        };
        if (Window.GetWindow(this) is { } window)
        {
            menu.Resources.MergedDictionaries.Add(window.Resources);
        }

        PopulateAddMenu(menu.Items);
        menu.IsOpen = true;
    }

    /// <summary>Blender Add menu items (shared by Shift+A and the top-bar Add menu). Only lights are implemented.</summary>
    public void PopulateAddMenu(ItemCollection items)
    {
        var menu = new { Items = items };
        menu.Items.Add(new MenuItem { Header = UiText.AddTitle, IsEnabled = false, InputGestureText = "Shift+A", Tag = "header" });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = UiText.AddMesh, IsEnabled = false });
        menu.Items.Add(new MenuItem { Header = UiText.AddCurve, IsEnabled = false });
        menu.Items.Add(new MenuItem { Header = UiText.AddEmpty, IsEnabled = false });
        menu.Items.Add(new Separator());
        var lights = new MenuItem { Header = UiText.AddLight };
        foreach (var type in new[] { SceneLightType.Point, SceneLightType.Directional, SceneLightType.Spot, SceneLightType.Area })
        {
            var item = new MenuItem { Header = LightTypeName(type), Icon = LightMenuIcon(type) };
            item.Click += (_, _) => AddLight(type);
            lights.Items.Add(item);
        }

        menu.Items.Add(lights);
        menu.Items.Add(new MenuItem { Header = UiText.AddCamera, IsEnabled = false });
    }

    /// <summary>Blender-style light icons (vector, light grey).</summary>
    static UIElement LightMenuIcon(SceneLightType type)
    {
        string data = type switch
        {
            SceneLightType.Point => "M7,2.5 A4.5,4.5 0 1 1 6.99,2.5 Z M7,5 A2,2 0 1 0 7.01,5 Z",
            SceneLightType.Directional => "M7,4.5 A2.5,2.5 0 1 1 6.99,4.5 Z M6.4,0 H7.6 V3 H6.4 Z M6.4,11 H7.6 V14 H6.4 Z M0,6.4 H3 V7.6 H0 Z M11,6.4 H14 V7.6 H11 Z M1.8,2.6 L2.6,1.8 L4.6,3.8 L3.8,4.6 Z M9.4,10.2 L10.2,9.4 L12.2,11.4 L11.4,12.2 Z M9.4,3.8 L11.4,1.8 L12.2,2.6 L10.2,4.6 Z M1.8,11.4 L3.8,9.4 L4.6,10.2 L2.6,12.2 Z",
            SceneLightType.Spot => "M5,1 H9 V4 L13,12 H1 L5,4 Z",
            _ => "M2,2 H12 V8 H2 Z M3.5,9 H4.7 V13 H3.5 Z M6.4,9 H7.6 V13 H6.4 Z M9.3,9 H10.5 V13 H9.3 Z",
        };
        return new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data),
            Fill = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
            Width = 14,
            Height = 14,
            Stretch = Stretch.Uniform,
        };
    }

    /// <summary>Add a Blender-default light as a new scene object (Point 1000 W, Sun 1 W/m², Spot 45°, Area 1 m, 1000 W).</summary>
    public void AddLight(SceneLightType type)
    {
        var light = new SceneLight
        {
            Type = type,
            Power = type == SceneLightType.Directional ? SceneLight.DefaultSunStrength : SceneLight.DefaultPointPower,
            Radius = type is SceneLightType.Point or SceneLightType.Spot ? SceneLight.DefaultRadius : 0f,
            OuterConeAngle = type == SceneLightType.Spot ? SceneLight.DefaultSpotSize / 2f : 0f,
            SpotBlend = type == SceneLightType.Spot ? SceneLight.DefaultSpotBlend : 0f,
            Shape = AreaShape.Square,
            Size = type == SceneLightType.Area ? new Vector2(1f, 1f) : Vector2.Zero,
        };
        string name = LightTypeName(type);
        var mesh = new MeshData
        {
            Name = name,
            Vertices = [],
            Indices = [],
            Min = new Vector3(-0.05f),
            Max = new Vector3(0.05f),
        };
        mesh.Nodes = SceneNode.Link([new SceneNode { Index = 0, Name = name, Kind = SceneNodeKind.Light, Light = light }]);
        var obj = new SceneObject { Id = _nextId++, Name = name, Mesh = mesh };
        // Blender adds at the 3D cursor; we put it above the scene. Sun / Spot / Area point down (local -Z -> world down).
        float height = _objects.Count == 0 ? 2f : MathF.Max(_sceneMax.Y, 0f) + MathF.Max(SceneRadius, 0.5f);
        obj.Transform.Position = new Vector3(0f, height, 0f);
        if (type != SceneLightType.Point)
        {
            obj.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f);
        }

        _objects.Add(obj);
        PushUndo(AddEntryId(obj.Id), obj.Transform, obj.Transform);
        Select(obj.Id);
        Invalidate();
        SceneChanged?.Invoke(this, EventArgs.Empty);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }
}
