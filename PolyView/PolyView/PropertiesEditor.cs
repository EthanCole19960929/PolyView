using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PolyView;

/// <summary>
/// Blender 4.2 Properties editor (bottom of the right column): header (editor type, search, pin),
/// vertical tab strip, and collapsible / reorderable panels. Built in code to stay light.
/// </summary>
public sealed class PropertiesEditor : UserControl
{
    static readonly Brush Bg = Frozen(0x2B, 0x2B, 0x2B);
    static readonly Brush PanelBg = Frozen(0x30, 0x30, 0x30);
    static readonly Brush HeaderBg = Frozen(0x3A, 0x3A, 0x3A);
    static readonly Brush FieldBg = Frozen(0x54, 0x54, 0x54);
    static readonly Brush FieldHover = Frozen(0x65, 0x65, 0x65);
    static readonly Brush TextBrush = Frozen(0xE6, 0xE6, 0xE6);
    static readonly Brush DimText = Frozen(0x9A, 0x9A, 0x9A);
    static readonly Brush TabActive = Frozen(0x4A, 0x4A, 0x4A);
    static readonly Brush Orange = Frozen(0xF0, 0x9A, 0x3C);
    static readonly Brush Green = Frozen(0x55, 0xC7, 0xA6);

    static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public enum Tab { Tool, Render, Output, ViewLayer, Scene, World, Object, Modifiers, Particles, Physics, Constraints, Data, Material }

    // icon geometry (16x16) and tint per tab
    static readonly Dictionary<Tab, (string Geo, Brush Color, string Zh, string En)> TabInfo = new()
    {
        [Tab.Tool] = ("M3,13 L9,7 M8,3 A3,3 0 1 0 13,8 L10,8 L8,6 Z M12,3 L12,6", Frozen(0xC8, 0xC8, 0xC8), "工具", "Tool"),
        [Tab.Render] = ("M2,5 H14 V13 H2 Z M5,5 V3 H11 V5 M8,9 m-2,0 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0", Frozen(0xC8, 0xC8, 0xC8), "渲染", "Render"),
        [Tab.Output] = ("M2,3 H14 V10 H2 Z M5,13 H11 M8,10 V13", Frozen(0xC8, 0xC8, 0xC8), "输出", "Output"),
        [Tab.ViewLayer] = ("M3,3 H11 V11 H3 Z M5,5 H13 V13 H5", Frozen(0xC8, 0xC8, 0xC8), "视图层", "View Layer"),
        [Tab.Scene] = ("M4,12 A2.5,2.5 0 1 1 4,11.99 M10,4 L14,11 H6 Z", Frozen(0xC8, 0xC8, 0xC8), "场景", "Scene"),
        [Tab.World] = ("M8,2 A6,6 0 1 1 7.99,2 M2,8 H14 M8,2 C5,5 5,11 8,14 C11,11 11,5 8,2", Frozen(0xE0, 0x6C, 0x6C), "世界环境", "World"),
        [Tab.Object] = ("M3,3 H13 V13 H3 Z M6,6 H10 V10 H6 Z", Orange, "物体", "Object"),
        [Tab.Modifiers] = ("M3,13 L9,7 M8,3 A3,3 0 1 0 13,8 L10,8 L8,6 Z", Frozen(0x6A, 0x9B, 0xE8), "修改器", "Modifiers"),
        [Tab.Particles] = ("M4,4 A1.5,1.5 0 1 1 3.99,4 M12,6 A1.5,1.5 0 1 1 11.99,6 M7,12 A1.5,1.5 0 1 1 6.99,12 M4,4 L12,6 L7,12", Frozen(0x6A, 0x9B, 0xE8), "粒子", "Particles"),
        [Tab.Physics] = ("M8,2 A6,6 0 1 1 7.99,2 M8,5 A3,3 0 1 1 7.99,5", Frozen(0x6A, 0x9B, 0xE8), "物理", "Physics"),
        [Tab.Constraints] = ("M3,10 A3,3 0 1 1 9,10 A3,3 0 1 1 3,10 M7,6 A3,3 0 1 1 13,6 A3,3 0 1 1 7,6", Frozen(0x6A, 0x9B, 0xE8), "物体约束", "Object Constraints"),
        [Tab.Data] = ("M8,2 L14,13 H2 Z M8,2 V13 M5,8 H11", Green, "数据", "Data"),
        [Tab.Material] = ("M8,2 A6,6 0 1 1 7.99,2 M8,2 V14 M2,8 H14", Frozen(0xE0, 0x6C, 0x6C), "材质", "Material"),
    };

    static readonly Dictionary<string, bool> Expanded = [];
    static readonly Dictionary<Tab, List<string>> PanelOrder = [];

    ModelViewport? _viewport;
    Tab _tab = Tab.Object;
    int _shownId = -2;
    Tab _shownTab;
    int _materialSlot;
    int _materialOwner = -1;
    readonly StackPanel _tabs = new() { Margin = new Thickness(2, 4, 2, 4) };
    readonly StackPanel _content = new() { Margin = new Thickness(6, 4, 6, 8) };
    readonly ScrollViewer _scroll;
    readonly TextBox _search = new();
    readonly ToggleButton _pin = new() { Width = 22, Height = 22, Focusable = false, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    readonly Path _headerIcon = new() { Width = 14, Height = 14, Stretch = Stretch.Uniform, StrokeThickness = 1.3, Margin = new Thickness(6, 0, 6, 0) };
    readonly TextBlock _headerName = new() { Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center };
    readonly List<Action> _updaters = [];
    int _pinnedId = -1;
    bool _refreshQueued;

    public PropertiesEditor()
    {
        Background = Bg;
        Foreground = TextBrush;
        FontSize = 12;

        // header: editor type, search, collapse
        var top = new DockPanel { Margin = new Thickness(4, 4, 4, 2), LastChildFill = true };
        var editorType = new Border
        {
            Width = 34, Height = 22, CornerRadius = new CornerRadius(4), Background = FieldBg, Margin = new Thickness(0, 0, 6, 0),
            Child = new Path
            {
                Data = Geometry.Parse("M1,2 H9 V6 H1 Z M1,8 H9 V12 H1 Z M12,6 L14,8 L16,6"), Stroke = TextBrush, StrokeThickness = 1.2,
                Width = 20, Height = 12, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            },
            ToolTip = UiText.T("编辑器类型：属性", "Editor Type: Properties"),
        };
        DockPanel.SetDock(editorType, Dock.Left);
        top.Children.Add(editorType);
        _search.Background = Frozen(0x1E, 0x1E, 0x1E);
        _search.Foreground = TextBrush;
        _search.CaretBrush = TextBrush;
        _search.BorderBrush = Frozen(0x3C, 0x3C, 0x3C);
        _search.Padding = new Thickness(20, 2, 4, 2);
        _search.Height = 22;
        _search.VerticalContentAlignment = VerticalAlignment.Center;
        _search.TextChanged += (_, _) => ApplySearch();
        _search.KeyDown += (_, e) => { if (e.Key == Key.Escape) { _search.Text = ""; Keyboard.ClearFocus(); } };
        var searchHint = new TextBlock { Text = UiText.T("搜索", "Search"), Foreground = DimText, IsHitTestVisible = false, Margin = new Thickness(22, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _search.TextChanged += (_, _) => searchHint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var searchGrid = new Grid();
        searchGrid.Children.Add(_search);
        searchGrid.Children.Add(new Path { Data = Geometry.Parse("M5,5 m-4,0 a4,4 0 1 0 8,0 a4,4 0 1 0 -8,0 M8,8 L12,12"), Stroke = DimText, StrokeThickness = 1.3, Margin = new Thickness(6, 5, 0, 0), IsHitTestVisible = false });
        searchGrid.Children.Add(searchHint);
        top.Children.Add(searchGrid);

        // object header: icon + name + pin
        var objHeader = new DockPanel { Margin = new Thickness(4, 4, 4, 2), Height = 24 };
        _pin.Content = new Path { Data = Geometry.Parse("M9,1 L15,7 L12,8 L9,11 L9,14 L2,7 L5,7 L8,4 Z M5,11 L1,15"), Stroke = DimText, StrokeThickness = 1.1, Width = 13, Height = 13, Stretch = Stretch.Uniform };
        _pin.ToolTip = UiText.T("固定：属性编辑器始终显示当前物体", "Pin: keep showing this object");
        _pin.Checked += (_, _) => { _pinnedId = _viewport?.SelectedId ?? -1; ((Path)_pin.Content).Stroke = TextBrush; Refresh(); };
        _pin.Unchecked += (_, _) => { _pinnedId = -1; ((Path)_pin.Content).Stroke = DimText; Refresh(); };
        DockPanel.SetDock(_pin, Dock.Right);
        objHeader.Children.Add(_pin);
        objHeader.Children.Add(_headerIcon);
        objHeader.Children.Add(_headerName);

        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _content };
        var tabBorder = new Border { Background = Frozen(0x23, 0x23, 0x23), Child = _tabs, Width = 30 };
        var body = new DockPanel();
        DockPanel.SetDock(tabBorder, Dock.Left);
        body.Children.Add(tabBorder);
        var right = new DockPanel();
        DockPanel.SetDock(objHeader, Dock.Top);
        right.Children.Add(objHeader);
        right.Children.Add(_scroll);
        body.Children.Add(right);

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(body);
        Content = root;
    }

    public Tab CurrentTab => _tab;

    public void Attach(ModelViewport viewport)
    {
        _viewport = viewport;
        viewport.SelectionChanged += (_, _) => Refresh();
        viewport.SceneChanged += (_, _) => Refresh();
        viewport.ObjectChanged += (_, id) => { if (id == ActiveId) QueueUpdate(); };
        Refresh();
    }

    public void ShowTab(Tab tab)
    {
        _tab = tab;
        Refresh();
    }

    public void ApplyLanguage()
    {
        _shownId = -2;
        Refresh();
    }

    int ActiveId
    {
        get
        {
            if (_viewport == null) return -1;
            if (_pinnedId >= 0 && _viewport.TryGetObjectInfo(_pinnedId, out _)) return _pinnedId;
            return _viewport.SelectedId;
        }
    }

    void QueueUpdate()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _refreshQueued = false;
            foreach (var u in _updaters) u();
        }, System.Windows.Threading.DispatcherPriority.Render);
    }

    static bool IsObjectTab(Tab t) => t >= Tab.Object;

    void Refresh()
    {
        if (_viewport == null) return;
        int id = ActiveId;
        bool hasObject = _viewport.TryGetObjectInfo(id, out var info);
        if (!hasObject && IsObjectTab(_tab)) _tab = Tab.Scene;
        BuildTabStrip(hasObject);

        var (geo, color, _, _) = TabInfo[hasObject && IsObjectTab(_tab) ? (_tab == Tab.Material ? Tab.Material : _tab == Tab.Data ? Tab.Data : Tab.Object) : Tab.Scene];
        _headerIcon.Data = Geometry.Parse(geo);
        _headerIcon.Stroke = color;
        _headerName.Text = hasObject && IsObjectTab(_tab)
            ? (_tab == Tab.Data ? _viewport.GetMeshDataName(id) : info.Name)
            : "Scene";

        if (id == _shownId && _tab == _shownTab)
        {
            foreach (var u in _updaters) u();
            return;
        }

        if (id != _materialOwner) { _materialSlot = 0; _materialOwner = id; }
        _shownId = id;
        _shownTab = _tab;
        _updaters.Clear();
        _content.Children.Clear();
        switch (_tab)
        {
            case Tab.Object: BuildObject(id, info); break;
            case Tab.Data: BuildData(id); break;
            case Tab.Material: BuildMaterial(id); break;
            case Tab.Scene: BuildScene(); break;
            default: BuildPlaceholders(_tab); break;
        }

        ApplyOrder();
        ApplySearch();
    }

    void BuildTabStrip(bool hasObject)
    {
        _tabs.Children.Clear();
        Tab[][] groups = hasObject
            ? [[Tab.Tool], [Tab.Render, Tab.Output, Tab.ViewLayer, Tab.Scene, Tab.World], [Tab.Object, Tab.Modifiers, Tab.Particles, Tab.Physics, Tab.Constraints, Tab.Data], [Tab.Material]]
            : [[Tab.Tool], [Tab.Render, Tab.Output, Tab.ViewLayer, Tab.Scene, Tab.World]];
        foreach (var group in groups)
        {
            foreach (var t in group)
            {
                var (geo, color, zh, en) = TabInfo[t];
                var b = new Border
                {
                    Width = 26, Height = 26, CornerRadius = new CornerRadius(4, 0, 0, 4), Margin = new Thickness(0, 1, -2, 1),
                    Background = t == _tab ? TabActive : Brushes.Transparent, ToolTip = UiText.T(zh, en), Cursor = Cursors.Hand,
                    Child = new Path { Data = Geometry.Parse(geo), Stroke = color, StrokeThickness = 1.3, Width = 15, Height = 15, Stretch = Stretch.Uniform },
                };
                var tab = t;
                b.MouseLeftButtonDown += (_, e) => { _tab = tab; Refresh(); e.Handled = true; };
                _tabs.Children.Add(b);
            }

            _tabs.Children.Add(new Border { Height = 8 });
        }
    }

    // ---------------- panels ----------------

    sealed class Panel(string key, Border root, StackPanel body)
    {
        public string Key = key;
        public Border Root = root;
        public StackPanel Body = body;
    }

    Panel AddPanel(string zh, string en, bool defaultOpen, Panel? parent = null)
    {
        string key = $"{_tab}/{en}";
        bool open = Expanded.TryGetValue(key, out var o) ? o : defaultOpen;
        var arrow = new Path
        {
            Data = Geometry.Parse("M0,0 L4,3.5 L0,7"), Stroke = Frozen(0xB0, 0xB0, 0xB0), StrokeThickness = 1.4, Margin = new Thickness(6, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(open ? 90 : 0),
        };
        var title = new TextBlock { Text = UiText.T(zh, en), Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center };
        var grip = new TextBlock { Text = "::::", Foreground = Frozen(0x6A, 0x6A, 0x6A), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.SizeNS, ToolTip = UiText.T("拖动以调整面板顺序", "Drag to reorder") };
        var header = new DockPanel { Height = 24, Background = parent == null ? HeaderBg : Brushes.Transparent, Cursor = Cursors.Arrow };
        DockPanel.SetDock(grip, Dock.Right);
        if (parent == null) header.Children.Add(grip);
        header.Children.Add(arrow);
        header.Children.Add(title);
        var body = new StackPanel { Margin = new Thickness(6, 4, 6, 6), Visibility = open ? Visibility.Visible : Visibility.Collapsed };
        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(body);
        var root = new Border { Background = PanelBg, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 0, 3), Child = stack, Tag = key, ClipToBounds = true };
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource == grip) return;
            bool now = body.Visibility != Visibility.Visible;
            Expanded[key] = now;
            body.Visibility = now ? Visibility.Visible : Visibility.Collapsed;
            arrow.RenderTransform = new RotateTransform(now ? 90 : 0);
            e.Handled = true;
        };
        if (parent == null)
        {
            HookReorder(grip, root);
            _content.Children.Add(root);
        }
        else
        {
            root.Background = Brushes.Transparent;
            parent.Body.Children.Add(root);
        }

        return new Panel(key, root, body);
    }

    void Placeholder(string zh, string en)
    {
        var p = AddPanel(zh, en, false);
        p.Body.Children.Add(new TextBlock { Text = UiText.T("（暂未实现）", "(Not implemented yet)"), Foreground = DimText, Margin = new Thickness(8, 2, 0, 2) });
    }

    void HookReorder(FrameworkElement grip, Border root)
    {
        bool dragging = false;
        grip.MouseLeftButtonDown += (_, e) => { dragging = true; grip.CaptureMouse(); e.Handled = true; };
        grip.MouseLeftButtonUp += (_, _) =>
        {
            if (!dragging) return;
            dragging = false;
            grip.ReleaseMouseCapture();
            PanelOrder[_tab] = _content.Children.OfType<Border>().Select(b => (string)b.Tag).ToList();
        };
        grip.MouseMove += (_, e) =>
        {
            if (!dragging) return;
            double y = e.GetPosition(_content).Y;
            int from = _content.Children.IndexOf(root);
            int to = 0;
            double acc = 0;
            for (int i = 0; i < _content.Children.Count; i++)
            {
                var c = (FrameworkElement)_content.Children[i];
                if (y > acc + c.ActualHeight / 2) to = i + (i >= from ? 0 : 1);
                acc += c.ActualHeight + 3;
            }

            to = Math.Clamp(to, 0, _content.Children.Count - 1);
            if (to != from)
            {
                _content.Children.RemoveAt(from);
                _content.Children.Insert(to, root);
            }
        };
    }

    void ApplyOrder()
    {
        if (!PanelOrder.TryGetValue(_tab, out var order)) return;
        var panels = _content.Children.OfType<Border>().ToList();
        var sorted = panels.OrderBy(p => { int i = order.IndexOf((string)p.Tag); return i < 0 ? int.MaxValue : i; }).ToList();
        var others = _content.Children.Cast<UIElement>().Where(c => c is not Border).ToList();
        _content.Children.Clear();
        foreach (var o in others) _content.Children.Add(o);
        foreach (var p in sorted) _content.Children.Add(p);
    }

    void ApplySearch()
    {
        string q = _search.Text.Trim();
        foreach (var b in _content.Children.OfType<Border>())
        {
            var title = ((b.Child as StackPanel)?.Children[0] as DockPanel)?.Children.OfType<TextBlock>().LastOrDefault()?.Text ?? "";
            b.Visibility = q.Length == 0 || title.Contains(q, StringComparison.OrdinalIgnoreCase) || ((string)b.Tag).Contains(q, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    // ---------------- rows ----------------

    static Grid Row(string label, UIElement field, UIElement? trailing = null)
    {
        var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.42, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        var l = new TextBlock { Text = label, Foreground = TextBrush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        g.Children.Add(l);
        Grid.SetColumn(field, 1);
        g.Children.Add(field);
        if (trailing != null)
        {
            Grid.SetColumn(trailing, 2);
            g.Children.Add(trailing);
        }

        return g;
    }

    static ToggleButton LockToggle(bool locked, Action<bool> changed)
    {
        var path = new Path { StrokeThickness = 1.1, Width = 11, Height = 13, Stretch = Stretch.Uniform };
        void Paint(bool l)
        {
            path.Data = Geometry.Parse(l ? "M2,7 H12 V15 H2 Z M4,7 V4 A3,3 0 0 1 10,4 V7" : "M2,7 H12 V15 H2 Z M4,7 V4 A3,3 0 0 1 10,4 V5");
            path.Stroke = l ? Frozen(0xF0, 0xF0, 0xF0) : DimText;
        }

        Paint(locked);
        var t = new ToggleButton
        {
            IsChecked = locked, Content = path, Width = 20, Height = 20, Focusable = false, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            ToolTip = UiText.T("锁定：交互变换时不改变该值", "Lock: interactive transforms don't change this value"),
            Template = FlatTemplate(),
        };
        t.Checked += (_, _) => { Paint(true); changed(true); };
        t.Unchecked += (_, _) => { Paint(false); changed(false); };
        return t;
    }

    static ControlTemplate FlatTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(ContentPresenter));
        factory.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        factory.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.AppendChild(factory);
        return new ControlTemplate(typeof(ToggleButton)) { VisualTree = border };
    }

    static CheckBox Check(string text, bool value, Action<bool> changed)
    {
        var c = new CheckBox { Content = text, IsChecked = value, Foreground = TextBrush, Margin = new Thickness(0, 2, 0, 2), Focusable = false };
        c.Checked += (_, _) => changed(true);
        c.Unchecked += (_, _) => changed(false);
        return c;
    }

    static TextBlock Info(string text) => new() { Text = text, Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 2, 0, 2) };

    // ---------------- Object ----------------

    void BuildObject(int id, SceneObjectInfo info)
    {
        var vp = _viewport!;
        var nameBox = NameBox(info.Name, n => vp.RenameObject(id, n));
        _content.Children.Add(nameBox);
        _updaters.Add(() => { if (!nameBox.IsKeyboardFocusWithin && vp.TryGetObjectInfo(id, out var i)) ((TextBox)nameBox.Child).Text = i.Name; });

        var transform = AddPanel("变换", "Transform", true);
        string[] axes = ["X", "Y", "Z"];
        string[] groups = [UiText.T("位置", "Location"), UiText.T("旋转", "Rotation"), UiText.T("缩放", "Scale")];
        var locks = vp.GetLocks(id);
        for (int g = 0; g < 3; g++)
        {
            var group = new StackPanel { Margin = new Thickness(0, g == 0 ? 0 : 6, 0, 0) };
            for (int a = 0; a < 3; a++)
            {
                int index = g * 3 + a;
                var field = new NumberField
                {
                    Step = g == 0 ? 0.01 : g == 1 ? 1 : 0.005,
                    Format = g == 0 ? v => $"{Trim(v, 5)} m" : g == 1 ? v => $"{Trim(v, 3)}°" : v => v.ToString("0.000", CultureInfo.InvariantCulture),
                    CornerTop = a == 0, CornerBottom = a == 2,
                };
                field.Value = Component(vp, id, index);
                field.Changing += v => vp.LiveTransform(id, WithComponent(vp, id, index, v));
                field.Committed += v => { vp.LiveTransform(id, WithComponent(vp, id, index, v)); vp.EndLiveTransform(); };
                _updaters.Add(() => { if (!field.IsBusy) field.Value = Component(vp, id, index); });
                var lockToggle = LockToggle(locks[index], l => vp.SetLock(id, index, l));
                group.Children.Add(Row(a == 0 ? $"{groups[g]} {axes[a]}" : axes[a], field, lockToggle));
            }

            transform.Body.Children.Add(group);
            if (g == 1)
            {
                var combo = new ComboBox { Height = 22, Margin = new Thickness(0, 4, 0, 0) };
                foreach (var o in EulerOrder.Orders) combo.Items.Add($"{o} {UiText.T("欧拉", "Euler")}");
                combo.SelectedIndex = Array.IndexOf(EulerOrder.Orders, vp.GetRotationMode(id));
                combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) vp.SetRotationMode(id, EulerOrder.Orders[combo.SelectedIndex]); };
                combo.ToolTip = UiText.T("旋转模式（四元数 / 轴角暂未支持）", "Rotation mode (Quaternion / Axis Angle not supported yet)");
                transform.Body.Children.Add(Row(UiText.T("模式", "Mode"), combo));
            }
        }

        var delta = AddPanel("增量变换", "Delta Transform", false, transform);
        delta.Body.Children.Add(new TextBlock { Text = UiText.T("（暂未实现）", "(Not implemented yet)"), Foreground = DimText, Margin = new Thickness(8, 2, 0, 2) });

        Placeholder("关系", "Relations");
        Placeholder("集合", "Collections");
        Placeholder("实例化", "Instancing");
        Placeholder("运动路径", "Motion Paths");
        Placeholder("着色方式", "Shading");
        var vis = AddPanel("可见性", "Visibility", false);
        var show = Check(UiText.T("在视图中显示", "Show in Viewports"), info.Visible, v => vp.SetObjectVisible(id, v));
        vis.Body.Children.Add(show);
        _updaters.Add(() => { if (vp.TryGetObjectInfo(id, out var i) && show.IsChecked != i.Visible) show.IsChecked = i.Visible; });
        Placeholder("视图显示", "Viewport Display");
        Placeholder("线条画", "Line Art");
        Placeholder("动画", "Animation");
        Placeholder("自定义属性", "Custom Properties");
    }

    static string Trim(double v, int decimals) => Math.Round(v, decimals).ToString("0.#####", CultureInfo.InvariantCulture);

    static double Component(ModelViewport vp, int id, int index)
    {
        if (!vp.TryGetTransform(id, out var t)) return 0;
        var e = vp.GetEulerDegrees(id);
        return index switch
        {
            0 => t.Position.X, 1 => t.Position.Y, 2 => t.Position.Z,
            3 => e.X, 4 => e.Y, 5 => e.Z,
            6 => t.Scale.X, 7 => t.Scale.Y, _ => t.Scale.Z,
        };
    }

    static ObjectTransform WithComponent(ModelViewport vp, int id, int index, double value)
    {
        vp.TryGetTransform(id, out var t);
        float v = (float)value;
        if (index < 3)
        {
            var p = t.Position;
            if (index == 0) p.X = v; else if (index == 1) p.Y = v; else p.Z = v;
            t.Position = p;
        }
        else if (index < 6)
        {
            var mode = vp.GetRotationMode(id);
            var e = vp.GetEulerDegrees(id);
            if (index == 3) e.X = v; else if (index == 4) e.Y = v; else e.Z = v;
            t.Rotation = EulerOrder.ToQuaternion(e, mode);
        }
        else
        {
            var s = t.Scale;
            if (index == 6) s.X = v; else if (index == 7) s.Y = v; else s.Z = v;
            t.Scale = s;
        }

        return t;
    }

    Border NameBox(string name, Action<string> rename, bool readOnly = false)
    {
        var box = new TextBox
        {
            Text = name, Background = FieldBg, Foreground = TextBrush, CaretBrush = TextBrush, BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 2, 4, 2), VerticalContentAlignment = VerticalAlignment.Center, IsReadOnly = readOnly,
        };
        void Commit()
        {
            if (!readOnly && !string.IsNullOrWhiteSpace(box.Text)) rename(box.Text.Trim());
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); Keyboard.ClearFocus(); e.Handled = true; }
            else if (e.Key == Key.Escape) { box.Text = name; Keyboard.ClearFocus(); e.Handled = true; }
        };
        box.LostKeyboardFocus += (_, _) => Commit();
        return new Border { CornerRadius = new CornerRadius(4), Background = FieldBg, Height = 24, Margin = new Thickness(0, 0, 0, 6), Child = box, ClipToBounds = true };
    }

    // ---------------- Data ----------------

    void BuildData(int id)
    {
        var vp = _viewport!;
        _content.Children.Add(NameBox(vp.GetMeshDataName(id), _ => { }, readOnly: true));
        var stats = AddPanel("统计", "Statistics", true);
        var (v, e, f, t) = vp.GetMeshStats(id);
        stats.Body.Children.Add(Row(UiText.T("顶点", "Vertices"), Info(v.ToString("N0"))));
        stats.Body.Children.Add(Row(UiText.T("边", "Edges"), Info(e.ToString("N0"))));
        stats.Body.Children.Add(Row(UiText.T("面", "Faces"), Info(f.ToString("N0"))));
        stats.Body.Children.Add(Row(UiText.T("三角面", "Triangles"), Info(t.ToString("N0"))));
        stats.Body.Children.Add(Row(UiText.T("用户", "Users"), Info(vp.GetMeshUsers(id).ToString())));
        stats.Body.Children.Add(Row(UiText.T("材质槽", "Material Slots"), Info(vp.GetMaterialCount(id).ToString())));
        if (vp.TryGetObjectInfo(id, out var info))
        {
            var s = info.Size;
            stats.Body.Children.Add(Row(UiText.T("尺寸", "Dimensions"), Info($"{s.X:0.###} × {s.Y:0.###} × {s.Z:0.###} m")));
        }

        Placeholder("顶点组", "Vertex Groups");
        Placeholder("形态键", "Shape Keys");
        Placeholder("UV 贴图", "UV Maps");
        Placeholder("颜色属性", "Color Attributes");
        Placeholder("法向", "Normals");
        Placeholder("自定义属性", "Custom Properties");
    }

    // ---------------- Material ----------------

    void BuildMaterial(int id)
    {
        var vp = _viewport!;
        var slots = vp.GetMaterials(id);
        if (_materialSlot >= slots.Count) _materialSlot = 0;
        var list = new ListBox { Background = Frozen(0x23, 0x23, 0x23), Foreground = TextBrush, BorderThickness = new Thickness(0), MinHeight = 60, MaxHeight = 120, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var s in slots)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(ToColor(s.BaseColor)), Stroke = DimText, Margin = new Thickness(0, 0, 6, 0) });
            row.Children.Add(new TextBlock { Text = s.Name, Foreground = TextBrush });
            list.Items.Add(new ListBoxItem { Content = row, Foreground = TextBrush });
        }

        list.SelectedIndex = _materialSlot;
        list.SelectionChanged += (_, _) => { if (list.SelectedIndex >= 0) { _materialSlot = list.SelectedIndex; _shownId = -2; Refresh(); } };
        _content.Children.Add(list);
        if (slots.Count == 0) return;
        int slot = _materialSlot;
        var m = slots[slot];
        _content.Children.Add(NameBox(m.Name, n => vp.UpdateMaterial(id, slot, x => x.Name = n)));

        var surface = AddPanel("表面", "Surface", true);
        surface.Body.Children.Add(Row(UiText.T("表面", "Surface"), Info(UiText.T("原理化 BSDF", "Principled BSDF"))));
        string[] rgba = ["R", "G", "B"];
        var swatch = new Border { Height = 20, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(ToColor(m.BaseColor)), BorderBrush = DimText, BorderThickness = new Thickness(1) };
        surface.Body.Children.Add(Row(UiText.T("基础色", "Base Color"), swatch));
        for (int c = 0; c < 3; c++)
        {
            int ch = c;
            var f = new NumberField { Step = 0.005, Min = 0, Max = 1, Format = v => v.ToString("0.000", CultureInfo.InvariantCulture), CornerTop = c == 0, CornerBottom = c == 2 };
            f.Value = ch == 0 ? m.BaseColor.X : ch == 1 ? m.BaseColor.Y : m.BaseColor.Z;
            void Set(double v)
            {
                vp.UpdateMaterial(id, slot, x =>
                {
                    var b = x.BaseColor;
                    if (ch == 0) b.X = (float)v; else if (ch == 1) b.Y = (float)v; else b.Z = (float)v;
                    x.BaseColor = b;
                });
                swatch.Background = new SolidColorBrush(ToColor(m.BaseColor));
            }

            f.Changing += Set;
            f.Committed += Set;
            surface.Body.Children.Add(Row(rgba[c], f));
        }

        surface.Body.Children.Add(Scalar(UiText.T("金属度", "Metallic"), m.Metallic, v => vp.UpdateMaterial(id, slot, x => x.Metallic = (float)v)));
        surface.Body.Children.Add(Scalar(UiText.T("糙度", "Roughness"), m.Roughness, v => vp.UpdateMaterial(id, slot, x => x.Roughness = (float)v)));
        var emission = AddPanel("自发光", "Emission", false, surface);
        for (int c = 0; c < 3; c++)
        {
            int ch = c;
            var cur = ch == 0 ? m.EmissionColor.X : ch == 1 ? m.EmissionColor.Y : m.EmissionColor.Z;
            emission.Body.Children.Add(Scalar(c == 0 ? $"{UiText.T("颜色", "Color")} R" : rgba[c], cur, v => vp.UpdateMaterial(id, slot, x =>
            {
                var e = x.EmissionColor;
                if (ch == 0) e.X = (float)v; else if (ch == 1) e.Y = (float)v; else e.Z = (float)v;
                x.EmissionColor = e;
            })));
        }

        emission.Body.Children.Add(Scalar(UiText.T("强度", "Strength"), m.EmissionStrength, v => vp.UpdateMaterial(id, slot, x => x.EmissionStrength = (float)v), max: 1000));
        surface.Body.Children.Add(new TextBlock
        {
            Text = UiText.T("注：视口目前只显示基础色，金属度 / 糙度 / 自发光会保存在材质上，等着色器支持后生效。", "Note: the viewport shows Base Color only for now; Metallic / Roughness / Emission are stored for later."),
            Foreground = DimText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 6, 0, 0), FontSize = 11,
        });
        Placeholder("体积（音量）", "Volume");
        Placeholder("设置", "Settings");
        Placeholder("视图显示", "Viewport Display");
        Placeholder("自定义属性", "Custom Properties");
    }

    static Grid Scalar(string label, double value, Action<double> set, double max = 1)
    {
        var f = new NumberField { Step = max > 1 ? 0.05 : 0.005, Min = 0, Max = max, Format = v => v.ToString("0.000", CultureInfo.InvariantCulture), CornerTop = true, CornerBottom = true, Value = value };
        f.Changing += set;
        f.Committed += set;
        return Row(label, f);
    }

    static Color ToColor(Vector4 c) => Color.FromRgb((byte)(Math.Clamp(c.X, 0, 1) * 255), (byte)(Math.Clamp(c.Y, 0, 1) * 255), (byte)(Math.Clamp(c.Z, 0, 1) * 255));

    // ---------------- Scene / others ----------------

    void BuildScene()
    {
        var vp = _viewport!;
        var scene = AddPanel("场景", "Scene", true);
        scene.Body.Children.Add(Row(UiText.T("物体数", "Objects"), Info(vp.ObjectIds.Count.ToString())));
        var units = AddPanel("单位", "Units", true);
        units.Body.Children.Add(Row(UiText.T("单位系统", "Unit System"), Info(UiText.T("公制", "Metric"))));
        units.Body.Children.Add(Row(UiText.T("长度", "Length"), Info(UiText.T("米", "Meters"))));
        units.Body.Children.Add(Row(UiText.T("旋转", "Rotation"), Info(UiText.T("角度", "Degrees"))));
        Placeholder("重力", "Gravity");
        Placeholder("模拟", "Simulation");
        Placeholder("音频", "Audio");
        Placeholder("刚体世界环境", "Rigid Body World");
        Placeholder("动画", "Animation");
        Placeholder("自定义属性", "Custom Properties");
    }

    void BuildPlaceholders(Tab tab)
    {
        string[][] names = tab switch
        {
            Tab.Tool => [["工作区", "Workspace"], ["选项", "Options"]],
            Tab.Render => [["采样", "Sampling"], ["光照", "Lighting"], ["胶片", "Film"], ["色彩管理", "Color Management"]],
            Tab.Output => [["格式", "Format"], ["帧范围", "Frame Range"], ["输出", "Output"]],
            Tab.ViewLayer => [["视图层", "View Layer"], ["通道", "Passes"]],
            Tab.World => [["表面", "Surface"], ["体积（音量）", "Volume"], ["视图显示", "Viewport Display"]],
            Tab.Modifiers => [["添加修改器", "Add Modifier"]],
            Tab.Particles => [["粒子系统", "Particle System"]],
            Tab.Physics => [["启用物理", "Enable Physics"]],
            Tab.Constraints => [["添加物体约束", "Add Object Constraint"]],
            _ => [],
        };
        foreach (var n in names) Placeholder(n[0], n[1]);
    }
}

/// <summary>Blender-style number field: drag left/right to scrub (Shift = fine, Ctrl = snap), click to type, Esc cancels.</summary>
public sealed class NumberField : Border
{
    readonly TextBlock _text = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
    double _value;
    double _start;
    Point _press;
    bool _pressed;
    bool _dragging;
    TextBox? _edit;

    public double Step { get; set; } = 0.01;
    public double Min { get; set; } = double.NegativeInfinity;
    public double Max { get; set; } = double.PositiveInfinity;
    public Func<double, string> Format { get; set; } = v => v.ToString("0.###", CultureInfo.InvariantCulture);
    public bool CornerTop { set => CornerRadius = new CornerRadius(value ? 4 : 0, value ? 4 : 0, CornerRadius.BottomRight, CornerRadius.BottomLeft); }
    public bool CornerBottom { set => CornerRadius = new CornerRadius(CornerRadius.TopLeft, CornerRadius.TopRight, value ? 4 : 0, value ? 4 : 0); }

    public event Action<double>? Changing;
    public event Action<double>? Committed;

    /// <summary>True while the user drags or types: external updates should not overwrite the field.</summary>
    public bool IsBusy => _pressed || _edit != null;

    public NumberField()
    {
        Height = 22;
        Margin = new Thickness(0, 0, 0, 1);
        Background = new SolidColorBrush(Color.FromRgb(0x54, 0x54, 0x54));
        Child = _text;
        Cursor = Cursors.SizeWE;
        MouseEnter += (_, _) => { if (_edit == null) Background = new SolidColorBrush(Color.FromRgb(0x65, 0x65, 0x65)); };
        MouseLeave += (_, _) => { if (_edit == null) Background = new SolidColorBrush(Color.FromRgb(0x54, 0x54, 0x54)); };
    }

    public double Value
    {
        get => _value;
        set
        {
            _value = value;
            _text.Text = Format(value);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_edit != null) return;
        _pressed = true;
        _dragging = false;
        _press = e.GetPosition(this);
        _start = _value;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_pressed) return;
        double dx = e.GetPosition(this).X - _press.X;
        if (!_dragging && Math.Abs(dx) < 3) return;
        _dragging = true;
        double step = Step * (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 1);
        double v = _start + dx * step;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            double snap = Step * 10;
            v = Math.Round(v / snap) * snap;
        }

        Value = Math.Clamp(v, Min, Max);
        Changing?.Invoke(_value);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        ReleaseMouseCapture();
        if (_dragging)
        {
            _dragging = false;
            Committed?.Invoke(_value);
        }
        else
        {
            BeginEdit();
        }

        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (_pressed && _dragging)
        {
            // Blender: right-click cancels the drag
            _pressed = _dragging = false;
            ReleaseMouseCapture();
            Value = _start;
            Committed?.Invoke(_value);
            e.Handled = true;
        }
    }

    void BeginEdit()
    {
        _edit = new TextBox
        {
            Text = _value.ToString("0.######", CultureInfo.InvariantCulture), Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            Foreground = Brushes.White, CaretBrush = Brushes.White, BorderThickness = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 0, 4, 0),
        };
        _edit.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter || e.Key == Key.Tab) { EndEdit(true); e.Handled = true; }
            else if (e.Key == Key.Escape) { EndEdit(false); e.Handled = true; }
        };
        _edit.LostKeyboardFocus += (_, _) => EndEdit(true);
        Child = _edit;
        Cursor = Cursors.IBeam;
        _edit.Loaded += (_, _) => { _edit?.Focus(); _edit?.SelectAll(); };
    }

    void EndEdit(bool commit)
    {
        if (_edit == null) return;
        var text = _edit.Text.Replace("m", "").Replace("°", "").Trim();
        _edit = null;
        Child = _text;
        Cursor = Cursors.SizeWE;
        Background = new SolidColorBrush(Color.FromRgb(0x54, 0x54, 0x54));
        if (commit && TryEval(text, out var v) && double.IsFinite(v))
        {
            Value = Math.Clamp(v, Min, Max);
            Committed?.Invoke(_value);
        }
        else
        {
            Value = _value;
        }
    }

    /// <summary>Plain numbers plus simple expressions like "1.5*2" or "90/3" (Blender accepts Python expressions).</summary>
    static bool TryEval(string text, out double value)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
        try
        {
            var result = new System.Data.DataTable().Compute(text, null);
            value = Convert.ToDouble(result, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            value = 0;
            return false;
        }
    }
}
