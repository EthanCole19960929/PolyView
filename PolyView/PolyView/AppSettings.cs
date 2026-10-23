using System.IO;
using System.Text.Json;

namespace PolyView;

/// <summary>User preferences persisted to %LOCALAPPDATA%\PolyView\settings.json.</summary>
public sealed class AppSettings
{
    public string Language { get; set; } = "zh";

    /// <summary>Start the built-in MCP server (http://127.0.0.1:47800/mcp) with the app.</summary>
    public bool McpEnabled { get; set; } = true;

    /// <summary>Client ids the user opted out of automatic MCP connection (default: none, all auto).</summary>
    public List<string> McpAutoConnectOff { get; set; } = [];

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PolyView", "settings.json");

    static AppSettings? _current;
    public static AppSettings Current => _current ??= Load();

    static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            App.LogException("AppSettings.Load", ex);
        }
        return new AppSettings();
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
            App.LogException("AppSettings.Save", ex);
        }
    }

    /// <summary>Apply the stored language to UiText.</summary>
    public static void ApplyStoredLanguage() => UiText.Chinese = Current.Language != "en";
}
