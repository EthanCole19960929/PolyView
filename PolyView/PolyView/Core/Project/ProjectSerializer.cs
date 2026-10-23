using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PolyView.Project;

public static class ProjectSerializer
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Serialize(ProjectDocument doc) => JsonSerializer.Serialize(doc, Options);

    public static ProjectDocument Deserialize(string json)
    {
        using var raw = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var migrated = ProjectMigrator.Migrate(raw.RootElement);
        var doc = JsonSerializer.Deserialize<ProjectDocument>(migrated, Options)
            ?? throw new InvalidDataException("project.json is empty.");
        if (doc.Format != ProjectDocument.FormatName)
        {
            throw new InvalidDataException("Not a PolyView project file.");
        }

        return doc;
    }
}
