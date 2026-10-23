using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PolyView;

/// <summary>
/// Blender-style box select in the 3D viewport. Left-drag from empty space (or B, then drag) draws a faint box with a
/// white dashed border; plain = replace, Shift = extend, Ctrl = subtract. Shift+click toggles one object.
/// The active object stays ModelViewport.SelectedId (what the outliner, gizmo and transforms use); the other selected
/// objects are kept here and highlighted by SceneRenderer.ExtraSelectedIds. Selection changes are not undoable
/// (as everywhere else in PolyView).
/// </summary>
public sealed partial class ModelViewport
{
    const double BoxDragThreshold = 4;

    readonly HashSet<int> _extraSelected = [];
    bool _settingSelection;
    bool _boxHooked;
    bool _boxActive;
    bool _boxPending;
    Point _boxStart;
    BoxSelectMode _boxMode;
    Canvas? _boxCanvas;
    Rectangle? _boxShade;
    Rectangle? _boxDash;

    static ModelViewport()
    {
        EventManager.RegisterClassHandler(typeof(ModelViewport), PreviewMouseDownEvent, new MouseButtonEventHandler((s, e) => ((ModelViewport)s).BoxPreviewMouseDown(e)));
        EventManager.RegisterClassHandler(typeof(ModelViewport), PreviewMouseMoveEvent, new MouseEventHandler((s, e) => ((ModelViewport)s).BoxPreviewMouseMove(e)));
        EventManager.RegisterClassHandler(typeof(ModelViewport), PreviewMouseUpEvent, new MouseButtonEventHandler((s, e) => ((ModelViewport)s).BoxPreviewMouseUp(e)));
        EventManager.RegisterClassHandler(typeof(ModelViewport), PreviewKeyDownEvent, new KeyEventHandler((s, e) => ((ModelViewport)s).BoxPreviewKeyDown(e)));
        EventManager.RegisterClassHandler(typeof(ModelViewport), LostMouseCaptureEvent, new MouseEventHandler((s, _) => ((ModelViewport)s).CancelBox()));
        EventManager.RegisterClassHandler(typeof(ModelViewport), LoadedEvent, new RoutedEventHandler((s, _) => ((ModelViewport)s).EnsureBoxHooks()));
    }

    void EnsureBoxHooks()
    {
        if (_boxHooked)
        {
            return;
        }

        _boxHooked = true;
        EnsureNavButtons();
        // Any other selection change (outliner click, Esc, delete, project load) drops the extra selection.
        SelectionChanged += (_, _) =>
        {
            if (!_settingSelection)
            {
                ClearExtraSelection();
            }
        };
    }

    /// <summary>All selected object ids: the active one first, then the others (scene order).</summary>
    public IReadOnlyList<int> SelectedIds
    {
        get
        {
            var list = new List<int>();
            if (TryGetObject(_selectedId, out _))
            {
                list.Add(_selectedId);
            }

            foreach (var o in _objects)
            {
                if (o.Id != _selectedId && _extraSelected.Contains(o.Id))
                {
                    list.Add(o.Id);
                }
            }

            return list;
        }
    }

    /// <summary>Set the whole selection. active must be one of ids (else the first id is used); empty clears.</summary>
    public void SetSelection(IEnumerable<int> ids, int active)
    {
        EnsureBoxHooks();
        var valid = ids.Where(id => TryGetObject(id, out _)).Distinct().ToList();
        if (!valid.Contains(active))
        {
            active = valid.Count > 0 ? valid[0] : -1;
        }

        _settingSelection = true;
        try
        {
            Select(active);
        }
        finally
        {
            _settingSelection = false;
        }

        _extraSelected.Clear();
        foreach (var id in valid)
        {
            if (id != active)
            {
                _extraSelected.Add(id);
            }
        }

        PushExtraSelection();
    }

    void ClearExtraSelection()
    {
        if (_extraSelected.Count == 0)
        {
            return;
        }

        _extraSelected.Clear();
        PushExtraSelection();
    }

    void PushExtraSelection()
    {
        if (_renderer != null)
        {
            _renderer.ExtraSelectedIds = _extraSelected;
        }

        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Objects (visible, in scene order) inside a viewport rectangle in DIPs (origin top-left).</summary>
    public List<int> ObjectsInBox(Rect box)
    {
        var hits = new List<int>();
        if (_objects.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0 || box.IsEmpty)
        {
            return hits;
        }

        float w = (float)ActualWidth, h = (float)ActualHeight;
        SceneRenderer.GetViewProjection(_camera, _scale, SceneRadius, w / h, out var view, out var projection);
        var viewProjection = view * projection;
        var sceneScale = Matrix4x4.CreateScale(_scale);
        var min = new Vector2((float)box.Left, (float)box.Top);
        var max = new Vector2((float)box.Right, (float)box.Bottom);
        foreach (var o in _objects)
        {
            if (o.Visible && BoxSelection.Hits(o, sceneScale, viewProjection, w, h, min, max))
            {
                hits.Add(o.Id);
            }
        }

        return hits;
    }

    /// <summary>Box select (also used by the select.box command). Returns the new selection, active first.</summary>
    public IReadOnlyList<int> BoxSelect(Rect box, BoxSelectMode mode)
    {
        var hits = ObjectsInBox(box);
        var (selected, active) = BoxSelection.Combine(SelectedIds, _selectedId, hits, mode);
        SetSelection(selected, active);
        return SelectedIds;
    }

    // ---------------- mouse / keys ----------------

    void BoxPreviewMouseDown(MouseButtonEventArgs e)
    {
        EnsureBoxHooks();
        if (!_boxPending)
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            // B, then press: the box starts right here (Blender).
            Focus();
            BeginBox(e.GetPosition(this), ModeFromModifiers());
            e.Handled = true;
        }
        else
        {
            EndPending();
            e.Handled = e.ChangedButton == MouseButton.Right;
        }
    }

    void BoxPreviewMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_boxActive)
        {
            UpdateBox(p);
            e.Handled = true;
            return;
        }

        if (!_leftPressed || e.LeftButton != MouseButtonState.Pressed || _xformActive || (p - _pressPoint).Length < BoxDragThreshold
            || (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            return;
        }

        // Only a drag that starts on empty space draws a box.
        if (PickAt(_pressPoint) >= 0 || TryPickLight(_pressPoint, out _, out _) || TryPickHelper(_pressPoint, out _, out _))
        {
            return;
        }

        _leftPressed = false;
        BeginBox(_pressPoint, ModeFromModifiers());
        UpdateBox(p);
        e.Handled = true;
    }

    void BoxPreviewMouseUp(MouseButtonEventArgs e)
    {
        if (_boxActive && e.ChangedButton == MouseButton.Left)
        {
            var box = new Rect(_boxStart, e.GetPosition(this));
            var mode = _boxMode;
            CancelBox();
            BoxSelect(box, mode);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left || !_leftPressed || (e.GetPosition(this) - _pressPoint).Length >= BoxDragThreshold)
        {
            return;
        }

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.Shift)
        {
            // Shift+click: add / make active / remove (Blender).
            _leftPressed = false;
            int id = PickAt(e.GetPosition(this));
            if (id >= 0)
            {
                var sel = SelectedIds.ToList();
                if (id == _selectedId)
                {
                    sel.Remove(id);
                    SetSelection(sel, sel.Count > 0 ? sel[0] : -1);
                }
                else
                {
                    if (!sel.Contains(id))
                    {
                        sel.Add(id);
                    }

                    SetSelection(sel, id);
                }
            }

            e.Handled = true;
            return;
        }

        // Plain click: the normal handler selects one object; drop the others first.
        ClearExtraSelection();
    }

    void BoxPreviewKeyDown(KeyEventArgs e)
    {
        if (_boxActive || _boxPending)
        {
            if (e.Key == Key.Escape)
            {
                CancelBox();
                EndPending();
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.None && !_xformActive && !e.IsRepeat)
        {
            _boxPending = true;
            Cursor = Cursors.Cross;
            e.Handled = true;
        }
    }

    BoxSelectMode ModeFromModifiers()
    {
        var m = Keyboard.Modifiers;
        return (m & ModifierKeys.Control) != 0 ? BoxSelectMode.Subtract
            : (m & ModifierKeys.Shift) != 0 ? BoxSelectMode.Extend
            : BoxSelectMode.Replace;
    }

    void BeginBox(Point start, BoxSelectMode mode)
    {
        EndPending();
        EnsureBoxOverlay();
        _boxActive = true;
        _boxStart = start;
        _boxMode = mode;
        _boxCanvas!.Visibility = Visibility.Visible;
        UpdateBox(start);
        CaptureMouse();
    }

    void UpdateBox(Point p)
    {
        if (_boxShade == null || _boxDash == null)
        {
            return;
        }

        var r = new Rect(_boxStart, p);
        foreach (var rect in new[] { _boxShade, _boxDash })
        {
            Canvas.SetLeft(rect, r.X);
            Canvas.SetTop(rect, r.Y);
            rect.Width = r.Width;
            rect.Height = r.Height;
        }
    }

    void CancelBox()
    {
        if (!_boxActive)
        {
            return;
        }

        _boxActive = false;
        if (_boxCanvas != null)
        {
            _boxCanvas.Visibility = Visibility.Collapsed;
        }

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    void EndPending()
    {
        if (_boxPending)
        {
            _boxPending = false;
            ClearValue(CursorProperty);
        }
    }

    /// <summary>Overlay: very transparent white fill, black line under a white dashed line (readable on light and dark).</summary>
    void EnsureBoxOverlay()
    {
        if (_boxCanvas != null)
        {
            return;
        }

        _boxCanvas = new Canvas { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _boxShade = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)), Stroke = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)), StrokeThickness = 1, SnapsToDevicePixels = true };
        _boxDash = new Rectangle { Stroke = Brushes.White, StrokeThickness = 1, StrokeDashArray = [4, 4], SnapsToDevicePixels = true };
        _boxCanvas.Children.Add(_boxShade);
        _boxCanvas.Children.Add(_boxDash);
        if (Content is Panel panel)
        {
            panel.Children.Add(_boxCanvas);
        }
    }
}
