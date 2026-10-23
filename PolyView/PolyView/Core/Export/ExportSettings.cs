using System.IO;
using System.Text.Json;

namespace PolyView.Export;

/// <summary>Remembers the last export folder and format (%LOCALAPPDATA%\PolyView\export.json, next to settings.json).</summary>
public sealed class ExportSettings
{
    public string? LastFolder { get; set; }
    public string LastFormat { get; set; } = "fbx";

    /// <summary>Default export folder: &lt;repo&gt;\Export when running from the source tree, otherwise Documents\PolyView\Export.</summary>
    public static string DefaultFolder
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var d = dir; d != null; d = d.Parent)
            {
                if (Directory.Exists(Path.Combine(d.FullName, ".git")))
                {
                    return Path.Combine(d.FullName, "Export");
                }
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PolyView", "Export");
        }
    }

    static string FilePath => Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "export.json");

    public static ExportSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<ExportSettings>(File.ReadAllText(FilePath)) ?? new ExportSettings();
            }
        }
        catch (Exception ex)
        {
            App.LogException("ExportSettings.Load", ex);
        }

        return new ExportSettings();
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
            App.LogException("ExportSettings.Save", ex);
        }
    }

    /// <summary>Folder for the save dialog: the last one used if it still exists, else the default (created).</summary>
    public string InitialFolder()
    {
        if (!string.IsNullOrEmpty(LastFolder) && Directory.Exists(LastFolder))
        {
            return LastFolder;
        }

        var folder = DefaultFolder;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            App.LogException("ExportSettings.InitialFolder", ex);
        }

        return folder;
    }
}
