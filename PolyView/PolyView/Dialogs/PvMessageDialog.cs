using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace PolyView;

/// <summary>Blender 4.2 style dark message dialog. Drop-in replacement for MessageBox.Show.</summary>
public sealed class PvMessageDialog : Window
{
    static readonly Brush Panel = Hex("#303030"), BorderB = Hex("#3A3A3A"), TitleBg = Hex("#262626"),
        TextB = Hex("#E6E6E6"), Btn = Hex("#545454"), BtnHover = Hex("#656565"),
        Primary = Hex("#4772B3"), PrimaryHover = Hex("#5680C2"), IconB = Hex("#CFCFCF");

    MessageBoxResult _result;
    readonly MessageBoxResult _cancelResult;

    public static MessageBoxResult Show(string text, string caption = "PolyView",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
        => Show(null, text, caption, buttons, image);

    public static MessageBoxResult Show(Window? owner, string text, string caption = "PolyView",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        owner ??= Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                  ?? Application.Current?.MainWindow;
        var dlg = new PvMessageDialog(text, caption, buttons, image);
        if (owner != null && owner.IsLoaded && owner != dlg) { dlg.Owner = owner; dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
        return dlg._result;
    }

    /// <summary>Dialog with custom buttons (label, result, primary). Esc / the close button return cancelResult; Enter picks the primary button.</summary>
    public static MessageBoxResult ShowCustom(Window? owner, string text, string caption, MessageBoxImage image,
        (string Label, MessageBoxResult Result, bool Primary)[] buttons, MessageBoxResult cancelResult)
    {
        owner ??= Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                  ?? Application.Current?.MainWindow;
        var dlg = new PvMessageDialog(text, caption, MessageBoxButton.OK, image, buttons, cancelResult);
        if (owner != null && owner.IsLoaded && owner != dlg) { dlg.Owner = owner; dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        else dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowDialog();
        return dlg._result;
    }

    PvMessageDialog(string text, string caption, MessageBoxButton buttons, MessageBoxImage image,
        (string, MessageBoxResult, bool)[]? custom = null, MessageBoxResult customCancel = MessageBoxResult.Cancel)
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight; ShowInTaskbar = false;
        Title = caption; FontSize = 13; FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        UseLayoutRounding = true; SnapsToDevicePixels = true;

        var defs = custom ?? buttons switch
        {
            MessageBoxButton.YesNoCancel => new[] { (UiText.DlgSave, MessageBoxResult.Yes, true), (UiText.DlgDontSave, MessageBoxResult.No, false), (UiText.DlgCancel, MessageBoxResult.Cancel, false) },
            MessageBoxButton.YesNo => new[] { (UiText.DlgYes, MessageBoxResult.Yes, true), (UiText.DlgNo, MessageBoxResult.No, false) },
            MessageBoxButton.OKCancel => new[] { (UiText.DlgOk, MessageBoxResult.OK, true), (UiText.DlgCancel, MessageBoxResult.Cancel, false) },
            _ => new[] { (UiText.DlgOk, MessageBoxResult.OK, true) },
        };
        _cancelResult = custom != null ? customCancel : buttons switch
        {
            MessageBoxButton.YesNoCancel or MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            _ => MessageBoxResult.OK,
        };
        _result = _cancelResult;

        // Title row
        var titleText = new TextBlock { Text = caption, Foreground = TextB, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var close = new TextBlock { Text = "\u2715", Foreground = IconB, FontSize = 11, Width = 30, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        close.MouseLeftButtonUp += (_, _) => Finish(_cancelResult);
        var titleGrid = new DockPanel { Height = 28, Background = TitleBg };
        DockPanel.SetDock(close, Dock.Right);
        titleGrid.Children.Add(close); titleGrid.Children.Add(titleText);
        titleGrid.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        var titleBorder = new Border { Child = titleGrid, CornerRadius = new CornerRadius(6, 6, 0, 0), ClipToBounds = true, Background = TitleBg };

        // Body
        var body = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 16, 22, 8) };
        var icon = MakeIcon(image);
        if (icon != null) body.Children.Add(icon);
        body.Children.Add(new TextBlock { Text = text, Foreground = TextB, TextWrapping = TextWrapping.Wrap, MaxWidth = 460, MinWidth = 220, VerticalAlignment = VerticalAlignment.Center, LineHeight = 19 });

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(14, 10, 14, 14) };
        foreach (var (label, res, primary) in defs) btnRow.Children.Add(MakeButton(label, res, primary));

        var content = new StackPanel();
        content.Children.Add(titleBorder); content.Children.Add(body); content.Children.Add(btnRow);

        Content = new Border
        {
            Margin = new Thickness(14), Background = Panel, BorderBrush = BorderB, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Child = content,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.55, Color = Colors.Black },
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Finish(_cancelResult); e.Handled = true; }
            else if (e.Key == Key.Enter) { Finish((defs.FirstOrDefault(x => x.Item3) is var p && p.Item1 != null ? p : defs[0]).Item2); e.Handled = true; }
        };
    }

    void Finish(MessageBoxResult r) { _result = r; Close(); }

    UIElement MakeButton(string label, MessageBoxResult res, bool primary)
    {
        var normal = primary ? Primary : Btn; var hover = primary ? PrimaryHover : BtnHover;
        var b = new Border
        {
            Background = normal, CornerRadius = new CornerRadius(4), MinWidth = 84, Height = 26, Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(12, 0, 12, 0), Cursor = Cursors.Hand,
            Child = new TextBlock { Text = label, Foreground = TextB, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        b.MouseEnter += (_, _) => b.Background = hover;
        b.MouseLeave += (_, _) => b.Background = normal;
        b.MouseLeftButtonUp += (_, _) => Finish(res);
        return b;
    }

    static UIElement? MakeIcon(MessageBoxImage image)
    {
        if (image == MessageBoxImage.None) return null;
        var c = new Canvas { Width = 24, Height = 24, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top };
        Brush stroke = image switch { MessageBoxImage.Error => Hex("#E06C6C"), MessageBoxImage.Warning => Hex("#E5B54A"), _ => IconB };
        string glyph = image switch { MessageBoxImage.Question => "?", MessageBoxImage.Information => "i", _ => "!" };
        Shape shape = image == MessageBoxImage.Warning
            ? new Polygon { Points = new PointCollection { new(12, 2), new(23, 22), new(1, 22) } }
            : new Ellipse { Width = 22, Height = 22 };
        shape.Stroke = stroke; shape.StrokeThickness = 1.6; shape.StrokeLineJoin = PenLineJoin.Round;
        if (shape is Ellipse) { Canvas.SetLeft(shape, 1); Canvas.SetTop(shape, 1); }
        c.Children.Add(shape);
        var t = new TextBlock { Text = glyph, Foreground = stroke, FontWeight = FontWeights.Bold, FontSize = 13, Width = 24, TextAlignment = TextAlignment.Center };
        Canvas.SetTop(t, image == MessageBoxImage.Warning ? 6 : 3);
        c.Children.Add(t);
        return c;
    }

    static Brush Hex(string s) { var b = (Brush)new BrushConverter().ConvertFromString(s)!; b.Freeze(); return b; }
}
