using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using PolyView.Mcp;

namespace PolyView;

/// <summary>Top bar "● MCP" status item (after Help) with a small Blender-style popup.</summary>
public partial class MainWindow
{
    Popup? _mcpPopup;
    Ellipse? _mcpDot;

    static readonly Brush McpGreen = new SolidColorBrush(Color.FromRgb(0x5C, 0xC8, 0x5C));
    static readonly Brush McpGrey = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70));

    void InitMcpMenu()
    {
        _mcpDot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(_mcpDot);
        header.Children.Add(new TextBlock { Text = "MCP", VerticalAlignment = VerticalAlignment.Center });
        MenuMcp.Header = header;
        MenuMcp.Click += (_, _) => ShowMcpPopup();
        McpServerHost.StateChanged += () => Dispatcher.BeginInvoke(UpdateMcpDot);
        UpdateMcpDot();
    }

    void UpdateMcpDot()
    {
        if (_mcpDot == null) return;
        _mcpDot.Fill = McpServerHost.Running ? McpGreen : McpGrey;
        MenuMcp.ToolTip = McpServerHost.Running ? McpServerHost.Url : UiText.T("MCP 未运行", "MCP is off");
    }

    void ShowMcpPopup()
    {
        var fg = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
        var dim = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
        var panel = new StackPanel { Width = 380 };
        var status = new TextBlock { Foreground = McpServerHost.Running ? McpGreen : dim, FontWeight = FontWeights.SemiBold,
            Text = McpServerHost.Running ? UiText.T("● MCP 运行中", "● MCP running") : "○ " + (McpServerHost.Error ?? UiText.T("MCP 已关闭", "MCP off")) };
        panel.Children.Add(status);
        var url = new TextBox { Text = McpServerHost.Url, IsReadOnly = true, Margin = new Thickness(0, 6, 0, 6), Height = 22, Padding = new Thickness(4, 0, 4, 0),
            VerticalContentAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x1D)), Foreground = fg, BorderThickness = new Thickness(0) };
        panel.Children.Add(url);
        var toggle = new CheckBox { Content = UiText.T("启用 MCP 服务", "Enable MCP server"), IsChecked = AppSettings.Current.McpEnabled, Foreground = fg, Margin = new Thickness(0, 0, 0, 6) };
        toggle.Click += async (_, _) =>
        {
            AppSettings.Current.McpEnabled = toggle.IsChecked == true;
            AppSettings.Current.Save();
            if (AppSettings.Current.McpEnabled) await McpServerHost.StartAsync(); else await McpServerHost.StopAsync();
            _mcpPopup!.IsOpen = false;
        };
        panel.Children.Add(toggle);
        var open = new Button { Content = UiText.T("打开 AI / MCP 设置", "Open AI / MCP settings"), Height = 24, Margin = new Thickness(0, 0, 0, 8),
            Background = new SolidColorBrush(Color.FromRgb(0x47, 0x72, 0xB3)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        open.Click += (_, _) =>
        {
            _mcpPopup!.IsOpen = false;
            var w = new PreferencesWindow(ApplyLanguage) { Owner = this };
            w.ShowCategory("AI");
            w.ShowDialog();
        };
        panel.Children.Add(open);
        panel.Children.Add(new TextBlock { Text = UiText.T("最近调用（最多 20 条）", "Recent calls (last 20)"), Foreground = dim, Margin = new Thickness(0, 0, 0, 3) });
        var log = new StackPanel();
        var calls = PolyViewTools.RecentCalls();
        if (calls.Count == 0) log.Children.Add(new TextBlock { Text = UiText.T("（暂无）", "(none yet)"), Foreground = dim });
        foreach (var c in calls)
        {
            log.Children.Add(new TextBlock { Text = c, Foreground = c.Contains("✗") ? new SolidColorBrush(Color.FromRgb(0xE0, 0x80, 0x60)) : fg,
                FontFamily = new FontFamily("Consolas"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        }

        panel.Children.Add(new ScrollViewer { Content = log, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _mcpPopup = new Popup
        {
            PlacementTarget = MenuMcp, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            Child = new Border { Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x16)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(10), Child = panel },
        };
        _mcpPopup.IsOpen = true;
    }
}
