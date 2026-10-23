using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PolyView.Import;

namespace PolyView;

/// <summary>
/// Blender-style import options panel (File > Import / drag and drop): preset dropdown with + / −, collapsible
/// 常规 / 选项 / 材质 sections plus FBX / glTF sections, blue 「导入 X」 and 「取消」. Returns null when cancelled.
/// </summary>
public sealed class ImportOptionsDialog : Window
{
    static readonly Brush PanelB = Hex("#FF303030"), TitleBg = Hex("#FF232323"), BorderB = Hex("#FF161616"),
        TextB = Hex("#FFE6E6E6"), DimB = Hex("#FFB0B0B0"), FieldB = Hex("#FF545454"), FieldHover = Hex("#FF656565"),
        BoxB = Hex("#FF1D1D1D"), Primary = Hex("#FF4772B3"), PrimaryHover = Hex("#FF5680C2"), HeaderB = Hex("#FF2A2A2A");

    readonly ImportOptions _o;
    readonly Dictionary<string, ImportOptions> _presets = ImportPresets.Load();
    readonly StackPanel _sections = new();
    readonly TextBlock _presetText = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    readonly Border _presetBox;
    readonly TextBox _presetName;
    readonly bool _fbx, _gltf;
    string? _preset;
    bool _ok;

    public static ImportOptions? Ask(Window? owner, IReadOnlyList<string> paths, ImportOptions? start = null)
    {
        var dlg = new ImportOptionsDialog(paths, start ?? ImportOptions.Default);
        if (owner != null && owner.IsLoaded)
        {
            dlg.Owner = owner;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dlg.ShowDialog();
        return dlg._ok ? dlg._o : null;
    }

    /// <summary>"FBX", "glTF", "OBJ" ... for the import button; empty when the files have different formats.</summary>
    public static string FormatLabel(IEnumerable<string> paths)
    {
        var labels = paths.Select(p => Path.GetExtension(p).ToLowerInvariant() switch
        {
            ".gltf" or ".glb" => "glTF",
            ".json" => "three.js",
            var e => e.TrimStart('.').ToUpperInvariant(),
        }).Distinct().ToArray();
        return labels.Length == 1 ? labels[0] : "";
    }

    ImportOptionsDialog(IReadOnlyList<string> paths, ImportOptions start)
    {
        _o = start.Clone();
        var exts = paths.Select(p => Path.GetExtension(p).ToLowerInvariant()).ToHashSet();
        _fbx = exts.Contains(".fbx");
        _gltf = exts.Contains(".gltf") || exts.Contains(".glb");

        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.Height; Width = 440; ShowInTaskbar = false;
        FontSize = 12; FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); Foreground = TextB;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        string caption = paths.Count == 1 ? paths[0] : UiText.ImpFilesTitle(paths.Count);
        Title = caption;

        // Title bar: the full file path.
        var close = new TextBlock { Text = "\u2715", Foreground = DimB, FontSize = 11, Width = 30, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        close.MouseLeftButtonUp += (_, _) => Close();
        var titleText = new TextBlock { Text = caption, Foreground = TextB, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = string.Join("\n", paths) };
        var titleBar = new DockPanel { Height = 28, Background = TitleBg };
        DockPanel.SetDock(close, Dock.Right);
        titleBar.Children.Add(close);
        titleBar.Children.Add(titleText);
        titleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        // Preset row: dropdown, +, −.
        _presetBox = Field(_presetText, 0);
        _presetBox.Cursor = Cursors.Hand;
        _presetBox.MouseLeftButtonUp += (_, _) => ShowPresetMenu();
        _presetName = new TextBox { Visibility = Visibility.Collapsed, Background = BoxB, Foreground = TextB, CaretBrush = TextB, BorderBrush = Primary, Padding = new Thickness(4, 2, 4, 2), VerticalContentAlignment = VerticalAlignment.Center, Height = 22 };
        _presetName.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { SavePreset(_presetName.Text); e.Handled = true; }
            else if (e.Key == Key.Escape) { EndPresetEdit(); e.Handled = true; }
        };
        _presetName.LostKeyboardFocus += (_, _) => EndPresetEdit();
        var add = SmallButton("+", UiText.ImpPresetAdd, () =>
        {
            _presetBox.Visibility = Visibility.Collapsed;
            _presetName.Visibility = Visibility.Visible;
            _presetName.Text = "";
            _presetName.Focus();
        });
        var remove = SmallButton("\u2212", UiText.ImpPresetRemove, RemovePreset);
        var presetRow = new DockPanel { Margin = new Thickness(10, 8, 10, 4) };
        DockPanel.SetDock(remove, Dock.Right);
        DockPanel.SetDock(add, Dock.Right);
        presetRow.Children.Add(remove);
        presetRow.Children.Add(add);
        var presetHost = new Grid();
        presetHost.Children.Add(_presetBox);
        presetHost.Children.Add(_presetName);
        presetRow.Children.Add(presetHost);
        UpdatePresetText();

        // Buttons.
        var format = FormatLabel(paths);
        var importBtn = Button(string.IsNullOrEmpty(format) ? UiText.ImpButton("").Trim() : UiText.ImpButton(format), true, Accept);
        var cancelBtn = Button(UiText.ImpCancel, false, Close);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10, 10, 10, 12) };
        buttons.Children.Add(importBtn);
        buttons.Children.Add(cancelBtn);

        BuildSections();
        var scroll = new ScrollViewer { Content = _sections, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 560 };
        var content = new StackPanel();
        content.Children.Add(new Border { Child = titleBar, CornerRadius = new CornerRadius(6, 6, 0, 0), ClipToBounds = true, Background = TitleBg });
        content.Children.Add(presetRow);
        content.Children.Add(scroll);
        content.Children.Add(buttons);
        Content = new Border
        {
            Margin = new Thickness(14), Background = PanelB, BorderBrush = BorderB, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Child = content,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.55, Color = Colors.Black },
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _presetName.Visibility != Visibility.Visible) { Close(); e.Handled = true; }
            else if (e.Key == Key.Enter && _presetName.Visibility != Visibility.Visible && Keyboard.FocusedElement is not TextBox) { Accept(); e.Handled = true; }
        };
    }

    void Accept()
    {
        Keyboard.ClearFocus(); // commit a number being edited
        try
        {
            _o.AxisMatrix();
        }
        catch (ArgumentException ex)
        {
            PvMessageDialog.Show(this, ex.Message, UiText.ImportFailedTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _ok = true;
        Close();
    }

    // ---------------------------------------------------------------- sections

    void BuildSections()
    {
        _sections.Children.Clear();
        AddSection(UiText.ImpGeneral, true,
            NumberRow(UiText.ImpScale, () => _o.Scale, v => _o.Scale = v, 0.0001f),
            NumberRow(UiText.ImpClamp, () => _o.ClampSize, v => _o.ClampSize = v, 0f),
            ChoiceRow(UiText.ImpForward, ImportOptions.Axes, a => a, () => _o.Forward, v => _o.Forward = v),
            ChoiceRow(UiText.ImpUp, ImportOptions.Axes, a => a, () => _o.Up, v => _o.Up = v));
        AddSection(UiText.ImpOptions, true,
            CheckRow(UiText.ImpSplitObject, () => _o.SplitByObject, v => _o.SplitByObject = v),
            CheckRow(UiText.ImpSplitGroup, () => _o.SplitByGroup, v => _o.SplitByGroup = v),
            CheckRow(UiText.ImpVertexGroups, () => _o.VertexGroups, v => _o.VertexGroups = v),
            CheckRow(UiText.ImpValidate, () => _o.ValidateMeshes, v => _o.ValidateMeshes = v),
            CheckRow(UiText.ImpClosedCurves, () => _o.DetectClosedCurves, v => _o.DetectClosedCurves = v),
            TextRow(UiText.ImpPathSeparator, () => _o.PathSeparator, v => _o.PathSeparator = v));
        AddSection(UiText.ImpMaterials, true,
            ChoiceRow(UiText.ImpNameCollision, ["MakeUnique", "ReferenceExisting"],
                k => k == "MakeUnique" ? UiText.ImpMakeUnique : UiText.ImpReferenceExisting,
                () => _o.MaterialNameCollision, v => _o.MaterialNameCollision = v));
        if (_fbx)
        {
            AddSection(UiText.ImpFbx, false,
                CheckRow(UiText.ImpFbxNormals, () => _o.FbxCustomNormals, v => _o.FbxCustomNormals = v),
                CheckRow(UiText.ImpFbxAnimation, () => _o.FbxImportAnimation, v => _o.FbxImportAnimation = v),
                CheckRow(UiText.ImpFbxApplyTransform, () => _o.FbxApplyTransform, v => _o.FbxApplyTransform = v),
                CheckRow(UiText.ImpFbxBoneOrientation, () => _o.FbxAutomaticBoneOrientation, v => _o.FbxAutomaticBoneOrientation = v));
        }

        if (_gltf)
        {
            AddSection(UiText.ImpGltf, false,
                CheckRow(UiText.ImpGltfMerge, () => _o.GltfMergeVertices, v => _o.GltfMergeVertices = v),
                CheckRow(UiText.ImpGltfShading, () => _o.GltfImportShading, v => _o.GltfImportShading = v),
                CheckRow(UiText.ImpGltfBindPose, () => _o.GltfGuessBindPose, v => _o.GltfGuessBindPose = v),
                CheckRow(UiText.ImpGltfLights, () => _o.GltfImportLights, v => _o.GltfImportLights = v));
        }
    }

    void AddSection(string title, bool expanded, params UIElement[] rows)
    {
        var body = new StackPanel { Margin = new Thickness(0, 2, 0, 6), Visibility = expanded ? Visibility.Visible : Visibility.Collapsed };
        foreach (var r in rows) body.Children.Add(r);
        var arrow = new TextBlock { Text = expanded ? "\u25BE" : "\u25B8", Foreground = DimB, Width = 16, VerticalAlignment = VerticalAlignment.Center };
        var header = new DockPanel { Background = HeaderB, Height = 24, Cursor = Cursors.Hand };
        header.Children.Add(arrow);
        header.Children.Add(new TextBlock { Text = title, Foreground = TextB, VerticalAlignment = VerticalAlignment.Center });
        header.MouseLeftButtonUp += (_, _) =>
        {
            bool show = body.Visibility != Visibility.Visible;
            body.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            arrow.Text = show ? "\u25BE" : "\u25B8";
        };
        var section = new StackPanel { Margin = new Thickness(10, 4, 10, 0) };
        section.Children.Add(new Border { Child = header, CornerRadius = new CornerRadius(3), Background = HeaderB, Padding = new Thickness(6, 0, 6, 0) });
        section.Children.Add(body);
        _sections.Children.Add(section);
    }

    static Grid Row(string label, UIElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 0), Height = 22 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.45, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.55, GridUnitType.Star) });
        var t = new TextBlock { Text = label, Foreground = DimB, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        g.Children.Add(t);
        Grid.SetColumn((FrameworkElement)control, 1);
        g.Children.Add(control);
        return g;
    }

    static Border Field(UIElement child, double margin) => new()
    {
        Background = FieldB, CornerRadius = new CornerRadius(3), Height = 22, Child = child, Margin = new Thickness(margin, 0, 0, 0),
    };

    UIElement NumberRow(string label, Func<float> get, Action<float> set, float min)
    {
        var box = new TextBox
        {
            Text = get().ToString("0.000", CultureInfo.InvariantCulture), Background = FieldB, Foreground = TextB, CaretBrush = TextB,
            BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        void Commit()
        {
            if (float.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && float.IsFinite(v))
            {
                set(MathF.Max(min, v));
            }

            box.Text = get().ToString("0.000", CultureInfo.InvariantCulture);
        }

        box.LostKeyboardFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); Keyboard.ClearFocus(); e.Handled = true; } };
        return Row(label, new Border { CornerRadius = new CornerRadius(3), Background = FieldB, Child = box, ClipToBounds = true });
    }

    UIElement TextRow(string label, Func<string> get, Action<string> set)
    {
        var box = new TextBox
        {
            Text = get(), Background = FieldB, Foreground = TextB, CaretBrush = TextB, BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 0, 4, 0),
        };
        box.TextChanged += (_, _) => set(box.Text);
        return Row(label, new Border { CornerRadius = new CornerRadius(3), Background = FieldB, Child = box, ClipToBounds = true });
    }

    UIElement ChoiceRow(string label, string[] keys, Func<string, string> text, Func<string> get, Action<string> set)
    {
        var t = new TextBlock { Text = text(get()), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var arrow = new TextBlock { Text = "\u25BE", Foreground = DimB, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        var dock = new DockPanel();
        DockPanel.SetDock(arrow, Dock.Right);
        dock.Children.Add(arrow);
        dock.Children.Add(t);
        var field = Field(dock, 0);
        field.Cursor = Cursors.Hand;
        Hover(field, FieldB, FieldHover);
        field.MouseLeftButtonUp += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = field, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MinWidth = field.ActualWidth };
            foreach (var k in keys)
            {
                var item = new MenuItem { Header = text(k), IsCheckable = true, IsChecked = k == get() };
                item.Click += (_, _) => { set(k); t.Text = text(k); };
                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        };
        return Row(label, field);
    }

    UIElement CheckRow(string label, Func<bool> get, Action<bool> set)
    {
        var mark = new TextBlock { Text = "\u2713", Foreground = TextB, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var box = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Child = mark, Margin = new Thickness(0, 0, 6, 0) };
        void Paint()
        {
            bool on = get();
            mark.Visibility = on ? Visibility.Visible : Visibility.Hidden;
            box.Background = on ? Primary : FieldB;
        }

        Paint();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0), Height = 20, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        row.Children.Add(box);
        row.Children.Add(new TextBlock { Text = label, Foreground = TextB, VerticalAlignment = VerticalAlignment.Center });
        row.MouseLeftButtonUp += (_, _) => { set(!get()); Paint(); };
        // Blender puts checkboxes in the value column.
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.45, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.55, GridUnitType.Star) });
        Grid.SetColumn(row, 1);
        g.Children.Add(row);
        return g;
    }

    static void Hover(Border b, Brush normal, Brush hover)
    {
        b.MouseEnter += (_, _) => b.Background = hover;
        b.MouseLeave += (_, _) => b.Background = normal;
    }

    Border SmallButton(string glyph, string tip, Action click)
    {
        var b = new Border
        {
            Width = 24, Height = 22, Margin = new Thickness(4, 0, 0, 0), CornerRadius = new CornerRadius(3), Background = FieldB, Cursor = Cursors.Hand, ToolTip = tip,
            Child = new TextBlock { Text = glyph, Foreground = TextB, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        Hover(b, FieldB, FieldHover);
        b.MouseLeftButtonUp += (_, _) => click();
        return b;
    }

    Border Button(string label, bool primary, Action click)
    {
        var normal = primary ? Primary : FieldB;
        var b = new Border
        {
            Background = normal, CornerRadius = new CornerRadius(4), MinWidth = 96, Height = 26, Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(12, 0, 12, 0), Cursor = Cursors.Hand,
            Child = new TextBlock { Text = label, Foreground = TextB, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        Hover(b, normal, primary ? PrimaryHover : FieldHover);
        b.MouseLeftButtonUp += (_, _) => click();
        return b;
    }

    // ---------------------------------------------------------------- presets

    void UpdatePresetText() => _presetText.Text = _preset ?? UiText.ImpPresetNone;

    void ShowPresetMenu()
    {
        var menu = new ContextMenu { PlacementTarget = _presetBox, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, MinWidth = _presetBox.ActualWidth };
        var none = new MenuItem { Header = UiText.ImpPresetNone, IsCheckable = true, IsChecked = _preset == null };
        none.Click += (_, _) => ApplyPreset(null);
        menu.Items.Add(none);
        foreach (var name in _presets.Keys.Order())
        {
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = name == _preset };
            item.Click += (_, _) => ApplyPreset(name);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    void ApplyPreset(string? name)
    {
        var source = name != null && _presets.TryGetValue(name, out var p) ? p : ImportOptions.Default;
        foreach (var prop in typeof(ImportOptions).GetProperties().Where(x => x.CanWrite))
        {
            prop.SetValue(_o, prop.GetValue(source));
        }

        _preset = name;
        UpdatePresetText();
        BuildSections();
    }

    void SavePreset(string name)
    {
        name = name.Trim();
        if (name.Length > 0)
        {
            Keyboard.ClearFocus();
            _presets[name] = _o.Clone();
            ImportPresets.Save(_presets);
            _preset = name;
            UpdatePresetText();
        }

        EndPresetEdit();
    }

    void EndPresetEdit()
    {
        _presetName.Visibility = Visibility.Collapsed;
        _presetBox.Visibility = Visibility.Visible;
    }

    void RemovePreset()
    {
        if (_preset != null && _presets.Remove(_preset))
        {
            ImportPresets.Save(_presets);
            _preset = null;
            UpdatePresetText();
        }
    }

    static Brush Hex(string s)
    {
        var b = (Brush)new BrushConverter().ConvertFromString(s)!;
        b.Freeze();
        return b;
    }
}
