using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PolyView.Mcp;

namespace PolyView;

/// <summary>MCP automation entry points (run on the UI thread, called by Mcp/PolyViewTools). See docs/11.</summary>
public partial class MainWindow
{
    void InitMcp()
    {
        PolyViewTools.Window = this;
        InitMcpMenu();
        if (!AppSettings.Current.McpEnabled) return;
        _ = McpServerHost.StartAsync();
        _ = Task.Run(McpClients.AutoConnectAll);
        Closed += (_, _) => McpServerHost.StopAsync().Wait(1500);
    }

    static float[] V(Vector3 v) => [MathF.Round(v.X, 4), MathF.Round(v.Y, 4), MathF.Round(v.Z, 4)];

    static Vector3? Vec(float[]? a) => a is { Length: 3 } ? new Vector3(a[0], a[1], a[2]) : a == null ? null : throw new ArgumentException("expected [x,y,z]");

    internal object McpGetScene()
    {
        var objects = new List<object>();
        foreach (var id in Viewport.ObjectIds)
        {
            if (!Viewport.TryGetObjectInfo(id, out var info)) continue;
            Viewport.TryGetTransform(id, out var t);
            var nodes = Viewport.GetNodes(id);
            var light = nodes.Count == 1 && nodes[0].Kind == SceneNodeKind.Light ? nodes[0].Light : null;
            objects.Add(new
            {
                id,
                name = info.Name,
                kind = light != null ? "light" : "mesh",
                light_type = light == null ? null : LightName(light.Type),
                visible = info.Visible,
                vertices = info.VertexCount,
                triangles = info.TriangleCount,
                position = V(t.Position),
                rotation_deg = V(t.EulerDegrees),
                scale = V(t.Scale),
                bounds_min = V(info.Min),
                bounds_max = V(info.Max),
                source = info.SourcePath,
                collection = Outliner.Root.FindOwner(id)?.Name,
            });
        }

        return new { ok = true, shading = ShadingName(Viewport.Shading), selected = Viewport.SelectedId, project = _projectPath, objects, collections = Outliner.DescribeCollections() };
    }

    static string LightName(SceneLightType t) => t switch
    {
        SceneLightType.Directional => "sun",
        SceneLightType.Point => "point",
        SceneLightType.Spot => "spot",
        SceneLightType.Area => "area",
        _ => t.ToString().ToLowerInvariant(),
    };

    static string ShadingName(ViewportShading s) => s switch
    {
        ViewportShading.Wireframe => "wireframe",
        ViewportShading.MaterialPreview => "material",
        ViewportShading.Rendered => "rendered",
        _ => "solid",
    };

    internal object McpSelect(int id)
    {
        if (id >= 0 && !Viewport.ObjectIds.Contains(id)) return new { ok = false, error = $"no object {id}" };
        Viewport.Select(id);
        return new { ok = true, selected = Viewport.SelectedId };
    }

    internal object McpSetTransform(int id, float[]? position, float[]? rotation, float[]? scale)
    {
        if (!Viewport.TryGetTransform(id, out var t)) return new { ok = false, error = $"no object {id}" };
        if (Vec(position) is { } p) t.Position = p;
        if (Vec(rotation) is { } r) t.Rotation = ObjectTransform.FromEuler(r);
        if (Vec(scale) is { } s) t.Scale = s;
        Viewport.SetTransform(id, t);
        Viewport.TryGetTransform(id, out t);
        return new { ok = true, id, position = V(t.Position), rotation_deg = V(t.EulerDegrees), scale = V(t.Scale) };
    }

    internal object McpAddLight(string type, float[]? position)
    {
        var lt = type.Trim().ToLowerInvariant() switch
        {
            "point" => SceneLightType.Point,
            "sun" or "directional" => SceneLightType.Directional,
            "spot" => SceneLightType.Spot,
            "area" => SceneLightType.Area,
            _ => SceneLightType.Unknown,
        };
        if (lt == SceneLightType.Unknown) return new { ok = false, error = "type must be point | sun | spot | area" };
        var before = Viewport.ObjectIds.ToHashSet();
        Viewport.AddLight(lt);
        var id = Viewport.ObjectIds.FirstOrDefault(i => !before.Contains(i), -1);
        if (id < 0) return new { ok = false, error = "light not added (limit 16?)" };
        if (Vec(position) is { } p && Viewport.TryGetTransform(id, out var t))
        {
            t.Position = p;
            Viewport.SetTransform(id, t);
        }

        Viewport.TryGetTransform(id, out var now);
        return new { ok = true, id, type = LightName(lt), position = V(now.Position) };
    }

    internal object McpDelete(int id)
    {
        if (!Viewport.TryGetObjectInfo(id, out var info)) return new { ok = false, error = $"no object {id}" };
        Viewport.RemoveObject(id);
        return new { ok = true, deleted = id };
    }

    internal object McpSetShading(string mode)
    {
        ViewportShading? s = mode.Trim().ToLowerInvariant() switch
        {
            "wireframe" => ViewportShading.Wireframe,
            "solid" => ViewportShading.Solid,
            "material" or "materialpreview" or "material_preview" => ViewportShading.MaterialPreview,
            "rendered" => ViewportShading.Rendered,
            _ => null,
        };
        if (s == null) return new { ok = false, error = "mode must be wireframe | solid | material | rendered" };
        Viewport.Shading = s.Value;
        return new { ok = true, shading = ShadingName(Viewport.Shading) };
    }

    internal object McpImport(string path)
    {
        if (!File.Exists(path)) return new { ok = false, error = "file not found" };
        if (!MeshImporter.IsSupported(path)) return new { ok = false, error = "unsupported format: " + string.Join(" ", MeshImporter.SupportedExtensions) };
        var added = ImportModel(Path.GetFullPath(path));
        return new { ok = true, id = added.Id, name = added.Name };
    }

    internal object McpSave(string? path)
    {
        if (path != null)
        {
            if (!path.EndsWith(".pvproj", StringComparison.OrdinalIgnoreCase)) path += ".pvproj";
            _projectPath = Path.GetFullPath(path);
        }
        else if (_projectPath == null)
        {
            return new { ok = false, error = "project never saved; pass a path" };
        }

        bool saved = SaveProject(false);
        return new { ok = saved, path = _projectPath };
    }

    internal byte[]? McpScreenshot()
    {
        Viewport.Invalidate();
        Viewport.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(Viewport);
        int w = Math.Max(1, (int)(Viewport.ActualWidth * dpi.DpiScaleX));
        int h = Math.Max(1, (int)(Viewport.ActualHeight * dpi.DpiScaleY));
        var rtb = new RenderTargetBitmap(w, h, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        rtb.Render(Viewport);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
