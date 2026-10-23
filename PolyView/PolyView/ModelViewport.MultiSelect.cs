using System.Numerics;

namespace PolyView;

/// <summary>Multi-selection helpers: G / R / S move every selected object with the active one; selection-set change event for the outliner.</summary>
public sealed partial class ModelViewport
{
    /// <summary>Raised when the set of selected objects changes (box select, Shift / Ctrl click, SetSelection).</summary>
    public event EventHandler? SelectedIdsChanged;

    readonly List<(SceneObject Obj, ObjectTransform Start)> _xformOthers = [];
    Matrix4x4 _xformActiveStartWorldInv = Matrix4x4.Identity;

    void RaiseSelectedIdsChanged() => SelectedIdsChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Called when a G / R / S / gizmo transform starts on the active object.</summary>
    void BeginMultiTransform(SceneObject active)
    {
        _xformOthers.Clear();
        Matrix4x4.Invert(active.World, out _xformActiveStartWorldInv);
        foreach (var id in SelectedIds)
        {
            if (id != active.Id && TryGetObject(id, out var o))
            {
                _xformOthers.Add((o, o.Transform));
            }
        }
    }

    /// <summary>Apply the active object's change (relative to its start) to the other selected objects.</summary>
    void UpdateMultiTransform(SceneObject active, bool commit)
    {
        if (_xformOthers.Count == 0)
        {
            return;
        }

        var delta = _xformActiveStartWorldInv * active.World;
        foreach (var (o, start) in _xformOthers)
        {
            var m = start.Matrix * delta;
            if (Matrix4x4.Decompose(m, out var s, out var r, out var p))
            {
                ApplyTransform(o, ApplyLocks(o.Id, start, new ObjectTransform { Position = p, Rotation = Quaternion.Normalize(r), Scale = s }), commit);
            }
        }
    }

    void CommitMultiTransform()
    {
        foreach (var (o, start) in _xformOthers)
        {
            if (!o.Transform.Equals(start))
            {
                PushUndo(o.Id, start, o.Transform);
            }

            ApplyTransform(o, o.Transform, commit: true);
        }

        _xformOthers.Clear();
    }

    void CancelMultiTransform()
    {
        foreach (var (o, start) in _xformOthers)
        {
            ApplyTransform(o, start, commit: true);
        }

        _xformOthers.Clear();
    }
}
