using System.IO;
using System.Numerics;
using PolyView.Project;

namespace PolyView;

/// <summary>Project files (.pvproj): capture the scene into a ProjectDocument and restore it.</summary>
public sealed partial class ModelViewport
{
    /// <summary>Snapshot of the viewport scene. Outliner-only state (collection flags, expand) is filled in by OutlinerPanel.</summary>
    public ProjectDocument CaptureProject()
    {
        var doc = new ProjectDocument();
        var assets = new Dictionary<string, ProjectAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in _objects)
        {
            var po = new ProjectObject
            {
                Id = o.Id,
                Name = o.Name,
                Position = [o.Transform.Position.X, o.Transform.Position.Y, o.Transform.Position.Z],
                Rotation = [o.Transform.Rotation.X, o.Transform.Rotation.Y, o.Transform.Rotation.Z, o.Transform.Rotation.W],
                Scale = [o.Transform.Scale.X, o.Transform.Scale.Y, o.Transform.Scale.Z],
                Visible = o.Visible,
                HiddenNodes = [.. o.HiddenNodes.Order()],
            };
            if (o.SourcePath != null && File.Exists(o.SourcePath))
            {
                var full = Path.GetFullPath(o.SourcePath);
                if (!assets.TryGetValue(full, out var asset))
                {
                    asset = new ProjectAsset
                    {
                        Id = ProjectPackage.Sha256OfFile(full),
                        MainFile = Path.GetFileName(full),
                        OriginalPath = full,
                        Files = ProjectPackage.CollectFiles(full).ConvertAll(f => Path.GetFileName(f)!),
                    };
                    assets[full] = asset;
                    if (!doc.Assets.Exists(a => a.Id == asset.Id))
                    {
                        doc.Assets.Add(asset);
                    }
                }

                po.Kind = "model";
                po.AssetId = asset.Id;
            }
            else if (_primitiveSpecs.TryGetValue(o.Id, out var spec))
            {
                po.Kind = "primitive";
                po.Builtin = SerializeSpec(spec);
            }
            else if (o.Mesh.Nodes.Length == 1 && o.Mesh.Nodes[0].Light is { } light && o.Mesh.Vertices.Length == 0)
            {
                po.Kind = "light";
                po.Light = new ProjectLight
                {
                    Type = light.Type.ToString(),
                    Color = [light.Color.X, light.Color.Y, light.Color.Z],
                    Intensity = light.Intensity,
                    Power = light.Power,
                    Radius = light.Radius,
                    Range = light.Range,
                    InnerConeAngle = light.InnerConeAngle,
                    OuterConeAngle = light.OuterConeAngle,
                    SpotBlend = light.SpotBlend,
                    Shape = light.Shape.ToString(),
                    Size = [light.Size.X, light.Size.Y],
                };
            }
            else
            {
                po.Kind = "builtin";
                po.Builtin = "bar";
            }

            doc.Objects.Add(po);
        }

        doc.View = new ProjectView
        {
            Target = [_camera.Target.X, _camera.Target.Y, _camera.Target.Z],
            Yaw = _camera.Yaw,
            Pitch = _camera.Pitch,
            Distance = _camera.Distance,
            FieldOfView = _camera.FieldOfView,
            Orthographic = _camera.Orthographic,
            Shading = _shading.ToString(),
            Tool = _tool.ToString(),
        };
        doc.Selection = new ProjectSelection { ObjectId = _selectedId, NodeIndex = _selectedNode };
        return doc;
    }

    /// <summary>Imports every model of the project (call on a background thread). Result is keyed by asset id.</summary>
    public static Dictionary<string, ImportResult> ImportProjectAssets(ProjectDocument doc, IReadOnlyDictionary<string, string> assetPaths)
    {
        var results = new Dictionary<string, ImportResult>();
        foreach (var id in doc.Objects.Where(o => o.Kind == "model" && o.AssetId != null).Select(o => o.AssetId!).Distinct())
        {
            results[id] = assetPaths.TryGetValue(id, out var path)
                ? MeshImporter.Load(path)
                : ImportResult.Fail("missing");
        }

        return results;
    }

    /// <summary>Replaces the scene with the project (UI thread). Returns old project id -> new object id, and names of objects that could not be restored.</summary>
    public (Dictionary<int, int> IdMap, List<string> Failed) RestoreProject(
        ProjectDocument doc, IReadOnlyDictionary<string, string> assetPaths, IReadOnlyDictionary<string, ImportResult> imports)
    {
        CancelTransform();
        _loadVersion++;
        ClearScene();
        DropUndoEntries(_undo);
        DropUndoEntries(_redo);
        _failure = null;
        var map = new Dictionary<int, int>();
        var failed = new List<string>();
        foreach (var po in doc.Objects)
        {
            SceneObject? obj = null;
            switch (po.Kind)
            {
                case "model":
                    if (po.AssetId != null && imports.TryGetValue(po.AssetId, out var r) && r.Ok && r.Mesh != null)
                    {
                        ApplyMesh(r.Mesh, assetPaths[po.AssetId], append: true);
                        obj = _objects[^1];
                    }

                    break;
                case "light":
                    if (po.Light != null)
                    {
                        obj = CreateLightObject(po);
                    }

                    break;
                case "primitive":
                    if (DeserializeSpec(po.Builtin) is { } ps)
                    {
                        obj = CreatePrimitiveObject(ps, po.Name);
                    }

                    break;
                case "builtin":
                    ApplyMesh(MeshBuilder.CreateBar(), null, append: true);
                    obj = _objects[^1];
                    break;
            }

            if (obj == null)
            {
                failed.Add(po.Name);
                continue;
            }

            map[po.Id] = obj.Id;
            ApplyTransform(obj, new ObjectTransform
            {
                Position = V3(po.Position, Vector3.Zero),
                Rotation = po.Rotation.Length == 4 ? Quaternion.Normalize(new Quaternion(po.Rotation[0], po.Rotation[1], po.Rotation[2], po.Rotation[3])) : Quaternion.Identity,
                Scale = V3(po.Scale, Vector3.One),
            }, commit: false);
            foreach (int n in po.HiddenNodes)
            {
                if (n >= 0 && n < obj.Mesh.Nodes.Length)
                {
                    SetNodeVisible(obj.Id, n, false);
                }
            }

            SetObjectVisible(obj.Id, po.Visible);
        }

        UpdateSceneBounds();
        var v = doc.View;
        _scale = Vector3.One;
        _camera.Target = V3(v.Target, Vector3.Zero);
        _camera.Yaw = v.Yaw;
        _camera.Pitch = Math.Clamp(v.Pitch, -OrbitCamera.MaxPitch, OrbitCamera.MaxPitch);
        _camera.Distance = v.Distance > 0 ? v.Distance : 3f;
        if (v.FieldOfView > 0)
        {
            _camera.FieldOfView = v.FieldOfView;
        }

        _camera.Orthographic = v.Orthographic;
        if (Enum.TryParse<ViewportShading>(v.Shading, out var shading))
        {
            Shading = shading;
        }

        if (Enum.TryParse<TransformTool>(v.Tool, out var tool))
        {
            ActiveTool = tool;
        }

        if (map.TryGetValue(doc.Selection.ObjectId, out int sel))
        {
            Select(sel);
            if (doc.Selection.NodeIndex >= 0)
            {
                SelectNode(sel, doc.Selection.NodeIndex);
            }
        }
        else
        {
            Select(-1);
        }

        RequestFreshFrames();
        SceneChanged?.Invoke(this, EventArgs.Empty);
        ViewChanged?.Invoke(this, EventArgs.Empty);
        return (map, failed);
    }

    SceneObject CreateLightObject(ProjectObject po)
    {
        var pl = po.Light!;
        var light = new SceneLight
        {
            Type = Enum.TryParse<SceneLightType>(pl.Type, out var t) ? t : SceneLightType.Point,
            Color = V3(pl.Color, Vector3.One),
            Intensity = pl.Intensity,
            Power = pl.Power,
            Radius = pl.Radius,
            Range = pl.Range,
            InnerConeAngle = pl.InnerConeAngle,
            OuterConeAngle = pl.OuterConeAngle,
            SpotBlend = pl.SpotBlend,
            Shape = Enum.TryParse<AreaShape>(pl.Shape, out var s) ? s : AreaShape.Square,
            Size = pl.Size.Length == 2 ? new Vector2(pl.Size[0], pl.Size[1]) : Vector2.Zero,
        };
        var mesh = new MeshData
        {
            Name = po.Name,
            Vertices = [],
            Indices = [],
            Min = new Vector3(-0.05f),
            Max = new Vector3(0.05f),
        };
        mesh.Nodes = SceneNode.Link([new SceneNode { Index = 0, Name = po.Name, Kind = SceneNodeKind.Light, Light = light }]);
        var obj = new SceneObject { Id = _nextId++, Name = po.Name, Mesh = mesh };
        _objects.Add(obj);
        return obj;
    }

    static Vector3 V3(float[]? a, Vector3 fallback) =>
        a is { Length: 3 } ? new Vector3(a[0], a[1], a[2]) : fallback;
}

public sealed partial class ModelViewport
{
    /// <summary>Synchronous import + append, used by the project self-test.</summary>
    internal void AddModelForTest(string path)
    {
        var r = MeshImporter.Load(path);
        if (r.Ok && r.Mesh != null)
        {
            ApplyMesh(r.Mesh, path, append: true);
        }
    }
}
