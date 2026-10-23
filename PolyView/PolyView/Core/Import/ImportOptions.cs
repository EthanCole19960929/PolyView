using System.IO;
using System.Numerics;
using System.Text.Json;

namespace PolyView.Import;

/// <summary>
/// Import settings, modelled on Blender 4.2 / 5.x import operators (File > Import).
/// Scale, ClampSize, Forward and Up are applied to the new object's transform; the remaining fields are kept for
/// presets and later use (PolyView imports one file as one object with its node tree).
/// </summary>
public sealed class ImportOptions
{
    public static readonly string[] Axes = ["X", "Y", "Z", "-X", "-Y", "-Z"];

    // 常规 / General
    public float Scale { get; set; } = 1f;
    /// <summary>Blender "Clamp Bounding Box": 0 = off, otherwise the largest dimension is scaled down to this size (m).</summary>
    public float ClampSize { get; set; }
    public string Forward { get; set; } = "-Z";
    public string Up { get; set; } = "Y";

    // 选项 / Options
    public bool SplitByObject { get; set; } = true;
    public bool SplitByGroup { get; set; }
    public bool VertexGroups { get; set; }
    public bool ValidateMeshes { get; set; } = true;
    public bool DetectClosedCurves { get; set; } = true;
    public string PathSeparator { get; set; } = "";

    // 材质 / Materials
    public string MaterialNameCollision { get; set; } = "MakeUnique";

    // FBX
    public bool FbxCustomNormals { get; set; } = true;
    public bool FbxImportAnimation { get; set; } = true;
    public bool FbxApplyTransform { get; set; }
    public bool FbxAutomaticBoneOrientation { get; set; }

    // glTF
    public bool GltfMergeVertices { get; set; }
    public bool GltfImportShading { get; set; } = true;
    public bool GltfGuessBindPose { get; set; } = true;
    public bool GltfImportLights { get; set; } = true;

    public static ImportOptions Default => new();

    public ImportOptions Clone() => JsonSerializer.Deserialize<ImportOptions>(JsonSerializer.Serialize(this))!;

    static Vector3 AxisVector(string axis) => axis.Trim().ToUpperInvariant() switch
    {
        "X" or "+X" => Vector3.UnitX,
        "-X" => -Vector3.UnitX,
        "Y" or "+Y" => Vector3.UnitY,
        "-Y" => -Vector3.UnitY,
        "Z" or "+Z" => Vector3.UnitZ,
        "-Z" => -Vector3.UnitZ,
        _ => throw new ArgumentException($"Unknown axis: {axis}"),
    };

    /// <summary>
    /// Rotation that maps the file's forward axis to PolyView -Z and its up axis to +Y (PolyView is Y-up, right-handed).
    /// Forward -Z / Up Y is the identity. Throws when forward and up are the same axis line.
    /// </summary>
    public Matrix4x4 AxisMatrix()
    {
        var f = AxisVector(Forward);
        var u = AxisVector(Up);
        if (MathF.Abs(Vector3.Dot(f, u)) > 0.5f)
        {
            throw new ArgumentException(UiText.ImportAxisConflict);
        }

        var r = Vector3.Cross(f, u);
        // Rows of B are the file vectors that become X, Y, Z; v * transpose(B) = (v·r, v·u, v·-f).
        var b = new Matrix4x4(
            r.X, r.Y, r.Z, 0,
            u.X, u.Y, u.Z, 0,
            -f.X, -f.Y, -f.Z, 0,
            0, 0, 0, 1);
        return Matrix4x4.Transpose(b);
    }
}

/// <summary>Named import presets (操作项预设), stored in %LOCALAPPDATA%\PolyView\import_presets.json.</summary>
public static class ImportPresets
{
    static string FilePath => Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "import_presets.json");

    public static Dictionary<string, ImportOptions> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<Dictionary<string, ImportOptions>>(File.ReadAllText(FilePath)) ?? [];
            }
        }
        catch (Exception ex)
        {
            App.LogException("ImportPresets.Load", ex);
        }

        return [];
    }

    public static void Save(Dictionary<string, ImportOptions> presets)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(presets, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            App.LogException("ImportPresets.Save", ex);
        }
    }
}
