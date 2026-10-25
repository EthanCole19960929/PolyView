using System.IO;
using System.Numerics;
using PolyView.Import;

namespace PolyView;

/// <summary>Blender-style import: the file is added to the current scene (nothing is replaced), undoable, selected and active.</summary>
public sealed partial class ModelViewport
{
    /// <summary>Blender-style unique name: "name", then "name.001", "name.002" ...</summary>
    public string UniqueObjectName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Object";
        }

        var used = new HashSet<string>(_objects.Select(o => o.Name), StringComparer.Ordinal);
        if (!used.Contains(name))
        {
            return name;
        }

        for (int i = 1; ; i++)
        {
            var candidate = $"{name}.{i:000}";
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Add an imported mesh as a new object. The file keeps its own coordinates relative to the scene origin; scale,
    /// clamp size and axis conversion go into the object transform (so they are undoable and saved in .pvproj).
    /// </summary>
    public SceneObject ImportMesh(MeshData mesh, string? path, ImportOptions? options = null)
    {
        options ??= ImportOptions.Default;
        var axis = options.AxisMatrix();
        CancelTransform();
        mesh = Recenter(mesh, out var center);

        float scale = options.Scale > 0 && float.IsFinite(options.Scale) ? options.Scale : 1f;
        if (options.ClampSize > 0)
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3(
                    (i & 1) == 0 ? mesh.Min.X : mesh.Max.X,
                    (i & 2) == 0 ? mesh.Min.Y : mesh.Max.Y,
                    (i & 4) == 0 ? mesh.Min.Z : mesh.Max.Z);
                var p = Vector3.Transform(c, axis) * scale;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            var size = max - min;
            float largest = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
            if (largest > options.ClampSize)
            {
                scale *= options.ClampSize / largest;
            }
        }

        var baseName = path == null ? mesh.Name : Path.GetFileNameWithoutExtension(path);
        var obj = new SceneObject
        {
            Id = _nextId++,
            Name = UniqueObjectName(baseName),
            SourcePath = path,
            AnimationSourcePath = path,
            Mesh = mesh,
            ImportOffset = center,
        };
        obj.Transform = new ObjectTransform
        {
            Scale = new Vector3(scale),
            Rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(axis)),
            Position = Vector3.Transform(center, Matrix4x4.CreateScale(scale) * axis),
        };

        _objects.Add(obj);
        _renderer?.AddMesh(obj.Id, mesh);
        _renderer?.SetWorld(obj.Id, obj.World);
        _failure = null;
        UpdateSceneBounds();
        PushUndo(AddEntryId(obj.Id), obj.Transform, obj.Transform);
        Select(obj.Id);
        ShowImportedTextures(mesh);
        Invalidate();
        SceneChanged?.Invoke(this, EventArgs.Empty);
        ViewChanged?.Invoke(this, EventArgs.Empty);
        return obj;
    }

    /// <summary>Synchronous import of a file into the scene (UI thread). Throws InvalidDataException with the importer error.</summary>
    public SceneObject ImportFile(string path, ImportOptions? options = null)
    {
        path = Path.GetFullPath(path);
        var result = MeshImporter.Load(path);
        if (!result.Ok || result.Mesh == null)
        {
            throw new InvalidDataException(result.Error);
        }

        return ImportMesh(result.Mesh, path, options);
    }

    /// <summary>Restore saved object names after RestoreProject (names may carry .001 suffixes).</summary>
    public void ApplyProjectNames(PolyView.Project.ProjectDocument doc, IReadOnlyDictionary<int, int> idMap)
    {
        bool changed = false;
        foreach (var po in doc.Objects)
        {
            if (!string.IsNullOrEmpty(po.Name) && idMap.TryGetValue(po.Id, out int id) && TryGetObject(id, out var o) && o.Name != po.Name)
            {
                o.Name = po.Name;
                changed = true;
            }
        }

        if (changed)
        {
            SceneChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
