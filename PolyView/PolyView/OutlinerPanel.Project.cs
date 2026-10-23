using PolyView.Project;

namespace PolyView;

/// <summary>Project files: the outliner owns the collection tree, per-row render toggles and expanded rows.</summary>
public partial class OutlinerPanel
{
    public void CaptureProject(ProjectDocument doc)
    {
        SyncMembership();
        doc.SceneCollection = ToNode(Root);
        // Old readers: flags of the first collection.
        if (Root.Children.Count > 0)
        {
            var first = Root.Children[0];
            doc.Collection.Name = first.Name;
            doc.Collection.Included = first.Included;
            doc.Collection.Visible = first.Visible;
            doc.Collection.Renderable = first.Renderable;
            doc.Collection.Expanded = first.Expanded;
        }

        var expanded = AllItems().Where(i => i.ObjectId >= 0 && i.Expanded).Select(i => (i.ObjectId, i.NodeIndex)).ToHashSet();
        foreach (var o in doc.Objects)
        {
            if (_ownVisible.TryGetValue(o.Id, out bool own))
            {
                o.Visible = own;
            }

            o.RenderOffNodes = [.. _renderOff.Where(k => k.Item1 == o.Id).Select(k => k.Item2).Order()];
            o.ExpandedNodes = [.. expanded.Where(k => k.Item1 == o.Id).Select(k => k.Item2).Order()];
        }
    }

    static ProjectCollectionNode ToNode(SceneCollection c) => new()
    {
        Name = c.Name,
        Included = c.Included,
        Visible = c.Visible,
        Renderable = c.Renderable,
        Expanded = c.Expanded,
        Objects = [.. c.Objects],
        Children = [.. c.Children.Select(ToNode)],
    };

    static SceneCollection FromNode(ProjectCollectionNode n, IReadOnlyDictionary<int, int> idMap)
    {
        var c = new SceneCollection
        {
            Name = n.Name,
            Included = n.Included,
            Visible = n.Visible,
            Renderable = n.Renderable,
            Expanded = n.Expanded,
        };
        foreach (int old in n.Objects)
        {
            if (idMap.TryGetValue(old, out int id))
            {
                c.Objects.Add(id);
            }
        }

        foreach (var child in n.Children)
        {
            c.AddChild(FromNode(child, idMap));
        }

        return c;
    }

    /// <summary>Call after ModelViewport.RestoreProject with its old-id -> new-id map.</summary>
    public void RestoreProject(ProjectDocument doc, IReadOnlyDictionary<int, int> idMap)
    {
        _ownVisible.Clear();
        _renderOff.Clear();
        _active = null;
        if (doc.SceneCollection != null)
        {
            Root = FromNode(doc.SceneCollection, idMap);
            Root.Name = "Scene Collection";
        }
        else
        {
            // Old project: one "Collection" holding every object.
            Root = new SceneCollection { Name = "Scene Collection" };
            var c = new SceneCollection
            {
                Name = string.IsNullOrWhiteSpace(doc.Collection.Name) ? "Collection" : doc.Collection.Name,
                Included = doc.Collection.Included,
                Visible = doc.Collection.Visible,
                Renderable = doc.Collection.Renderable,
                Expanded = doc.Collection.Expanded,
            };
            Root.AddChild(c);
            foreach (var o in doc.Objects)
            {
                if (idMap.TryGetValue(o.Id, out int id))
                {
                    c.Objects.Add(id);
                }
            }
        }

        var expanded = new HashSet<(int, int)>();
        foreach (var o in doc.Objects)
        {
            if (!idMap.TryGetValue(o.Id, out int id))
            {
                continue;
            }

            _ownVisible[id] = o.Visible;
            foreach (int n in o.RenderOffNodes)
            {
                _renderOff.Add((id, n));
            }

            foreach (int n in o.ExpandedNodes)
            {
                expanded.Add((id, n));
            }
        }

        Rebuild();
        foreach (var item in AllItems())
        {
            if (item.Collection == null && item.ObjectId >= 0)
            {
                item.SetExpandedSilently(expanded.Contains((item.ObjectId, item.NodeIndex)));
            }
        }

        ApplyCollection();
        Flatten();
        SyncFromViewport();
    }
}
