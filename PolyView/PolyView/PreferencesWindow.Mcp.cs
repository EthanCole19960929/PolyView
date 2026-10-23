using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PolyView.Mcp;

namespace PolyView;

/// <summary>Preferences > AI / MCP: server status, client scan, connect / disconnect, copy config.</summary>
public partial class PreferencesWindow
{
    List<McpClient> _mcpClients = [];

    /// <summary>Open on a given category (e.g. "AI").</summary>
    public void ShowCategory(string key) { _category = key; Rebuild(); }
    readonly HashSet<string> _mcpChecked = [];

    static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x7C, 0xC4, 0x6A));
    static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x9A, 0x4A));

    Button PrefButton(string text, Action onClick, bool primary = false)
    {
        var b = new Button
        {
            Content = text, Height = 24, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 6, 0),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            Background = primary ? new SolidColorBrush(Color.FromRgb(0x47, 0x72, 0xB3)) : FieldBrush,
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    UIElement McpServerPanel()
    {
        var enabled = new CheckBox
        {
            Content = UiText.T("随软件启动 MCP 服务", "Start MCP server with PolyView"),
            IsChecked = AppSettings.Current.McpEnabled, Foreground = Brushes.White, Style = (Style)FindResource("PrefCheck"),
        };
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        void Refresh()
        {
            status.Text = McpServerHost.Running ? UiText.T("● 运行中", "● Running") : "○ " + (McpServerHost.Error ?? UiText.T("已停止", "Stopped"));
            status.Foreground = McpServerHost.Running ? OkBrush : WarnBrush;
        }

        enabled.Click += async (_, _) =>
        {
            AppSettings.Current.McpEnabled = enabled.IsChecked == true;
            AppSettings.Current.Save();
            if (AppSettings.Current.McpEnabled) await McpServerHost.StartAsync(); else await McpServerHost.StopAsync();
            Refresh();
        };
        Refresh();
        var url = new TextBox
        {
            Text = McpServerHost.Url, IsReadOnly = true, Height = 22, VerticalContentAlignment = VerticalAlignment.Center,
            Background = FieldBrush, Foreground = Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(6, 0, 6, 0),
        };
        var copy = new WrapPanel();
        copy.Children.Add(PrefButton(UiText.T("复制 HTTP 配置", "Copy HTTP config"), () => Clipboard.SetText(McpClients.HttpSnippet)));
        copy.Children.Add(PrefButton(UiText.T("复制 stdio 配置 (npx)", "Copy stdio config (npx)"), () => Clipboard.SetText(McpClients.StdioSnippet)));
        return Rows(LabeledRow(UiText.T("服务", "Server"), enabled), LabeledRow(UiText.T("状态", "Status"), status),
            LabeledRow(UiText.T("地址", "URL"), url), LabeledRow(UiText.T("通用配置", "Any MCP client"), copy),
            new TextBlock { Text = UiText.T("仅监听本机 127.0.0.1；删除物体前会弹窗让你确认。", "Listens on 127.0.0.1 only; deleting objects asks you first."), Foreground = DimBrush, TextWrapping = TextWrapping.Wrap });
    }

    UIElement McpClientsPanel()
    {
        var host = new StackPanel();
        var list = new StackPanel();
        var message = new TextBlock { Foreground = DimBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

        TextBlock Cell(string t, Brush? b = null, double w = 70) => new() { Text = t, Width = w, Foreground = b ?? Brushes.White, VerticalAlignment = VerticalAlignment.Center };

        void Fill()
        {
            list.Children.Clear();
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            head.Children.Add(Cell("", null, 24));
            head.Children.Add(Cell(UiText.T("AI 客户端", "AI client"), DimBrush, 170));
            head.Children.Add(Cell(UiText.T("已安装", "Installed"), DimBrush));
            head.Children.Add(Cell(UiText.T("运行中", "Running"), DimBrush));
            head.Children.Add(Cell(UiText.T("已连接", "Connected"), DimBrush));
            list.Children.Add(head);
            foreach (var c in _mcpClients)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                var cb = new CheckBox { IsChecked = _mcpChecked.Contains(c.Id), Width = 24, Style = (Style)FindResource("PrefCheck"), VerticalAlignment = VerticalAlignment.Center };
                var id = c.Id;
                cb.Click += (_, _) => { if (cb.IsChecked == true) _mcpChecked.Add(id); else _mcpChecked.Remove(id); };
                row.Children.Add(cb);
                row.Children.Add(Cell(c.Name, c.Installed ? Brushes.White : DimBrush, 170));
                row.Children.Add(Cell(c.Installed ? "✔" : "—", c.Installed ? OkBrush : DimBrush));
                row.Children.Add(Cell(c.ProcessRunning ? "✔" : "—", c.ProcessRunning ? OkBrush : DimBrush));
                row.Children.Add(Cell(c.Connected ? "✔" : "—", c.Connected ? OkBrush : DimBrush));
                if (!c.CanAuto)
                {
                    row.Children.Add(new Border { Background = WarnBrush, CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = UiText.T("手动", "Manual"), Foreground = Brushes.Black, FontSize = 11 } });
                    var cc = c;
                    var btn = PrefButton(UiText.T("\u590d\u5236\u914d\u7f6e", "Copy config"), () => { Clipboard.SetText(McpClients.SnippetFor(cc)); message.Text = UiText.T($"\u5df2\u590d\u5236 {cc.Name} \u7684\u914d\u7f6e\uff0c\u8bf7\u5728\u5b83\u7684 MCP \u8bbe\u7f6e\u91cc\u7c98\u8d34\uff08\u5730\u5740 {McpServerHost.Url}\uff09\u3002", $"Copied config for {cc.Name}; paste it in its MCP settings."); });
                    btn.Height = 20;
                    row.Children.Add(btn);
                }

                row.ToolTip = c.ConfigPath ?? UiText.T("该客户端需在其设置里手动粘贴配置", "Paste the copied config in this client's settings");
                list.Children.Add(row);
            }
        }

        void Scan()
        {
            _mcpClients = McpClients.Scan();
            if (_mcpChecked.Count == 0)
            {
                foreach (var c in _mcpClients.Where(c => c.Installed && c.CanAuto && !c.Connected)) _mcpChecked.Add(c.Id);
            }

            Fill();
        }

        void Apply(bool connect)
        {
            var done = new List<string>();
            var restart = new List<string>();
            var manual = new List<string>();
            var failed = new List<string>();
            foreach (var c in _mcpClients.Where(c => _mcpChecked.Contains(c.Id)))
            {
                if (!c.CanAuto) { manual.Add(c.Name); continue; }
                var err = connect ? McpClients.Connect(c) : McpClients.Disconnect(c);
                if (err != null) { failed.Add($"{c.Name}: {err}"); continue; }
                done.Add(c.Name);
                if (c.ProcessRunning) restart.Add(c.Name);
            }

            if (manual.Count > 0 && connect) Clipboard.SetText(McpClients.HttpSnippet);
            var parts = new List<string>();
            if (done.Count > 0) parts.Add((connect ? UiText.T("已连接：", "Connected: ") : UiText.T("已断开：", "Disconnected: ")) + string.Join("、", done));
            if (restart.Count > 0) parts.Add(UiText.T("请重启以生效：", "Restart to apply: ") + string.Join("、", restart));
            if (manual.Count > 0 && connect) parts.Add(UiText.T("需手动粘贴（配置已复制到剪贴板）：", "Paste manually (config copied): ") + string.Join("、", manual));
            if (failed.Count > 0) parts.Add(UiText.T("失败：", "Failed: ") + string.Join("；", failed));
            message.Text = parts.Count > 0 ? string.Join("\n", parts) : UiText.T("没有勾选任何客户端。", "No client selected.");
            Scan();
        }

        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        buttons.Children.Add(PrefButton(UiText.T("扫描", "Scan"), () => { Scan(); message.Text = UiText.T("已扫描本机 AI 客户端。", "Scanned this PC."); }));
        buttons.Children.Add(PrefButton(UiText.T("一键连接 AI（连接所选）", "Connect selected"), () => Apply(true), primary: true));
        buttons.Children.Add(PrefButton(UiText.T("断开", "Disconnect"), () => Apply(false)));
        host.Children.Add(buttons);
        host.Children.Add(list);
        host.Children.Add(message);
        Scan();
        return host;
    }
}
