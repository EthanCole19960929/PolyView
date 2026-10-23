using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PolyView;

/// <summary>Blender 4.2 3D viewport header: editor type, mode, View / Select / Add / Object menus (shading buttons sit on the right).</summary>
public sealed partial class ModelViewport
{
    public const double HeaderHeight = 30;
    readonly MenuItem _hdrEditor = new();
    readonly MenuItem _hdrMode = new();
    readonly MenuItem _hdrView = new();
    readonly MenuItem _hdrSelect = new();
    readonly MenuItem _hdrAdd = new();
    readonly MenuItem _hdrObject = new();
    readonly TextBlock _hdrModeText = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)) };

    UIElement BuildViewportHeader()
    {
        var menu = new Menu { Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        // Editor type: icon + small arrow, like Blender.
        _hdrEditor.Header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { LineIcon("mesh"), new TextBlock { Text = " ▾", Foreground = MenuGreyBrush, FontSize = 10, VerticalAlignment = VerticalAlignment.Center } },
        };
        // Mode dropdown: a rounded "button" look.
        _hdrMode.Header = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x54, 0x54, 0x54)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { LineIcon("cube"), new TextBlock { Width = 4 }, _hdrModeText, new TextBlock { Text = "  ▾", Foreground = MenuGreyBrush, FontSize = 10, VerticalAlignment = VerticalAlignment.Center } },
            },
        };
        foreach (var (item, fill) in new (MenuItem, Action<ItemCollection>)[]
        {
            (_hdrEditor, FillEditorMenu), (_hdrMode, FillModeMenu), (_hdrView, FillViewMenu),
            (_hdrSelect, FillSelectMenu), (_hdrAdd, PopulateBlenderAddMenu), (_hdrObject, FillObjectMenu),
        })
        {
            item.Items.Add(new MenuItem()); // placeholder so the submenu can open; refilled on open (follows the language)
            item.SubmenuOpened += (s, e) =>
            {
                if (e.OriginalSource != s)
                {
                    return;
                }

                item.Items.Clear();
                fill(item.Items);
            };
            menu.Items.Add(item);
        }

        ApplyHeaderLanguage();
        return new Border
        {
            Height = HeaderHeight,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x30, 0x30, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x16)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(4, 0, 0, 0),
            Child = menu,
        };
    }

    /// <summary>Refresh the header labels after a language switch.</summary>
    public void ApplyHeaderLanguage()
    {
        _hdrModeText.Text = UiText.HdrObjectMode;
        _hdrView.Header = UiText.HdrView;
        _hdrSelect.Header = UiText.HdrSelect;
        _hdrAdd.Header = UiText.AddTitle;
        _hdrObject.Header = UiText.HdrObject;
        _hdrEditor.ToolTip = UiText.HdrEditorType;
    }

    static MenuItem Item(string header, Action? click, string? gesture = null, bool check = false)
    {
        var item = new MenuItem { Header = header, IsEnabled = click != null, InputGestureText = gesture ?? "", IsCheckable = false, IsChecked = check };
        if (click != null)
        {
            item.Click += (_, _) => click();
        }

        return item;
    }

    void FillEditorMenu(ItemCollection items)
    {
        items.Add(Item(UiText.Hdr3DViewport, () => { }, "Shift F5", check: true));
        items.Add(Item(UiText.HdrImageEditor, null, "Shift F10"));
        items.Add(Item(UiText.HdrShaderEditor, null, "Shift F3"));
        items.Add(Item(UiText.HdrTimeline, null, "Shift F12"));
        items.Add(new Separator());
        items.Add(Item(UiText.HdrOutliner, null, "Shift F9"));
        items.Add(Item(UiText.HdrProperties, null, "Shift F7"));
    }

    void FillModeMenu(ItemCollection items)
    {
        items.Add(Item(UiText.HdrObjectMode, () => { }, check: true));
        items.Add(Item(UiText.HdrEditMode, null, "Tab"));
        items.Add(Item(UiText.HdrSculptMode, null));
        items.Add(Item(UiText.HdrVertexPaint, null));
        items.Add(Item(UiText.HdrWeightPaint, null));
        items.Add(Item(UiText.HdrTexturePaint, null));
    }

    void FillViewMenu(ItemCollection items)
    {
        items.Add(Item(UiText.HdrToolbar, null, "T"));
        items.Add(Item(UiText.HdrSidebar, null, "N"));
        items.Add(new Separator());
        items.Add(Item(UiText.HdrFrameSelected, _selectedId >= 0 ? FrameSelected : null, "Numpad ."));
        items.Add(Item(UiText.HdrFrameAll, _objects.Count > 0 ? FrameAll : null, "Home"));
        items.Add(new Separator());
        items.Add(Item(UiText.HdrPerspOrtho, () => ViewKey(Key.NumPad5, ModifierKeys.None), "Numpad 5"));
        var vp = new MenuItem { Header = UiText.HdrViewpoint };
        vp.Items.Add(Item(UiText.HdrCameraView, () => ToggleCameraView(), "Numpad 0"));
        vp.Items.Add(new Separator());
        vp.Items.Add(Item(UiText.HdrViewTop, () => ViewKey(Key.NumPad7, ModifierKeys.None), "Numpad 7"));
        vp.Items.Add(Item(UiText.HdrViewBottom, () => ViewKey(Key.NumPad7, ModifierKeys.Control), "Ctrl Numpad 7"));
        vp.Items.Add(new Separator());
        vp.Items.Add(Item(UiText.HdrViewFront, () => ViewKey(Key.NumPad1, ModifierKeys.None), "Numpad 1"));
        vp.Items.Add(Item(UiText.HdrViewBack, () => ViewKey(Key.NumPad1, ModifierKeys.Control), "Ctrl Numpad 1"));
        vp.Items.Add(new Separator());
        vp.Items.Add(Item(UiText.HdrViewRight, () => ViewKey(Key.NumPad3, ModifierKeys.None), "Numpad 3"));
        vp.Items.Add(Item(UiText.HdrViewLeft, () => ViewKey(Key.NumPad3, ModifierKeys.Control), "Ctrl Numpad 3"));
        items.Add(vp);
    }

    void FillSelectMenu(ItemCollection items)
    {
        // Selection is single-object for now: All / Invert need multi-select.
        items.Add(Item(UiText.HdrSelectAll, null, "A"));
        items.Add(Item(UiText.HdrSelectNone, _selectedId >= 0 ? () => Select(-1) : null, "Alt A"));
        items.Add(Item(UiText.HdrSelectInvert, null, "Ctrl I"));
        items.Add(new Separator());
        items.Add(Item(UiText.HdrSelectBox, null, "B"));
    }

    void FillObjectMenu(ItemCollection items)
    {
        bool sel = _selectedId >= 0;
        var t = new MenuItem { Header = UiText.HdrTransform };
        t.Items.Add(Item(UiText.HdrMove, sel ? () => ViewKey(Key.G, ModifierKeys.None) : null, "G"));
        t.Items.Add(Item(UiText.HdrRotate, sel ? () => ViewKey(Key.R, ModifierKeys.None) : null, "R"));
        t.Items.Add(Item(UiText.HdrScale, sel ? () => ViewKey(Key.S, ModifierKeys.None) : null, "S"));
        items.Add(t);
        items.Add(Item(UiText.HdrApply, null, "Ctrl A"));
        items.Add(Item(UiText.HdrSnap, null, "Shift S"));
        items.Add(new Separator());
        items.Add(Item(UiText.HdrDuplicate, sel && _primitiveSpecs.ContainsKey(_selectedId) ? () => DuplicateSelected() : null, "Shift D"));
        items.Add(Item(UiText.HdrParent, null, "Ctrl P"));
        items.Add(new Separator());
        var sh = new MenuItem { Header = UiText.HdrShowHide };
        sh.Items.Add(Item(UiText.HdrShowHidden, _objects.Any(o => !o.Visible) ? ShowAllHidden : null, "Alt H"));
        sh.Items.Add(Item(UiText.HdrHideSelected, sel ? () => SetObjectVisible(_selectedId, false) : null, "H"));
        items.Add(sh);
        items.Add(new Separator());
        items.Add(Item(UiText.HdrDelete, sel ? () => DeleteSelectedConfirm(ask: false) : null, "X"));
    }

    /// <summary>Run a viewport shortcut from a menu (same code path as the keyboard).</summary>
    void ViewKey(Key key, ModifierKeys modifiers)
    {
        Focus();
        HandleKey(key, modifiers);
    }

    void ShowAllHidden()
    {
        foreach (var o in _objects.Where(o => !o.Visible).ToList())
        {
            SetObjectVisible(o.Id, true);
        }

        SceneChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Frame the selected object, keeping the view direction (Blender Numpad .).</summary>
    public void FrameSelected()
    {
        if (!TryGetObject(_selectedId, out var o))
        {
            FrameAll();
            return;
        }

        float yaw = _camera.Yaw, pitch = _camera.Pitch;
        var (min, max) = o.WorldBounds();
        _camera.Frame(min * _scale, max * _scale);
        _camera.SetView(yaw, pitch);
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }
}
