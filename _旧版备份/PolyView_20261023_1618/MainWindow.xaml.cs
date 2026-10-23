using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace PolyView;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Viewport.ViewChanged += (_, _) => UpdateStatus();
        ApplyLanguage();
    }

    void ApplyLanguage()
    {
        Title = UiText.WindowTitle;
        OpenButton.Content = UiText.Open;
        ResetButton.Content = UiText.Reset;
        LanguageButton.Content = UiText.Language;
        ScaleLabel.Text = UiText.Scale;
        StretchLabel.Text = UiText.Stretch;
        HintText.Text = UiText.Hint;
        UpdateStatus();
    }

    void UpdateStatus()
    {
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
            Viewport.TriangleCount,
            Viewport.FrameMilliseconds);
    }

    void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = UiText.FileFilter };
        if (dialog.ShowDialog(this) == true)
        {
            ResetSliders();
            Viewport.LoadPath(dialog.FileName);
        }
    }

    void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        ResetSliders();
        Viewport.ResetView();
    }

    void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        UiText.Chinese = !UiText.Chinese;
        ApplyLanguage();
    }

    void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
        {
            return;
        }

        Viewport.SetScale((float)ScaleSlider.Value, (float)StretchSlider.Value);
    }

    void StretchSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded)
        {
            return;
        }

        Viewport.SetScale((float)ScaleSlider.Value, (float)StretchSlider.Value);
    }

    void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasModelFile(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void Window_Drop(object sender, DragEventArgs e)
    {
        var path = FirstModelFile(e);
        if (path == null)
        {
            StatusText.Text = UiText.DropRejected;
            return;
        }

        ResetSliders();
        Viewport.LoadPath(path);
    }

    void ResetSliders()
    {
        ScaleSlider.Value = 1;
        StretchSlider.Value = 1;
    }

    static bool HasModelFile(DragEventArgs e) => FirstModelFile(e) != null;

    static string? FirstModelFile(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return null;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return null;
        }

        foreach (var file in files)
        {
            var extension = System.IO.Path.GetExtension(file);
            if (extension.Equals(".obj", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".stl", StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        return null;
    }
}
