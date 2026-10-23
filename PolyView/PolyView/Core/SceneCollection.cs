namespace PolyView;

/// <summary>
/// Blender-style collection: a named group of objects and child collections.
/// The root is the "Scene Collection" (cannot be renamed, deleted or hidden). Each object lives in exactly one collection.
/// </summary>
public sealed class SceneCollection
{
    static int _nextId = 1;

    public int Id { get; } = _nextId++;
    public string Name { get; set; } = "Collection";
    public bool Included { get; set; } = true;
    public bool Visible { get; set; } = true;
    public bool Renderable { get; set; } = true;
    public bool Expanded { get; set; } = true;
    public SceneCollection? Parent { get; private set; }
    public List<SceneCollection> Children { get; } = [];
    public List<int> Objects { get; } = [];

    public bool IsRoot => Parent == null;

    /// <summary>Shown in the viewport: own and every ancestor's include + eye flags are on.</summary>
    public bool EffectiveShown => (IsRoot || (Included && Visible)) && (Parent?.EffectiveShown ?? true);

    public bool EffectiveExcluded => (!IsRoot && !Included) || (Parent?.EffectiveExcluded ?? false);

    public bool EffectiveRenderOff => (!IsRoot && !Renderable) || (Parent?.EffectiveRenderOff ?? false);

    public void AddChild(SceneCollection child, int index = -1)
    {
        child.Parent?.Children.Remove(child);
        child.Parent = this;
        if (index < 0 || index > Children.Count)
        {
            Children.Add(child);
        }
        else
        {
            Children.Insert(index, child);
        }
    }

    public bool IsAncestorOf(SceneCollection other)
    {
        for (var p = other.Parent; p != null; p = p.Parent)
        {
            if (p == this)
            {
                return true;
            }
        }

        return false;
    }

    public IEnumerable<SceneCollection> SelfAndDescendants()
    {
        yield return this;
        foreach (var c in Children)
        {
            foreach (var d in c.SelfAndDescendants())
            {
                yield return d;
            }
        }
    }

    public SceneCollection? FindOwner(int objectId) => SelfAndDescendants().FirstOrDefault(c => c.Objects.Contains(objectId));

    public SceneCollection? Find(int id) => SelfAndDescendants().FirstOrDefault(c => c.Id == id);

    public SceneCollection? FindByName(string name) =>
        SelfAndDescendants().FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Blender naming: "Collection", "Collection.001", ...</summary>
    public string UniqueName(string baseName)
    {
        var names = SelfAndDescendants().Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName))
        {
            return baseName;
        }

        for (int i = 1; ; i++)
        {
            var n = $"{baseName}.{i:000}";
            if (!names.Contains(n))
            {
                return n;
            }
        }
    }

    /// <summary>Move an object into this collection (removes it from any other one under the same root).</summary>
    public void MoveObjectHere(SceneCollection root, int objectId)
    {
        foreach (var c in root.SelfAndDescendants())
        {
            c.Objects.Remove(objectId);
        }

        Objects.Add(objectId);
    }

    /// <summary>Delete this collection; its objects and child collections move to the parent.</summary>
    public void DeleteKeepChildren()
    {
        if (Parent is not { } parent)
        {
            return;
        }

        int index = parent.Children.IndexOf(this);
        parent.Children.Remove(this);
        foreach (var c in Children.ToList())
        {
            parent.AddChild(c, index++);
        }

        parent.Objects.AddRange(Objects);
        Objects.Clear();
        Parent = null;
    }
}
