using System.Text.Json.Serialization;

namespace PolyView.Project;

/// <summary>Root of project.json inside a .pvproj package (see docs/09_工程文件格式.md).</summary>
public sealed class ProjectDocument
{
    public const string FormatName = "PolyView.Project";
    public const int CurrentVersion = 1;

    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = CurrentVersion;
    public string App { get; set; } = "PolyView";
    public DateTime SavedAt { get; set; } = DateTime.Now;
    public List<ProjectAsset> Assets { get; set; } = [];
    public List<ProjectObject> Objects { get; set; } = [];
    public ProjectCollection Collection { get; set; } = new();
    public ProjectView View { get; set; } = new();
    public ProjectSelection Selection { get; set; } = new();
}

/// <summary>An embedded original model file (and its side files) stored under assets/&lt;sha256&gt;/.</summary>
public sealed class ProjectAsset
{
    public string Id { get; set; } = "";          // sha256 of the main file (hex)
    public string MainFile { get; set; } = "";    // file name inside assets/<id>/
    public string? OriginalPath { get; set; }     // absolute path at save time
    public bool Embedded { get; set; } = true;
    public List<string> Files { get; set; } = []; // all file names inside assets/<id>/
}

public sealed class ProjectObject
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>"model" (from an asset), "light" (added light), "builtin" (built-in sample mesh).</summary>
    public string Kind { get; set; } = "model";
    public string? AssetId { get; set; }
    public string? Builtin { get; set; }
    public ProjectLight? Light { get; set; }
    public float[] Position { get; set; } = [0, 0, 0];
    public float[] Rotation { get; set; } = [0, 0, 0, 1];  // quaternion x y z w
    public float[] Scale { get; set; } = [1, 1, 1];
    public bool Visible { get; set; } = true;               // own eye state
    public List<int> HiddenNodes { get; set; } = [];
    public List<int> RenderOffNodes { get; set; } = [];     // -1 = the object row itself
    public List<int> ExpandedNodes { get; set; } = [];      // -1 = the object row itself
}

public sealed class ProjectLight
{
    public string Type { get; set; } = "Point";
    public float[] Color { get; set; } = [1, 1, 1];
    public float Intensity { get; set; } = 1f;
    public float Power { get; set; }
    public float Radius { get; set; }
    public float Range { get; set; }
    public float InnerConeAngle { get; set; }
    public float OuterConeAngle { get; set; }
    public float SpotBlend { get; set; }
    public string Shape { get; set; } = "Square";
    public float[] Size { get; set; } = [0, 0];
}

public sealed class ProjectCollection
{
    public string Name { get; set; } = "Collection";
    public bool Included { get; set; } = true;
    public bool Visible { get; set; } = true;
    public bool Renderable { get; set; } = true;
    public bool Expanded { get; set; } = true;
}

public sealed class ProjectView
{
    public float[] Target { get; set; } = [0, 0, 0];
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float Distance { get; set; } = 3f;
    public float FieldOfView { get; set; }
    public bool Orthographic { get; set; }
    public string Shading { get; set; } = "Solid";
    public string Tool { get; set; } = "Select";
}

public sealed class ProjectSelection
{
    public int ObjectId { get; set; } = -1;
    public int NodeIndex { get; set; } = -1;
}

/// <summary>Save parameters. Empty for now; later options (link instead of embed, thumbnail size, compression) go here.</summary>
public sealed class ProjectSaveOptions
{
    public bool EmbedAssets { get; set; } = true;
    public bool WriteThumbnail { get; set; } = true;

    public static ProjectSaveOptions Default => new();
}
