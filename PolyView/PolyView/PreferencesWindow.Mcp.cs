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
            head.Children.Add(Cell(UiText.T("AI \u5ba2\u6237\u7aef", "AI client"), DimBrush, 190));
            head.Children.Add(Cell(UiText.T("\u72b6\u6001", "Status"), DimBrush, 120));
            list.Children.Add(head);
            foreach (var c in _mcpClients.OrderByDescending(c => c.Installed))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                row.Children.Add(Cell(c.Name, c.Installed ? Brushes.White : DimBrush, 190));
                var cc = c;
                if (!c.Installed)
                {
                    row.Children.Add(Cell(UiText.T("\u672a\u5b89\u88c5", "Not installed"), DimBrush, 120));
                }
                else if (c.CanAuto)
                {
                    bool on = McpClients.AutoEnabled(c);
                    row.Children.Add(on
                        ? Cell(c.Connected ? UiText.T("\u2714 \u5df2\u81ea\u52a8\u8fde\u63a5", "\u2714 Auto-connected") : UiText.T("\u8fde\u63a5\u5931\u8d25", "Not connected"), c.Connected ? OkBrush : WarnBrush, 120)
                        : Cell(UiText.T("\u5df2\u5173\u95ed", "Off"), DimBrush, 120));
                    var cb = new CheckBox { Content = UiText.T("\u81ea\u52a8\u8fde\u63a5", "Auto-connect"), IsChecked = on, Foreground = DimBrush, Style = (Style)FindResource("PrefCheck"), VerticalAlignment = VerticalAlignment.Center };
                    cb.Click += (_, _) =>
                    {
                        var off = AppSettings.Current.McpAutoConnectOff;
                        off.Remove(cc.Id);
                        if (cb.IsChecked != true) off.Add(cc.Id);
                        AppSettings.Current.Save();
                        var err = cb.IsChecked == true ? McpClients.Connect(cc) : McpClients.Disconnect(cc);
                        message.Text = err != null ? cc.Name + ": " + err : cc.ProcessRunning ? UiText.T($"\u8bf7\u91cd\u542f {cc.Name} \u4ee5\u751f\u6548\u3002", $"Restart {cc.Name} to apply.") : "";
                        Scan();
                    };
                    row.Children.Add(cb);
                }
                else
                {
                    row.Children.Add(Cell(UiText.T("\u9700\u624b\u52a8\u7c98\u8d34", "Paste manually"), WarnBrush, 120));
                    var btn = PrefButton(UiText.T("\u590d\u5236\u914d\u7f6e", "Copy config"), () => { Clipboard.SetText(McpClients.SnippetFor(cc)); message.Text = UiText.T("\u5df2\u590d\u5236\u914d\u7f6e\u3002", "Config copied. ") + McpClients.ManualSteps(cc); });
                    btn.Height = 20;
                    row.Children.Add(btn);
                }

                row.ToolTip = c.ConfigPath ?? UiText.T("\u8be5\u5ba2\u6237\u7aef\u9700\u5728\u5176\u8bbe\u7f6e\u91cc\u624b\u52a8\u7c98\u8d34\u914d\u7f6e", "Paste the copied config in this client's settings");
                list.Children.Add(row);
            }
        }

        void Scan()
        {
            _mcpClients = McpClients.Scan();
            Fill();
        }

        host.Children.Add(new TextBlock { Text = UiText.T("PolyView \u542f\u52a8\u65f6\u4f1a\u81ea\u52a8\u8fde\u63a5\u5df2\u5b89\u88c5\u7684 AI\uff0c\u65e0\u9700\u4efb\u4f55\u64cd\u4f5c\uff1b\u7b2c\u4e00\u6b21\u8fde\u63a5\u540e\u9700\u91cd\u542f\u4e00\u6b21\u8be5 AI\u3002", "PolyView connects installed AI apps automatically on launch; restart an AI once after its first connection."),
            Foreground = DimBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        host.Children.Add(list);
        host.Children.Add(message);
        var refresh = PrefButton(UiText.T("\u5237\u65b0", "Refresh"), Scan);
        refresh.HorizontalAlignment = HorizontalAlignment.Left;
        refresh.Margin = new Thickness(0, 6, 0, 0);
        host.Children.Add(refresh);
        Scan();
        return host;
    }
}
