using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PolyView.Project;

namespace PolyView;

/// <summary>Project files (.pvproj): Save (Ctrl+S), Save As (Ctrl+Shift+S), Open, title with * when modified, ask on close.</summary>
public partial class MainWindow
{
    readonly ProjectSettings _settings = ProjectSettings.Load();
    string? _projectPath;
    bool _projectDirty;
    bool _restoring;
    MenuItem? _menuSaveAs;

    void InitProject()
    {
        Viewport.SceneChanged += (_, _) => MarkProjectDirty();
        Viewport.ObjectChanged += (_, _) => MarkProjectDirty();
        Closing += OnProjectClosing;
        FileAssociation.EnsureRegistered();
        MenuSave.IsEnabled = true;
        MenuSave.Click += (_, _) => SaveProject(saveAs: false);
        _menuSaveAs = new MenuItem { InputGestureText = "Ctrl+Shift+S" };
        _menuSaveAs.Click += (_, _) => SaveProject(saveAs: true);
        MenuFile.Items.Insert(MenuFile.Items.IndexOf(MenuSave) + 1, _menuSaveAs);
        // The startup sample is not a user change.
        Dispatcher.BeginInvoke(() => { _projectDirty = false; UpdateProjectTitle(); }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        ApplyProjectLanguage();
    }

    void ApplyProjectLanguage()
    {
        if (_menuSaveAs != null)
        {
            _menuSaveAs.Header = UiText.MenuSaveAs;
        }

        UpdateProjectTitle();
    }

    void MarkProjectDirty()
    {
        if (_restoring || _projectDirty)
        {
            return;
        }

        _projectDirty = true;
        UpdateProjectTitle();
    }

    void UpdateProjectTitle()
    {
        var name = _projectPath == null ? UiText.Untitled : Path.GetFileName(_projectPath);
        Title = $"{(_projectDirty ? "* " : "")}{name} - {UiText.WindowTitle}";
    }

    /// <summary>Ctrl+S / Ctrl+Shift+S. Returns true when handled.</summary>
    bool TryHandleProjectKey(Key key, ModifierKeys modifiers)
    {
        if (key != Key.S || Viewport.IsTransforming)
        {
            return false;
        }

        if (modifiers == ModifierKeys.Control)
        {
            SaveProject(saveAs: false);
            return true;
        }

        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            SaveProject(saveAs: true);
            return true;
        }

        return false;
    }

    bool SaveProject(bool saveAs)
    {
        var path = _projectPath;
        if (saveAs || path == null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = UiText.ProjectFilter,
                DefaultExt = ProjectPackage.Extension,
                AddExtension = true,
                InitialDirectory = path != null ? Path.GetDirectoryName(path) : _settings.DefaultProjectFolder(),
                FileName = path != null ? Path.GetFileName(path) : UiText.Untitled + ProjectPackage.Extension,
            };
            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            path = dialog.FileName;
            if (!ProjectPackage.IsProjectFile(path))
            {
                path += ProjectPackage.Extension;
            }
        }

        try
        {
            var doc = Viewport.CaptureProject();
            Outliner.CaptureProject(doc);
            var options = ProjectSaveOptions.Default;
            ProjectPackage.Write(path, doc, options.WriteThumbnail ? CaptureThumbnail() : null, options);
            _projectPath = path;
            _projectDirty = false;
            RememberProject(path);
            UpdateProjectTitle();
            ShowNotice(UiText.ProjectSaved(path));
            return true;
        }
        catch (Exception ex)
        {
            App.LogException("SaveProject", ex);
            PvMessageDialog.Show(this, UiText.ProjectSaveFailed(ex.Message), "PolyView", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    void RememberProject(string path)
    {
        _settings.LastProjectFolder = Path.GetDirectoryName(Path.GetFullPath(path));
        _settings.RecentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _settings.RecentProjects.Insert(0, path);
        if (_settings.RecentProjects.Count > 10)
        {
            _settings.RecentProjects.RemoveRange(10, _settings.RecentProjects.Count - 10);
        }

        _settings.Save();
    }

    byte[]? CaptureThumbnail()
    {
        try
        {
            if (Viewport.ActualWidth < 2 || Viewport.ActualHeight < 2)
            {
                return null;
            }

            double scale = Math.Min(256 / Viewport.ActualWidth, 256 / Viewport.ActualHeight);
            var bitmap = new RenderTargetBitmap(
                Math.Max(1, (int)(Viewport.ActualWidth * scale)), Math.Max(1, (int)(Viewport.ActualHeight * scale)), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(Viewport);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            App.LogException("CaptureThumbnail", ex);
            return null;
        }
    }

    /// <summary>Asks to save when there are unsaved changes. False = the user cancelled.</summary>
    bool ConfirmDiscardChanges()
    {
        if (!_projectDirty)
        {
            return true;
        }

        var name = _projectPath == null ? UiText.Untitled : Path.GetFileName(_projectPath);
        var answer = PvMessageDialog.Show(this, UiText.AskSaveChanges(name), "PolyView", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => SaveProject(saveAs: false),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    void OnProjectClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            e.Cancel = true;
        }
    }

    /// <summary>Opens .pvproj files from a list (dialog / drop / command line). Returns the remaining non-project paths.</summary>
    string[] OpenProjectsIn(IEnumerable<string> paths)
    {
        var list = paths.ToArray();
        var project = list.FirstOrDefault(ProjectPackage.IsProjectFile);
        if (project != null && File.Exists(project))
        {
            OpenProject(Path.GetFullPath(project));
        }

        return list.Where(p => !ProjectPackage.IsProjectFile(p)).ToArray();
    }

    void OpenProject(string path)
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        ShowNotice(UiText.ProjectOpening);
        Task.Run(() =>
        {
            var (doc, assets, missing) = ProjectPackage.Read(path);
            var imports = ModelViewport.ImportProjectAssets(doc, assets);
            return (doc, assets, missing, imports);
        }).ContinueWith(task => Dispatcher.BeginInvoke(() =>
        {
            if (task.IsFaulted)
            {
                var ex = task.Exception!.GetBaseException();
                App.LogException("OpenProject", ex);
                PvMessageDialog.Show(this, ex is ProjectVersionException ? UiText.ProjectTooNew : UiText.ProjectOpenFailed(ex.Message),
                    "PolyView", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var (doc, assets, missing, imports) = task.Result;
            _restoring = true;
            List<string> failed;
            try
            {
                var (map, f) = Viewport.RestoreProject(doc, assets, imports);
                failed = f;
                Outliner.RestoreProject(doc, map);
            }
            finally
            {
                _restoring = false;
            }

            _projectPath = path;
            _projectDirty = false;
            RememberProject(path);
            UpdateProjectTitle();
            UpdateProjection();
            ShowNotice(UiText.ProjectOpened(Path.GetFileName(path)));
            var problems = missing.Concat(failed).Distinct().ToList();
            if (problems.Count > 0)
            {
                PvMessageDialog.Show(this, UiText.ProjectMissing(string.Join("\n", problems)), "PolyView", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            Activate();
        }));
    }

    static bool IsOpenable(string path) => MeshImporter.IsSupported(path) || ProjectPackage.IsProjectFile(path);
}
