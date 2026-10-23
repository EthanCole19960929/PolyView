namespace PolyView.Export;

/// <summary>Export settings. There is no options window yet; the defaults are used everywhere for now.</summary>
public sealed class ExportOptions
{
    /// <summary>Export only the selected object (when one is selected) instead of every visible object.</summary>
    public bool SelectionOnly { get; init; }

    /// <summary>Skip objects / nodes hidden in the outliner.</summary>
    public bool SkipHidden { get; init; } = true;

    /// <summary>Write textures (PNG next to the file; embedded for GLB).</summary>
    public bool IncludeTextures { get; init; } = true;

    /// <summary>Write per-vertex colours when a mesh part has more than one colour.</summary>
    public bool IncludeVertexColors { get; init; } = true;

    /// <summary>STL / PLY are written Z-up (what Blender and slicers expect). Other formats stay Y-up.</summary>
    public bool ZUpForStlPly { get; init; } = true;

    public static ExportOptions Default => new();
}

/// <summary>One supported export format.</summary>
public sealed record ExportFormat(string Key, string AssimpId, string Extension, string Label)
{
    public static readonly ExportFormat[] All =
    [
        new("fbx", "fbx", ".fbx", "FBX"),
        new("obj", "obj", ".obj", "Wavefront OBJ"),
        new("gltf", "gltf2", ".gltf", "glTF 2.0 (.gltf)"),
        new("glb", "glb2", ".glb", "glTF 2.0 Binary (.glb)"),
        new("stl", "stlb", ".stl", "STL"),
        new("ply", "plyb", ".ply", "Stanford PLY"),
        new("dae", "collada", ".dae", "Collada (.dae)"),
    ];

    /// <summary>Accepts a key (fbx), an extension (.fbx), an Assimp id (gltf2, collada, stlb ...) or a label; null when unknown.</summary>
    public static ExportFormat? Find(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return null;
        }

        var f = format.Trim().TrimStart('.').ToLowerInvariant();
        f = f switch
        {
            "gltf2" => "gltf",
            "glb2" => "glb",
            "collada" => "dae",
            "stlb" => "stl",
            "plyb" => "ply",
            _ => f,
        };
        return All.FirstOrDefault(x => x.Key == f);
    }

    public static ExportFormat? FromPath(string path) => Find(System.IO.Path.GetExtension(path));
}

/// <summary>Result of an export.</summary>
public sealed class ExportResult
{
    public required string Path { get; init; }
    public required string Format { get; init; }
    public int ObjectCount { get; init; }
    public int MeshCount { get; init; }
    public int VertexCount { get; init; }
    public int TriangleCount { get; init; }
    public System.Numerics.Vector3 Min { get; init; }
    public System.Numerics.Vector3 Max { get; init; }

    /// <summary>Extra files written next to the model (textures, .mtl, .bin).</summary>
    public IReadOnlyList<string> ExtraFiles { get; init; } = [];
}

/// <summary>Thrown with a user-readable message when an export cannot be done.</summary>
public sealed class ExportException(string message, Exception? inner = null) : Exception(message, inner);
