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
        AppSettings.ApplyStoredLanguage();
        InitializeComponent();
        DarkTitleBar.Apply(this);
        // Language now lives in Edit > Preferences; the old toggle is hidden.
        MenuLanguage.Visibility = Visibility.Collapsed;
        MenuPreferences.IsEnabled = true;
        MenuPreferences.Click += (_, _) => new PreferencesWindow(ApplyLanguage) { Owner = this }.ShowDialog();
        Viewport.ViewChanged += (_, _) => UpdateStatus();
        Outliner.Attach(Viewport);
        ApplyLanguage();
        InitProject();
        InitMcp();

        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        // 性能测试：PolyView.exe --bench 模型路径 结果文件（追加一行制表符分隔的结果后自动退出）
        if (args.Length >= 2 && args[0] == "--project-selftest")
        {
            Environment.Exit(Project.ProjectSelfTest.Run(System.IO.Path.GetFullPath(args[1])));
        }

        if (args.Length >= 3 && args[0] == "--bench")
        {
            Viewport.StartBenchmark(System.IO.Path.GetFullPath(args[1]), System.IO.Path.GetFullPath(args[2]));
            return;
        }

        // 支持「打开方式」或命令行：PolyView.exe 模型1 [模型2 ...]，多个文件一起打开
        var startup = OpenProjectsIn(args.Where(System.IO.File.Exists)).Where(a => MeshImporter.IsSupported(a) && System.IO.File.Exists(a))
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
        ApplyProjectLanguage();
        MenuFile.Header = UiText.MenuFile;
        MenuNew.Header = UiText.MenuNew;
        MenuOpen.Header = UiText.MenuOpen;
        MenuRecent.Header = UiText.MenuRecent;
        MenuSave.Header = UiText.MenuSave;
        MenuImport.Header = UiText.MenuImport;
        MenuExport.Header = UiText.MenuExport;
        MenuQuit.Header = UiText.MenuQuit;
        MenuEdit.Header = UiText.MenuEdit;
        MenuUndo.Header = UiText.MenuUndo;
        MenuRedo.Header = UiText.MenuRedo;
        MenuResetView.Header = UiText.Reset;
        MenuLanguage.Header = UiText.MenuLanguage;
        MenuPreferences.Header = UiText.MenuPreferences;
        MenuAdd.Header = UiText.MenuAdd;
        MenuAdd.Items.Clear();
        Viewport.PopulateAddMenu(MenuAdd.Items);
        MenuRender.Header = UiText.MenuRender;
        MenuRenderImage.Header = UiText.MenuRenderImage;
        MenuRenderAnimation.Header = UiText.MenuRenderAnimation;
        MenuViewRender.Header = UiText.MenuViewRender;
        MenuWindow.Header = UiText.MenuWindow;
        MenuNewWindow.Header = UiText.MenuNewWindow;
        MenuFullscreen.Header = UiText.MenuFullscreen;
        MenuProjection.Header = UiText.MenuProjection;
        MenuHelp.Header = UiText.MenuHelp;
        MenuManual.Header = UiText.MenuManual;
        MenuAbout.Header = UiText.MenuAbout;
        BuildWorkspaceTabs();
        HintText.Text = UiText.Hint;
        Outliner.ApplyLanguage();
        Viewport.ApplyToolbarLanguage();
        Viewport.ApplyShadingLanguage();
        UpdateStatus();
        UpdateProjection();
    }

    /// <summary>Blender workspace tabs: only Layout exists; the rest are shown greyed out.</summary>
    void BuildWorkspaceTabs()
    {
        WorkspaceTabs.Children.Clear();
        var names = UiText.Workspaces;
        for (int i = 0; i < names.Length; i++)
        {
            bool active = i == 0;
            WorkspaceTabs.Children.Add(new System.Windows.Controls.Border
            {
                Style = (Style)FindResource("WorkspaceTab"),
                Background = active ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x47, 0x47, 0x47)) : System.Windows.Media.Brushes.Transparent,
                IsEnabled = active,
                Child = new System.Windows.Controls.TextBlock
                {
                    Text = names[i],
                    FontSize = 12,
                    Foreground = active
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE6, 0xE6, 0xE6))
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6E, 0x6E, 0x6E)),
                },
            });
        }
    }

    void UpdateProjection()
    {
        ProjectionText.Text = Viewport.Orthographic ? UiText.ViewOrtho : UiText.ViewPerspective;
    }

    void MenuQuit_Click(object sender, RoutedEventArgs e) => Close();

    void MenuUndo_Click(object sender, RoutedEventArgs e) => Viewport.Undo();

    void MenuRedo_Click(object sender, RoutedEventArgs e) => Viewport.Redo();

    void MenuProjection_Click(object sender, RoutedEventArgs e)
    {
        Viewport.HandleKey(Key.NumPad5, ModifierKeys.None);
        UpdateProjection();
    }

    WindowState _stateBeforeFullscreen = WindowState.Normal;

    void MenuFullscreen_Click(object sender, RoutedEventArgs e)
    {
        if (WindowStyle == WindowStyle.None)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = _stateBeforeFullscreen;
        }
        else
        {
            _stateBeforeFullscreen = WindowState;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
        }
    }

    void MenuAbout_Click(object sender, RoutedEventArgs e) =>
        PvMessageDialog.Show(this, UiText.AboutText, UiText.MenuAbout, MessageBoxButton.OK, MessageBoxImage.Information);

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase && !Viewport.IsTransforming)
        {
            return;
        }

        // Alt 组合键时 WPF 给的是 Key.System，真实按键在 SystemKey 里。
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (TryHandleProjectKey(key, Keyboard.Modifiers))
        {
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.O)
        {
            OpenButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && key == Key.Q)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && key == Key.Space)
        {
            MenuFullscreen_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

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
        var dialog = new OpenFileDialog { Filter = UiText.OpenAnyFilter + UiText.FileFilter, Multiselect = true };
        if (dialog.ShowDialog(this) == true)
        {
            // 多选：第一个替换场景，其余追加为新对象（各自有 id，可点选）。
            var models = OpenProjectsIn(dialog.FileNames);
            if (models.Length > 0)
            {
                Viewport.LoadPaths(models);
            }
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
        var models = OpenProjectsIn(paths);
        if (models.Length > 0)
        {
            Viewport.LoadPaths(models);
        }
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

        return files.Where(IsOpenable).ToArray();
    }
}
