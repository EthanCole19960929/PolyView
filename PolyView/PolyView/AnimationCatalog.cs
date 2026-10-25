using System.IO;
using Silk.NET.Assimp;

namespace PolyView;

public sealed record ModelAnimationSource(int ObjectId, string ModelName, string ModelPath);

public sealed record AnimationClipInfo(string Name, string ClipName, string FilePath, string RelativePath, double? DurationSeconds, int ClipIndex = 0)
{
    public string DurationText => DurationSeconds is { } seconds ? $"{seconds:0.##} s" : "—";
}

public sealed record AnimationScanResult(string Folder, IReadOnlyList<AnimationClipInfo> Clips, IReadOnlyList<string> Errors, int FilesChecked);

/// <summary>Discovers actual animation clips, including animation-only files, without converting meshes or decoding textures.</summary>
public static class AnimationCatalog
{
    static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".fbx", ".glb", ".gltf", ".dae", ".bvh", ".x", ".3ds", ".md5anim", ".smd", ".iqm", ".ms3d", ".lws",
    };

    public static string ResolveModelPath(string? savedPath, string? originalPath, string importedPath, string? projectPath = null)
    {
        string cache = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PolyView", "ProjectCache"))
            + Path.DirectorySeparatorChar;
        foreach (string? candidate in new[] { savedPath, originalPath })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string full = Path.GetFullPath(candidate);
            if (!full.StartsWith(cache, StringComparison.OrdinalIgnoreCase) && Directory.Exists(Path.GetDirectoryName(full))) return full;
        }

        // A relocated portable project should discover animations beside the project, not in the extraction cache.
        if (projectPath != null)
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, Path.GetFileName(originalPath ?? importedPath));
        return savedPath ?? originalPath ?? importedPath;
    }

    public static AnimationScanResult Scan(string modelPath, CancellationToken cancellationToken = default, IProgress<int>? progress = null)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(modelPath))!;
        var clips = new List<AnimationClipInfo>();
        var errors = new List<string>();
        var pending = new Stack<string>();
        pending.Push(folder);
        int checkedFiles = 0;
        using var assimp = Assimp.GetApi();
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] files;
            string[] children;
            try
            {
                files = Directory.GetFiles(directory);
                children = Directory.GetDirectories(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{directory}: {ex.Message}");
                continue;
            }

            foreach (string child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // Do not follow directory junctions/symlinks out of the model folder or into cycles.
                    if ((System.IO.File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{child}: {ex.Message}");
                }
            }

            foreach (string file in files.Order(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Extensions.Contains(Path.GetExtension(file))) continue;
                try
                {
                    ReadClips(assimp, file, folder, clips, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
                {
                    errors.Add($"{Path.GetRelativePath(folder, file)}: {ex.Message}");
                }
                checkedFiles++;
                progress?.Report(checkedFiles);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new AnimationScanResult(folder,
            clips.OrderBy(c => c.RelativePath, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            errors, checkedFiles);
    }

    static unsafe void ReadClips(Assimp assimp, string path, string folder, List<AnimationClipInfo> clips, CancellationToken token)
    {
        var scene = assimp.ImportFile(path, 0);
        if (scene == null) throw new InvalidDataException(assimp.GetErrorStringS());
        try
        {
            for (uint i = 0; i < scene->MNumAnimations; i++)
            {
                token.ThrowIfCancellationRequested();
                var animation = scene->MAnimations[i];
                string clipName = animation->MName.AsString;
                string fileName = Path.GetFileNameWithoutExtension(path);
                string name = scene->MNumAnimations == 1 ? fileName : $"{fileName} / {(string.IsNullOrWhiteSpace(clipName) ? $"Clip {i + 1}" : clipName)}";
                double? duration = animation->MTicksPerSecond > 0 && double.IsFinite(animation->MTicksPerSecond)
                    && double.IsFinite(animation->MDuration) && animation->MDuration >= 0
                    ? animation->MDuration / animation->MTicksPerSecond : null;
                clips.Add(new AnimationClipInfo(name, clipName, path, Path.GetRelativePath(folder, path), duration, (int)i));
            }
        }
        finally
        {
            assimp.ReleaseImport(scene);
        }
    }
}
