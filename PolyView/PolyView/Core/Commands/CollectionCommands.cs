using System.ComponentModel;

namespace PolyView.Commands;

/// <summary>Blender-style collections (outliner). Collections are addressed by name (unique) or numeric id; "Scene Collection" is the root.</summary>
static class CollectionCommands
{
    static SceneCollection Find(MainWindow w, string? name) =>
        string.IsNullOrWhiteSpace(name) ? w.Outliner.Root
        : w.Outliner.FindCollection(name) ?? throw new ArgumentException($"no collection '{name}'");

    [PvCommand("collection.new", "collection", "新建集合", "Create a collection (optionally inside parent, optionally moving objects into it)")]
    static object New(MainWindow w, [Description("Name (default Collection, made unique)")] string? name = null,
        [Description("Parent collection name or id (default Scene Collection)")] string? parent = null,
        [Description("Object ids to move into it")] int[]? object_ids = null)
    {
        var c = w.Outliner.NewCollection(Find(w, parent), object_ids, name);
        return new { ok = true, id = c.Id, name = c.Name };
    }

    [PvCommand("collection.move_objects", "collection", "移动到集合（M）", "Move objects into a collection")]
    static object MoveObjects(MainWindow w, [Description("Object ids")] int[] object_ids, [Description("Target collection name or id")] string collection)
    {
        var c = Find(w, collection);
        w.Outliner.MoveObjects(object_ids, c);
        return new { ok = true, collection = c.Name };
    }

    [PvCommand("collection.link_objects", "collection", "链接到集合（Shift+M）", "Also add objects to a collection (they stay in their current ones)")]
    static object LinkObjects(MainWindow w, [Description("Object ids")] int[] object_ids, [Description("Target collection name or id")] string collection)
    {
        var c = Find(w, collection);
        w.Outliner.LinkObjects(object_ids, c);
        return new { ok = true, collection = c.Name };
    }

    [PvCommand("collection.move", "collection", "移动集合", "Nest a collection inside another collection")]
    static object Move(MainWindow w, [Description("Collection name or id")] string collection, [Description("New parent name or id")] string parent)
        => new { ok = w.Outliner.MoveCollection(Find(w, collection), Find(w, parent)) };

    [PvCommand("collection.rename", "collection", "重命名集合", "Rename a collection")]
    static object Rename(MainWindow w, [Description("Collection name or id")] string collection, [Description("New name")] string name)
    {
        var c = Find(w, collection);
        bool ok = w.Outliner.RenameCollection(c, name);
        return new { ok, name = c.Name };
    }

    [PvCommand("collection.delete", "collection", "删除集合", "Delete a collection; its objects and child collections move to the parent (nothing is deleted)", Destructive = true)]
    static object Delete(MainWindow w, [Description("Collection name or id")] string collection)
        => new { ok = w.Outliner.DeleteCollection(Find(w, collection)) };

    [PvCommand("collection.set_visible", "collection", "集合显示/排除", "Hide/show a collection (eye) and/or exclude it from the view layer (checkbox)")]
    static object SetVisible(MainWindow w, [Description("Collection name or id")] string collection,
        [Description("Eye toggle")] bool? visible = null, [Description("Checkbox: false = excluded")] bool? included = null)
    {
        var c = Find(w, collection);
        w.Outliner.SetCollectionFlags(c, visible, included);
        return new { ok = true, name = c.Name, visible = c.Visible, included = c.Included };
    }
}
