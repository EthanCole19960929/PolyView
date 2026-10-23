using System.Windows.Input;

namespace PolyView;

/// <summary>Object / data-block helpers for the outliner (Blender object-data split) and the M key.</summary>
public partial class ModelViewport
{
    /// <summary>M (false) / Shift+M (true = link) with an object selected: the outliner shows the Move / Link to Collection menu.</summary>
    public event EventHandler<bool>? MoveToCollectionRequested;

    bool HandleCollectionKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.M && modifiers is ModifierKeys.None or ModifierKeys.Shift && _selectedId >= 0 && MoveToCollectionRequested != null)
        {
            MoveToCollectionRequested(this, modifiers == ModifierKeys.Shift);
            return true;
        }

        return false;
    }

    /// <summary>Name of the mesh data-block the object uses (shared by linked duplicates).</summary>
    public string GetMeshDataName(int id) =>
        TryGetObject(id, out var o) ? (string.IsNullOrWhiteSpace(o.Mesh.Name) ? o.Name : o.Mesh.Name) : "";

    /// <summary>Material slots of the object's mesh data (one per texture / part group, at least one).</summary>
    public int GetMaterialCount(int id)
    {
        if (!TryGetObject(id, out var o))
        {
            return 0;
        }

        int textures = o.Mesh.Parts.Select(p => p.TextureIndex).Distinct().Count();
        return Math.Max(1, textures);
    }

    /// <summary>Objects sharing the same mesh data-block (Blender "users"; Alt+D linked duplicates).</summary>
    public int GetMeshUsers(int id) => TryGetObject(id, out var o) ? _objects.Count(x => ReferenceEquals(x.Mesh, o.Mesh)) : 0;

    public bool RenameObject(int id, string name)
    {
        if (!TryGetObject(id, out var o) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        o.Name = name.Trim();
        SceneChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
