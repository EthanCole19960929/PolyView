using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PolyView;

public sealed partial class ModelViewport
{
    bool _gridOverlayVisible = true;
    bool _selectionOutlineOverlayVisible = true;
    bool _overlayEnabled = true;

    public bool OverlayEnabled
    {
        get => _overlayEnabled;
        set
        {
            if (_overlayEnabled == value) return;
            _overlayEnabled = value;
            UpdateOverlayButtonState();
            ApplyOverlayVisibility();
            Invalidate();
        }
    }

    void UpdateOverlayButtonState()
    {
        if (_overlayButton != null) _overlayButton.IsChecked = OverlayEnabled;
        if (_overlayDropdownButton != null) _overlayDropdownButton.IsChecked = OverlayEnabled;
    }

    void ApplyOverlayVisibility()
    {
        if (_renderer == null) return;
        _renderer.ShowGrid = OverlayEnabled && GridOverlayVisible;
        _renderer.ShowSelectionOutline = OverlayEnabled && SelectionOutlineOverlayVisible;
    }

    public bool GridOverlayVisible
    {
        get => _gridOverlayVisible;
        set
        {
            if (_gridOverlayVisible == value)
            {
                return;
            }

            _gridOverlayVisible = value;
            UpdateOverlayButtonState();
            ApplyOverlayVisibility();
            Invalidate();
        }
    }

    public bool SelectionOutlineOverlayVisible
    {
        get => _selectionOutlineOverlayVisible;
        set
        {
            if (_selectionOutlineOverlayVisible == value)
            {
                return;
            }

            _selectionOutlineOverlayVisible = value;
            UpdateOverlayButtonState();
            ApplyOverlayVisibility();
            Invalidate();
        }
    }

    public bool IsBoneOverlayNodeVisible(int id, int nodeIndex) =>
        TryGetObject(id, out var obj)
        && nodeIndex >= 0 && nodeIndex < obj.Mesh.Nodes.Length
        && obj.Mesh.Nodes[nodeIndex].IsBone
        && !obj.HiddenBones.Contains(nodeIndex);

    public void SetBoneOverlayNodeVisible(int id, int nodeIndex, bool visible)
    {
        if (!TryGetObject(id, out var obj)
            || nodeIndex < 0 || nodeIndex >= obj.Mesh.Nodes.Length
            || !obj.Mesh.Nodes[nodeIndex].IsBone)
        {
            return;
        }

        bool changed = visible ? obj.HiddenBones.Remove(nodeIndex) : obj.HiddenBones.Add(nodeIndex);
        if (!changed)
        {
            return;
        }

        Invalidate();
        ObjectChanged?.Invoke(this, id);
    }

    void ShowOverlayMenu(UIElement target)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
        };
        if (Window.GetWindow(this) is { } window)
        {
            menu.Resources.MergedDictionaries.Add(window.Resources);
        }

        AddOverlayOption(menu, UiText.OverlayGrid, GridOverlayVisible, value => GridOverlayVisible = value);
        AddOverlayOption(menu, UiText.ViewportBones, BoneOverlayVisible, value => BoneOverlayVisible = value);
        menu.Items.Add(new Separator());
        AddOverlayOption(menu, UiText.OverlaySelectionOutline, SelectionOutlineOverlayVisible, value => SelectionOutlineOverlayVisible = value);
        menu.IsOpen = true;
    }

    static void AddOverlayOption(ContextMenu menu, string label, bool value, Action<bool> set)
    {
        var check = new CheckBox
        {
            IsChecked = value,
            IsHitTestVisible = false,
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 9, 0),
        };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(check);
        header.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var item = new MenuItem { Header = header, IsCheckable = false };
        item.Click += (_, _) => set(!value);
        menu.Items.Add(item);
    }
}
