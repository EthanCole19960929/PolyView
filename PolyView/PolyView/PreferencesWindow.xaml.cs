using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PolyView;

/// <summary>Blender 4.2-style Preferences window. Interface page holds the language selector.</summary>
public partial class PreferencesWindow : Window
{
    string _category = "Interface";
    readonly Action _onLanguageChanged;

    public static string DefaultProjectFolder => @"C:\Project\C#\PolyView\Export";

    static PreferencesWindow? Current;

    public PreferencesWindow(Action onLanguageChanged)
    {
        InitializeComponent();
        Current = this;
        _onLanguageChanged = onLanguageChanged;
        Rebuild();
    }

    void Rebuild()
    {
        Title = UiText.PrefTitle;
        SearchHint.Text = UiText.PrefSearch;
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        BuildCategories();
        BuildSections();
    }

    void BuildCategories()
    {
        CategoryList.Children.Clear();
        var filter = SearchBox.Text.Trim();
        foreach (var key in UiText.PrefCategoryKeys)
        {
            if (key == null)
            {
                if (filter.Length == 0) CategoryList.Children.Add(new Border { Height = 10 });
                continue;
            }
            var name = UiText.PrefCategory(key);
            if (filter.Length > 0 && !Matches(key, filter)) continue;
            var btn = new ToggleButton
            {
                Content = name,
                IsChecked = key == _category,
                Style = (Style)FindResource("CatButton"),
            };
            var k = key;
            btn.Click += (_, _) => { _category = k; BuildCategories(); BuildSections(); };
            CategoryList.Children.Add(btn);
        }
    }

    static bool Matches(string key, string filter)
    {
        bool Has(string s) => s.Contains(filter, StringComparison.OrdinalIgnoreCase);
        if (Has(UiText.PrefCategory(key))) return true;
        if (UiText.PrefSections(key).Any(Has)) return true;
        return key == "Interface" && (Has(UiText.PrefLanguage) || Has("language") || Has("语言"));
    }

    void BuildSections()
    {
        SectionList.Children.Clear();
        var sections = UiText.PrefSections(_category);
        for (int i = 0; i < sections.Length; i++)
        {
            UIElement body = Placeholder();
            bool expanded = false;
            if (_category == "Interface")
            {
                if (i == 0) { body = DisplayPanel(); expanded = true; }
                else if (i == 1) { body = EditorsPanel(); expanded = true; }
                else if (i == 2) { body = LanguagePanel(); expanded = true; }
            }
            else if (_category == "SaveLoad" && i == 1) { body = FolderRow(); expanded = true; }
            SectionList.Children.Add(Panel(sections[i], body, expanded));
        }
    }

    Expander Panel(string title, UIElement body, bool expanded) => new()
    {
        Header = title, IsExpanded = expanded, Content = body,
        Style = (Style)FindResource("PanelExpander"),
    };

    static readonly Brush FieldBrush = new SolidColorBrush(Color.FromRgb(0x54, 0x54, 0x54));
    static readonly Brush DimBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));

    static StackPanel Rows(params UIElement[] rows)
    {
        var s = new StackPanel();
        foreach (var r in rows) { if (r is FrameworkElement fe) fe.Margin = new Thickness(0, 2, 0, 2); s.Children.Add(r); }
        return s;
    }

    static UIElement Check(string label, bool value, string? leftLabel = null)
    {
        var cb = new CheckBox { Content = label, IsChecked = value, Foreground = Brushes.White, Style = (Style)Current!.FindResource("PrefCheck") };
        return LabeledRow(leftLabel ?? "", cb);
    }

    static UIElement Combo(string label, params string[] items)
    {
        var c = new ComboBox { Height = 22, MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var it in items) c.Items.Add(it);
        c.SelectedIndex = 0;
        return LabeledRow(label, c);
    }

    static UIElement Field(string label, string value) => LabeledRow(label, new TextBox
    {
        Text = value, Height = 22, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        Background = FieldBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0),
    });

    UIElement DisplayPanel() => Rows(
        Field(UiText.T("分辨率缩放", "Resolution Scale"), "1.00"),
        Field(UiText.T("线宽", "Line Width"), UiText.T("自动", "Auto")),
        Check(UiText.T("工具提示", "Tooltips"), true, UiText.T("提示", "Tooltips")),
        Check(UiText.T("Python 工具提示", "Python Tooltips"), false),
        Check(UiText.T("启动画面", "Splash Screen"), true));

    UIElement EditorsPanel() => Rows(
        Check(UiText.T("区块重叠", "Region Overlap"), true, UiText.T("编辑器", "Editors")),
        Check(UiText.T("边角控制柄", "Corner Splitting"), true),
        Check(UiText.T("数值输入箭头", "Number Arrows"), true),
        Check(UiText.T("导航控件", "Navigation Controls"), true),
        Field(UiText.T("边界宽度", "Border Width"), "2 px"),
        Combo(UiText.T("拾色器类型", "Color Picker Type"), UiText.T("圆形 (色相/值)", "Circle (HSV)"), UiText.T("方形 (SV + H)", "Square (SV + H)")),
        Combo(UiText.T("标题栏位置", "Header Position"), UiText.T("保持现状", "Keep Existing"), UiText.T("顶部", "Top"), UiText.T("底部", "Bottom")),
        Combo(UiText.T("系数显示类型", "Factor Display Type"), UiText.T("系数", "Factor"), UiText.T("百分比", "Percentage")),
        Panel(UiText.T("临时编辑器", "Temporary Editors"), Rows(
            Combo(UiText.T("渲染", "Render In"), UiText.T("新窗口", "New Window"), UiText.T("图像编辑器", "Image Editor")),
            Combo(UiText.T("文件浏览器", "File Browser"), UiText.T("新窗口", "New Window"), UiText.T("最大化区域", "Maximized Area"))), false),
        Panel(UiText.T("状态栏", "Status Bar"), Rows(
            Check(UiText.T("场景统计", "Scene Statistics"), false, UiText.T("显示", "Show")),
            Check(UiText.T("显存", "Video Memory"), false),
            Check(UiText.T("版本", "Blender Version"), false)), false));

    UIElement LanguagePanel() => Rows(
        LanguageRow(),
        Check(UiText.T("工具提示", "Tooltips"), true, UiText.T("翻译", "Translate")),
        Check(UiText.T("界面", "Interface"), true),
        Check(UiText.T("报告", "Reports"), true),
        Check(UiText.T("新建数据", "New Data"), true),
        Combo(UiText.T("日期格式", "Date Format"), UiText.T("自动", "Automatic"), "YYYY-MM-DD", "DD/MM/YYYY"),
        Combo(UiText.T("时间", "Time"), UiText.T("自动", "Automatic"), "24h", "12h"));

    static TextBlock Placeholder() => new() { Text = UiText.PrefPlaceholder, Foreground = DimBrush };

    static Grid LabeledRow(string label, UIElement value)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.4, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.6, GridUnitType.Star) });
        var t = new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        g.Children.Add(t);
        Grid.SetColumn(value, 1);
        g.Children.Add(value);
        return g;
    }

    UIElement LanguageRow()
    {
        var combo = new ComboBox { Height = 22, MinWidth = 160 };
        combo.Items.Add("简体中文 (Simplified Chinese)");
        combo.Items.Add("English (English)");
        combo.SelectedIndex = UiText.Chinese ? 0 : 1;
        combo.ToolTip = UiText.PrefLanguageHint;
        combo.SelectionChanged += (_, _) =>
        {
            bool zh = combo.SelectedIndex == 0;
            if (zh == UiText.Chinese) return;
            UiText.Chinese = zh;
            AppSettings.Current.Language = zh ? "zh" : "en";
            AppSettings.Current.Save();
            _onLanguageChanged();
            Dispatcher.BeginInvoke(Rebuild);
        };
        return LabeledRow(UiText.PrefLanguage, combo);
    }

    static UIElement FolderRow()
    {
        var box = new TextBox
        {
            Text = DefaultProjectFolder, IsReadOnly = true, Height = 22, VerticalContentAlignment = VerticalAlignment.Center,
            Background = FieldBrush, Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
            BorderThickness = new Thickness(0), Padding = new Thickness(6, 0, 6, 0),
        };
        return LabeledRow(UiText.PrefDefaultSaveFolder, box);
    }

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (CategoryList == null) return;
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        BuildCategories();
    }
}
