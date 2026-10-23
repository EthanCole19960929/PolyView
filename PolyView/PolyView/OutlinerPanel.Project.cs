using PolyView.Project;

namespace PolyView;

/// <summary>Project files: the outliner owns collection flags, per-row render toggles and expanded rows.</summary>
public partial class OutlinerPanel
{
    public void CaptureProject(ProjectDocument doc)
    {
        doc.Collection.Included = _collIncluded;
        doc.Collection.Visible = _collVisible;
        doc.Collection.Renderable = _collRenderable;
        doc.Collection.Expanded = _collection?.Expanded ?? true;
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

    /// <summary>Call after ModelViewport.RestoreProject with its old-id -> new-id map.</summary>
    public void RestoreProject(ProjectDocument doc, IReadOnlyDictionary<int, int> idMap)
    {
        _ownVisible.Clear();
        _renderOff.Clear();
        _collIncluded = doc.Collection.Included;
        _collVisible = doc.Collection.Visible;
        _collRenderable = doc.Collection.Renderable;
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
            if (item.ObjectId >= 0)
            {
                item.SetExpandedSilently(expanded.Contains((item.ObjectId, item.NodeIndex)));
            }
            else if (item.Kind == OutlinerKind.Collection)
            {
                item.SetExpandedSilently(doc.Collection.Expanded);
            }
        }

        ApplyCollection();
        Flatten();
        SyncFromViewport();
    }
}
