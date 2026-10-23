using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PolyView.Export;

namespace PolyView;

/// <summary>File > Export: save dialog, export through SceneExporter, errors in PvMessageDialog.</summary>
public partial class MainWindow
{
    readonly ExportSettings _exportSettings = ExportSettings.Load();

    void InitExport()
    {
        MenuExport.IsEnabled = true;
        MenuExport.Click += (_, _) => ExportWithDialog();
        InitImport();
    }

    /// <summary>
    /// Export API for AI / MCP: writes the scene (or only the selected object) to path.
    /// format: fbx / obj / gltf / glb / stl / ply / dae (null = from the extension). Throws ExportException on failure.
    /// </summary>
    public ExportResult ExportScene(string path, string? format = null, bool selectionOnly = false)
    {
        if (!Dispatcher.CheckAccess())
        {
            return Dispatcher.Invoke(() => ExportScene(path, format, selectionOnly));
        }

        return Viewport.ExportScene(path, format, selectionOnly);
    }

    /// <summary>Export only the selected object (falls back to the whole scene when nothing is selected).</summary>
    public ExportResult ExportSelection(string path, string? format = null) => ExportScene(path, format, selectionOnly: true);

    void ExportWithDialog()
    {
        if (!Viewport.HasMesh)
        {
            PvMessageDialog.Show(this, UiText.ExportNothing, UiText.ExportFailedTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool selection = Viewport.SelectedId >= 0;
        var formats = ExportFormat.All;
        int filterIndex = Array.FindIndex(formats, f => f.Key == _exportSettings.LastFormat);
        var name = selection && Viewport.TryGetObjectInfo(Viewport.SelectedId, out var info)
            ? Path.GetFileNameWithoutExtension(info.Name)
            : _projectPath != null ? Path.GetFileNameWithoutExtension(_projectPath) : UiText.Untitled;
        var dialog = new SaveFileDialog
        {
            Title = selection ? UiText.DlgExportSelection : UiText.DlgExport,
            Filter = UiText.ExportFilter,
            FilterIndex = filterIndex < 0 ? 1 : filterIndex + 1,
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = _exportSettings.InitialFolder(),
            FileName = name,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var fmt = ExportFormat.FromPath(dialog.FileName)
            ?? formats[Math.Clamp(dialog.FilterIndex - 1, 0, formats.Length - 1)];
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var result = Viewport.ExportScene(dialog.FileName, fmt.Key, selection);
            _exportSettings.LastFolder = Path.GetDirectoryName(result.Path);
            _exportSettings.LastFormat = fmt.Key;
            _exportSettings.Save();
            ShowNotice(UiText.ExportDone(result.Path, result.ObjectCount, result.TriangleCount));
        }
        catch (Exception ex)
        {
            App.LogException("Export", ex);
            Mouse.OverrideCursor = null;
            PvMessageDialog.Show(this, ex is ExportException ? ex.Message : UiText.ExportAssimpFailed(ex.Message),
                UiText.ExportFailedTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
}
