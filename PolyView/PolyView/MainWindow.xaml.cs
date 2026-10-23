using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace PolyView;

public partial class MainWindow : Window
{
    string? _notice;
    DateTime _noticeUntil;

    public MainWindow()
    {
        InitializeComponent();
        Viewport.ViewChanged += (_, _) => UpdateStatus();
        Outliner.Attach(Viewport);
        ApplyLanguage();

        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        // 性能测试：PolyView.exe --bench 模型路径 结果文件（追加一行制表符分隔的结果后自动退出）
        if (args.Length >= 3 && args[0] == "--bench")
        {
            Viewport.StartBenchmark(System.IO.Path.GetFullPath(args[1]), System.IO.Path.GetFullPath(args[2]));
            return;
        }

        // 支持「打开方式」或命令行：PolyView.exe 模型1 [模型2 ...]，多个文件一起打开
        var startup = args.Where(a => MeshImporter.IsSupported(a) && System.IO.File.Exists(a))
            .Select(System.IO.Path.GetFullPath)
            .ToArray();
        if (startup.Length > 0)
        {
            Viewport.LoadPaths(startup);
        }
    }

    void ApplyLanguage()
    {
        Title = UiText.WindowTitle;
        OpenButton.Content = UiText.Open;
        ResetButton.Content = UiText.Reset;
        LanguageButton.Content = UiText.Language;
        HintText.Text = UiText.Hint;
        Outliner.ApplyLanguage();
        Viewport.ApplyToolbarLanguage();
        UpdateStatus();
        UpdateProjection();
    }

    void UpdateProjection()
    {
        ProjectionText.Text = Viewport.Orthographic ? UiText.Ortho : UiText.Perspective;
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase)
        {
            return;
        }

        // Alt 组合键时 WPF 给的是 Key.System，真实按键在 SystemKey 里。
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Viewport.HandleKey(key, Keyboard.Modifiers))
        {
            e.Handled = true;
            UpdateProjection();
        }
    }

    void UpdateStatus()
    {
        if (_notice != null && DateTime.Now < _noticeUntil)
        {
            StatusText.Text = _notice;
            return;
        }

        _notice = null;
        if (!string.IsNullOrEmpty(Viewport.Failure))
        {
            StatusText.Text = Viewport.Failure;
            return;
        }

        if (!Viewport.HasMesh)
        {
            StatusText.Text = UiText.Reading;
            return;
        }

        StatusText.Text = UiText.Ready(
            Viewport.MeshName,
            Viewport.VertexCount,
            Viewport.TriangleCount);
        if (Viewport.MeshNotice.Length > 0)
        {
            StatusText.Text += "    " + Viewport.MeshNotice;
        }
    }

    void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = UiText.FileFilter, Multiselect = true };
        if (dialog.ShowDialog(this) == true)
        {
            // 多选：第一个替换场景，其余追加为新对象（各自有 id，可点选）。
            Viewport.LoadPaths(dialog.FileNames);
        }
    }

    void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        Viewport.ResetView();
    }

    void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        UiText.Chinese = !UiText.Chinese;
        ApplyLanguage();
    }

    void ShowNotice(string text)
    {
        _notice = text;
        _noticeUntil = DateTime.Now.AddSeconds(4);
        StatusText.Text = text;
    }

    void Window_DragEnter(object sender, DragEventArgs e)
    {
        Window_DragOver(sender, e);
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && !HasModelFile(e))
        {
            // 不支持的文件拖不进来（光标显示禁止），提前在状态栏说明原因。
            ShowNotice(UiText.DropRejected);
        }
    }

    void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasModelFile(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void Window_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var paths = ModelFiles(e);
        if (paths.Length == 0)
        {
            ShowNotice(UiText.DropRejected);
            return;
        }

        _notice = null;
        // 一次拖入多个文件：一起打开（第一个替换场景，其余追加）。
        Viewport.LoadPaths(paths);
        Activate();
    }

    static bool HasModelFile(DragEventArgs e) => ModelFiles(e).Length > 0;

    static string[] ModelFiles(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return [];
        }

        return files.Where(MeshImporter.IsSupported).ToArray();
    }
}
