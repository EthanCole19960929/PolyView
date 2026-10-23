namespace PolyView;

/// <summary>
/// Undoable delete. A delete step keeps the removed SceneObjects (mesh data, transform, materials keyed by mesh,
/// visibility, hidden nodes), their scene order, collection memberships and the selection, so Ctrl+Z restores them
/// exactly and Ctrl+Y deletes them again. GPU buffers are rebuilt on restore. Records are freed when the entry
/// falls off the undo/redo stacks.
/// </summary>
public sealed partial class ModelViewport
{
    /// <summary>Set by the outliner so delete/restore can keep collection memberships.</summary>
    public Func<SceneCollection>? CollectionRootProvider { get; set; }

    SceneCollection? CollectionRoot => CollectionRootProvider?.Invoke();

    sealed class CustomUndo
    {
        public required Action Undo;
        public required Action Redo;
        public Action? Drop;
    }

    const int CustomUndoBase = int.MinValue / 2;
    readonly Dictionary<int, CustomUndo> _customUndo = [];
    int _customUndoSeq;

    static bool IsCustomEntry(int id) => id <= CustomUndoBase;

    /// <summary>Push an arbitrary undo step (joins the current undo group).</summary>
    public void PushCustomUndo(Action undo, Action redo, Action? drop = null)
    {
        int key = ++_customUndoSeq;
        _customUndo[key] = new CustomUndo { Undo = undo, Redo = redo, Drop = drop };
        PushUndo(CustomUndoBase - key, ObjectTransform.Identity, ObjectTransform.Identity);
    }

    bool ApplyCustomEntry(int id, bool useOld)
    {
        if (!_customUndo.TryGetValue(CustomUndoBase - id, out var c))
        {
            return false;
        }

        if (useOld) c.Undo(); else c.Redo();
        return true;
    }

    /// <summary>Release whatever an undo entry keeps alive (called when it leaves the stacks for good).</summary>
    void DropUndoEntry(int id)
    {
        if (IsCustomEntry(id))
        {
            if (_customUndo.Remove(CustomUndoBase - id, out var c))
            {
                c.Drop?.Invoke();
            }
        }
        else if (id < 0)
        {
            _removedByUndo.Remove(-id - 1);
        }
    }

    void DropUndoEntries(List<(int Id, ObjectTransform Old, ObjectTransform New, int Group)> list)
    {
        foreach (var e in list)
        {
            DropUndoEntry(e.Id);
        }

        list.Clear();
    }

    sealed record DeletedObject(SceneObject Obj, int Index, List<(SceneCollection Col, int Index)> Members);

    /// <summary>Delete objects as one undo step. Returns the number deleted.</summary>
    public int DeleteObjects(IEnumerable<int> ids)
    {
        var set = ids.ToHashSet();
        var recs = new List<DeletedObject>();
        for (int i = 0; i < _objects.Count; i++)
        {
            var o = _objects[i];
            if (!set.Contains(o.Id))
            {
                continue;
            }

            var members = new List<(SceneCollection, int)>();
            if (CollectionRoot != null)
            {
                foreach (var c in CollectionRoot.SelfAndDescendants())
                {
                    for (int k = 0; k < c.Objects.Count; k++)
                    {
                        if (c.Objects[k] == o.Id) members.Add((c, k));
                    }
                }
            }

            recs.Add(new DeletedObject(o, i, members));
        }

        if (recs.Count == 0)
        {
            return 0;
        }

        var selIds = new List<int>(_extraSelected);
        if (_selectedId >= 0) selIds.Add(_selectedId);
        int active = _selectedId;

        void DoDelete()
        {
            foreach (var r in recs)
            {
                RemoveObject(r.Obj.Id);
            }
        }

        void DoRestore()
        {
            // Collections first (ascending index per collection), so the outliner's sync doesn't refile them.
            if (CollectionRoot != null)
            {
                var alive = CollectionRoot.SelfAndDescendants().ToHashSet();
                foreach (var (col, idx, oid) in recs.SelectMany(r => r.Members.Select(m => (m.Col, m.Index, r.Obj.Id))).OrderBy(m => m.Index))
                {
                    if (!alive.Contains(col)) continue;
                    col.Objects.Remove(oid);
                    col.Objects.Insert(Math.Min(idx, col.Objects.Count), oid);
                }
            }

            foreach (var r in recs.OrderBy(r => r.Index))
            {
                var o = r.Obj;
                if (_objects.Exists(x => x.Id == o.Id)) continue;
                _objects.Insert(Math.Min(r.Index, _objects.Count), o);
                if (o.Mesh.Vertices.Length > 0)
                {
                    _renderer?.AddMesh(o.Id, o.Mesh);
                }

                _renderer?.SetWorld(o.Id, o.World);
                _renderer?.SetVisible(o.Id, o.Visible);
                foreach (int n in o.HiddenNodes.ToList())
                {
                    o.HiddenNodes.Remove(n);
                    SetNodeVisible(o.Id, n, false);
                }
            }

            UpdateSceneBounds();
            Invalidate();
            SceneChanged?.Invoke(this, EventArgs.Empty);
            ViewChanged?.Invoke(this, EventArgs.Empty);
            SetSelection(selIds, active);
        }

        DoDelete();
        PushCustomUndo(DoRestore, DoDelete, () => recs.Clear());
        return recs.Count;
    }
}