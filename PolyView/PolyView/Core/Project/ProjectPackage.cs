using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace PolyView.Project;

/// <summary>Reads / writes the .pvproj zip: project.json, thumbnail.png, assets/&lt;sha256&gt;/files.</summary>
public static class ProjectPackage
{
    public const string Extension = ".pvproj";
    public const string BackupExtension = ".pvproj1";
    const string ManifestEntry = "project.json";
    const string ThumbnailEntry = "thumbnail.png";

    static readonly HashSet<string> SideExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mtl", ".bin", ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".dds", ".ktx2", ".webp", ".tif", ".tiff",
    };

    /// <summary>.pvproj, or its .pvproj1 backup (same format).</summary>
    public static bool IsProjectFile(string path) =>
        string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetExtension(path), BackupExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Turns whatever the dialog returned into "&lt;folder&gt;\&lt;name&gt;.pvproj" (never a folder, never a .pvproj1).</summary>
    public static string NormalizeSavePath(string path, string fallbackName)
    {
        path = Path.GetFullPath(path.Trim());
        if (Directory.Exists(path) || path.EndsWith(Path.DirectorySeparatorChar))
        {
            path = Path.Combine(path, fallbackName);
        }

        return Path.ChangeExtension(path, Extension);
    }

    public static string Sha256OfFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Main model file plus, for formats that reference other files (OBJ / glTF), the side files next to it.</summary>
    public static List<string> CollectFiles(string mainPath)
    {
        var list = new List<string> { mainPath };
        var ext = Path.GetExtension(mainPath).ToLowerInvariant();
        if (ext is ".obj" or ".gltf" or ".fbx" or ".dae" or ".3ds" or ".json")
        {
            var dir = Path.GetDirectoryName(mainPath)!;
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                if (!string.Equals(f, mainPath, StringComparison.OrdinalIgnoreCase) && SideExtensions.Contains(Path.GetExtension(f)))
                {
                    list.Add(f);
                }
            }
        }

        return list;
    }

    /// <summary>Writes the package to a temp file, keeps the old file as .pvproj1, then moves the temp file into place.</summary>
    public static void Write(string path, ProjectDocument doc, byte[]? thumbnailPng, ProjectSaveOptions options)
    {
        path = Path.GetFullPath(path);
        if (Directory.Exists(path))
        {
            throw new IOException("A folder with this name already exists: " + path);
        }

        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, "~" + Path.GetFileNameWithoutExtension(path) + "." + Environment.ProcessId + ".saving");
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                var manifest = zip.CreateEntry(ManifestEntry, CompressionLevel.Optimal);
                using (var w = new StreamWriter(manifest.Open(), new System.Text.UTF8Encoding(false)))
                {
                    w.Write(ProjectSerializer.Serialize(doc));
                }

                if (thumbnailPng != null)
                {
                    var thumb = zip.CreateEntry(ThumbnailEntry, CompressionLevel.NoCompression);
                    using var s = thumb.Open();
                    s.Write(thumbnailPng);
                }

                foreach (var asset in doc.Assets.Where(a => a.Embedded && a.OriginalPath != null))
                {
                    var sourceDir = Path.GetDirectoryName(asset.OriginalPath!)!;
                    foreach (var name in asset.Files)
                    {
                        zip.CreateEntryFromFile(Path.Combine(sourceDir, name), $"assets/{asset.Id}/{name}", CompressionLevel.Optimal);
                    }
                }
            }

            // Make sure what we wrote reads back before it replaces anything.
            using (var check = ZipFile.OpenRead(temp))
            {
                if (check.GetEntry(ManifestEntry) == null)
                {
                    throw new IOException("The saved package is incomplete.");
                }
            }

            if (File.Exists(path))
            {
                // Overwriting: keep the previous version as <name>.pvproj1, then move the new file into place.
                if (options.KeepBackup)
                {
                    File.Copy(path, Path.ChangeExtension(path, BackupExtension), overwrite: true);
                }

                File.Move(temp, path, overwrite: true);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>Reads project.json and extracts embedded assets to a cache folder. Returns assetId -> main file path on disk.</summary>
    public static (ProjectDocument Doc, Dictionary<string, string> AssetPaths, List<string> Missing) Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var manifest = zip.GetEntry(ManifestEntry) ?? throw new InvalidDataException("project.json is missing.");
        string json;
        using (var r = new StreamReader(manifest.Open()))
        {
            json = r.ReadToEnd();
        }

        var doc = ProjectSerializer.Deserialize(json);
        var paths = new Dictionary<string, string>();
        var missing = new List<string>();
        var cacheRoot = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PolyView", "ProjectCache"));
        foreach (var asset in doc.Assets)
        {
            if (!IsSafeSegment(asset.Id) || !IsSafeSegment(asset.MainFile))
            {
                throw new InvalidDataException("Invalid asset name in project.json.");
            }

            string? found = null;
            if (asset.Embedded)
            {
                var target = Path.Combine(cacheRoot, asset.Id);
                var prefix = $"assets/{asset.Id}/";
                foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal) && e.Name.Length > 0))
                {
                    var dest = Path.GetFullPath(Path.Combine(target, entry.FullName[prefix.Length..]));
                    // Zip-slip guard: every extracted file must stay inside the asset's cache folder.
                    if (!dest.StartsWith(Path.GetFullPath(target) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("Unsafe path in project package: " + entry.FullName);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    if (!File.Exists(dest) || new FileInfo(dest).Length != entry.Length)
                    {
                        entry.ExtractToFile(dest, overwrite: true);
                    }
                }

                var main = Path.Combine(target, asset.MainFile);
                if (File.Exists(main))
                {
                    found = main;
                }
            }

            if (found == null && asset.OriginalPath != null && File.Exists(asset.OriginalPath))
            {
                found = asset.OriginalPath;
            }

            if (found != null)
            {
                paths[asset.Id] = found;
            }
            else
            {
                missing.Add(asset.OriginalPath ?? asset.MainFile);
            }
        }

        return (doc, paths, missing);
    }

    static bool IsSafeSegment(string s) =>
        s.Length > 0 && s.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && s != "." && s != "..";
}
