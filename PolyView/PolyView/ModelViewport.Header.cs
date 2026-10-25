using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PolyView;

/// <summary>Blender 4.2 3D viewport header: editor type, mode, View / Select / Add / Object menus (shading buttons sit on the right).</summary>
public sealed partial class ModelViewport
{
    public const double HeaderHeight = 30;
    readonly MenuItem _hdrView = new();
    readonly MenuItem _hdrSelect = new();
    readonly MenuItem _hdrAdd = new();
    readonly MenuItem _hdrObject = new();
    readonly TextBlock _hdrModeText = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)) };

    UIElement BuildViewportHeader()
    {
        var menu = new Menu { Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
        var modeLabel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x54, 0x54, 0x54)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { LineIcon("cube"), new TextBlock { Width = 4 }, _hdrModeText },
            },
        };
        foreach (var (item, fill) in new (MenuItem, Action<ItemCollection>)[]
        {
            (_hdrView, FillViewMenu),
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

        var headerContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        headerContent.Children.Add(modeLabel);
        headerContent.Children.Add(menu);

        ApplyHeaderLanguage();
        return new Border
        {
            Height = HeaderHeight,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x30, 0x30, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x16)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(4, 0, 0, 0),
            Child = headerContent,
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

    void FillViewMenu(ItemCollection items)
    {
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
        items.Add(Item(UiText.HdrSelectNone, _selectedId >= 0 ? () => Select(-1) : null, "Alt A"));
    }

    void FillObjectMenu(ItemCollection items)
    {
        bool sel = _selectedId >= 0;
        var t = new MenuItem { Header = UiText.HdrTransform };
        t.Items.Add(Item(UiText.HdrMove, sel ? () => ViewKey(Key.G, ModifierKeys.None) : null, "G"));
        t.Items.Add(Item(UiText.HdrRotate, sel ? () => ViewKey(Key.R, ModifierKeys.None) : null, "R"));
        t.Items.Add(Item(UiText.HdrScale, sel ? () => ViewKey(Key.S, ModifierKeys.None) : null, "S"));
        items.Add(t);
        items.Add(Item(UiText.HdrDuplicate, sel ? () => DuplicateAndMove(linked: false) : null, "Shift D"));
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
