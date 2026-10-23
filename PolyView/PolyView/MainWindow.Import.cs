using System.IO;
using System.Windows;
using Microsoft.Win32;
using PolyView.Import;

namespace PolyView;

/// <summary>
/// Blender-style import: File > Import and dropping model files open the import options panel, then ADD the models
/// to the current scene (undoable, marks the project dirty, saved in .pvproj). Dropping a .pvproj still opens the project.
/// </summary>
public partial class MainWindow
{
    ImportOptions _lastImportOptions = ImportOptions.Default;

    void InitImport()
    {
        MenuImport.IsEnabled = true;
        MenuImport.Click += (_, _) => ImportWithFileDialog();
        // Runs before Window_Drop and handles the event, so a drop no longer replaces the scene.
        PreviewDrop += OnImportDrop;
    }

    void OnImportDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        e.Handled = true;
        var paths = ModelFiles(e);
        if (paths.Length == 0)
        {
            ShowNotice(UiText.DropRejected);
            return;
        }

        _notice = null;
        Activate();
        // .pvproj files open as projects (with the unsaved-changes prompt); model files are returned for import.
        var models = OpenProjectsIn(paths);
        if (models.Length > 0)
        {
            ImportWithDialog(models);
        }
    }

    void ImportWithFileDialog()
    {
        var dialog = new OpenFileDialog { Filter = UiText.FileFilter, Multiselect = true };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var models = dialog.FileNames.Where(MeshImporter.IsSupported).ToArray();
        if (models.Length > 0)
        {
            ImportWithDialog(models);
        }
    }

    /// <summary>One options panel for all files, then import them all (loading in the background).</summary>
    void ImportWithDialog(IReadOnlyList<string> paths)
    {
        var options = ImportOptionsDialog.Ask(this, paths, _lastImportOptions);
        if (options == null)
        {
            return;
        }

        _lastImportOptions = options;
        var list = paths.Select(Path.GetFullPath).ToArray();
        ShowNotice(UiText.ImportBusy);
        Task.Run(() => Array.ConvertAll(list, MeshImporter.Load)).ContinueWith(task => Dispatcher.BeginInvoke(() =>
        {
            var failed = new List<string>();
            int added = 0;
            if (task.IsFaulted)
            {
                failed.Add(task.Exception?.GetBaseException().Message ?? "?");
            }
            else
            {
                for (int i = 0; i < list.Length; i++)
                {
                    var r = task.Result[i];
                    if (!r.Ok || r.Mesh == null)
                    {
                        failed.Add($"{Path.GetFileName(list[i])}: {r.Error}");
                        continue;
                    }

                    try
                    {
                        Viewport.ImportMesh(r.Mesh, list[i], options);
                        added++;
                    }
                    catch (Exception ex)
                    {
                        App.LogException("Import", ex);
                        failed.Add($"{Path.GetFileName(list[i])}: {ex.Message}");
                    }
                }
            }

            ShowNotice(UiText.ImportDone(added));
            if (failed.Count > 0)
            {
                PvMessageDialog.Show(this, UiText.ImportFailed(string.Join("\n", failed)), UiText.ImportFailedTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }));
    }

    /// <summary>
    /// Import API for AI / MCP: adds the model to the current scene (no dialog), selects it and returns the new object.
    /// Throws InvalidDataException / ArgumentException with a readable message on failure.
    /// </summary>
    public SceneObject ImportModel(string path, ImportOptions? options = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.Invoke(() => ImportModel(path, options));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(path);
        }

        if (!MeshImporter.IsSupported(path))
        {
            throw new InvalidDataException("unsupported format: " + string.Join(" ", MeshImporter.SupportedExtensions));
        }

        return Viewport.ImportFile(path, options ?? ImportOptions.Default);
    }
}
