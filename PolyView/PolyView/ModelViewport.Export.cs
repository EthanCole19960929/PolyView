using PolyView.Export;

namespace PolyView;

/// <summary>Scene access for File > Export and the AI / MCP export API.</summary>
public partial class ModelViewport
{
    /// <summary>
    /// Objects to export: the selected object when selectionOnly is true and something is selected, otherwise every object.
    /// Hidden objects are filtered by SceneExporter (ExportOptions.SkipHidden).
    /// </summary>
    public IReadOnlyList<SceneObject> GetExportObjects(bool selectionOnly)
    {
        if (selectionOnly && TryGetObject(_selectedId, out var selected))
        {
            return [selected];
        }

        return _objects.ToArray();
    }

    /// <summary>Export the scene (or the selection) to path. format: fbx / obj / gltf / glb / stl / ply / dae, or null to use the extension.</summary>
    public ExportResult ExportScene(string path, string? format = null, bool selectionOnly = false, ExportOptions? options = null)
    {
        options ??= new ExportOptions { SelectionOnly = selectionOnly };
        return SceneExporter.Export(GetExportObjects(selectionOnly || options.SelectionOnly), path, format, options);
    }
}
