using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PolyView;

/// <summary>Preferences for the supported language, project folder, and MCP settings.</summary>
public partial class PreferencesWindow : Window
{
    string _category = "Interface";
    readonly Action _onLanguageChanged;

    public static string DefaultProjectFolder => @"C:\Project\C#\PolyView\Export";

    static PreferencesWindow? Current;

    public PreferencesWindow(Action onLanguageChanged)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
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
            UIElement body;
            bool expanded = true;
            if (_category == "Interface")
            {
                body = LanguagePanel();
            }
            else if (_category == "AI") body = i == 0 ? McpServerPanel() : McpClientsPanel();
            else body = FolderRow();
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
        var panel = new StackPanel();
        foreach (var row in rows)
        {
            if (row is FrameworkElement element) element.Margin = new Thickness(0, 2, 0, 2);
            panel.Children.Add(row);
        }

        return panel;
    }

    UIElement LanguagePanel() => LanguageRow();

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
