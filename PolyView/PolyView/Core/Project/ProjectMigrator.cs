using System.Text.Json;

namespace PolyView.Project;

/// <summary>Upgrades older project.json versions to the current one before deserializing.</summary>
public static class ProjectMigrator
{
    public static string Migrate(JsonElement root)
    {
        int version = root.TryGetProperty("version", out var v) && v.TryGetInt32(out int n) ? n : 1;
        if (version > ProjectDocument.CurrentVersion)
        {
            throw new ProjectVersionException(version);
        }

        // Version 1 is current: nothing to do. Future: if (version == 1) { ...rewrite to 2... }
        return root.GetRawText();
    }
}

public sealed class ProjectVersionException(int version)
    : Exception($"Project file version {version} is newer than this PolyView supports ({ProjectDocument.CurrentVersion}).")
{
    public int Version { get; } = version;
}
