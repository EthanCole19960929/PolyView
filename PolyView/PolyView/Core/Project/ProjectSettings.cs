using System.IO;
using System.Text.Json;

namespace PolyView.Project;

/// <summary>Small per-user settings in %LOCALAPPDATA%\PolyView\project-settings.json.</summary>
public sealed class ProjectSettings
{
    public string? LastProjectFolder { get; set; }
    public List<string> RecentProjects { get; set; } = [];

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PolyView", "project-settings.json");

    public static ProjectSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<ProjectSettings>(File.ReadAllText(FilePath)) ?? new();
            }
        }
        catch (Exception ex)
        {
            App.LogException("ProjectSettings.Load", ex);
        }

        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            App.LogException("ProjectSettings.Save", ex);
        }
    }

    /// <summary>Default folder for Save / Save As: the last folder the user saved to, else the repo's Export folder next to the app.</summary>
    public string DefaultProjectFolder()
    {
        if (!string.IsNullOrEmpty(LastProjectFolder) && Directory.Exists(LastProjectFolder))
        {
            return LastProjectFolder;
        }

        // Walk up from bin\Debug\... to find the project root that holds the Export folder.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var export = Path.Combine(dir.FullName, "Export");
            if (Directory.Exists(export) || File.Exists(Path.Combine(dir.FullName, "README.md")) && Directory.Exists(Path.Combine(dir.FullName, "docs")))
            {
                Directory.CreateDirectory(export);
                return export;
            }

            dir = dir.Parent;
        }

        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PolyView");
        Directory.CreateDirectory(fallback);
        return fallback;
    }
}
