using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PolyView;

/// <summary>
/// Blender-style colour picker popup: saturation / value square, hue bar and a hex field (hex is sRGB, like Blender).
/// Values in and out are linear RGB. changed() fires live while dragging; closed() fires once when the popup closes.
/// </summary>
public static class ColorPicker
{
    public static void Open(FrameworkElement anchor, Vector4 linear, Action<Vector3> changed, Action closed)
    {
        var srgb = new Vector3(ToSrgb(linear.X), ToSrgb(linear.Y), ToSrgb(linear.Z));
        RgbToHsv(srgb, out double h, out double s, out double v);
        const double size = 170;
        var hueBrush = new SolidColorBrush(HsvColor(h, 1, 1));
        var square = new Grid { Width = size, Height = size, Cursor = Cursors.Cross, ClipToBounds = true };
        square.Children.Add(new Rectangle { Fill = hueBrush });
        square.Children.Add(new Rectangle { Fill = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0) });
        square.Children.Add(new Rectangle { Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90) });
        var marker = new Ellipse { Width = 10, Height = 10, Stroke = Brushes.White, StrokeThickness = 1.5, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        square.Children.Add(marker);

        var hueStops = new GradientStopCollection();
        for (int i = 0; i <= 6; i++) hueStops.Add(new GradientStop(HsvColor(i / 6.0, 1, 1), i / 6.0));
        var hueBar = new Grid { Width = 18, Height = size, Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand, ClipToBounds = true };
        hueBar.Children.Add(new Rectangle { Fill = new LinearGradientBrush(hueStops, new Point(0, 0), new Point(0, 1)) });
        var hueMarker = new Border { Height = 3, BorderBrush = Brushes.White, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        hueBar.Children.Add(hueMarker);

        var hex = new TextBox { Width = 90, Margin = new Thickness(0, 8, 0, 0), Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)), Foreground = Brushes.White, CaretBrush = Brushes.White, BorderThickness = new Thickness(0), Padding = new Thickness(4, 2, 4, 2) };
        var hexRow = new StackPanel { Orientation = Orientation.Horizontal };
        hexRow.Children.Add(new TextBlock { Text = UiText.T("十六进制", "Hex"), Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 8, 0) });
        hexRow.Children.Add(hex);

        void Sync(bool notify)
        {
            hueBrush.Color = HsvColor(h, 1, 1);
            marker.Margin = new Thickness(s * size - 5, (1 - v) * size - 5, 0, 0);
            hueMarker.Margin = new Thickness(0, h * size - 1.5, 0, 0);
            var c = HsvColor(h, s, v);
            if (!hex.IsKeyboardFocusWithin) hex.Text = $"{c.R:X2}{c.G:X2}{c.B:X2}";
            if (notify) changed(new Vector3(ToLinear(c.R / 255f), ToLinear(c.G / 255f), ToLinear(c.B / 255f)));
        }

        void DragSquare(Point p) { s = Math.Clamp(p.X / size, 0, 1); v = Math.Clamp(1 - p.Y / size, 0, 1); Sync(true); }
        void DragHue(Point p) { h = Math.Clamp(p.Y / size, 0, 0.9999); Sync(true); }
        square.MouseLeftButtonDown += (_, e) => { square.CaptureMouse(); DragSquare(e.GetPosition(square)); e.Handled = true; };
        square.MouseMove += (_, e) => { if (square.IsMouseCaptured) DragSquare(e.GetPosition(square)); };
        square.MouseLeftButtonUp += (_, _) => square.ReleaseMouseCapture();
        hueBar.MouseLeftButtonDown += (_, e) => { hueBar.CaptureMouse(); DragHue(e.GetPosition(hueBar)); e.Handled = true; };
        hueBar.MouseMove += (_, e) => { if (hueBar.IsMouseCaptured) DragHue(e.GetPosition(hueBar)); };
        hueBar.MouseLeftButtonUp += (_, _) => hueBar.ReleaseMouseCapture();
        hex.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            var t = hex.Text.Trim().TrimStart('#');
            if (t.Length == 6 && int.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
            {
                RgbToHsv(new Vector3(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f), out h, out s, out v);
                Keyboard.ClearFocus();
                Sync(true);
            }

            e.Handled = true;
        };

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(square);
        top.Children.Add(hueBar);
        var body = new StackPanel { Margin = new Thickness(10) };
        body.Children.Add(top);
        body.Children.Add(hexRow);
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            Child = new Border { Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Child = body },
        };
        popup.Closed += (_, _) => closed();
        Sync(false);
        popup.IsOpen = true;
    }

    static float ToSrgb(float c) { c = Math.Clamp(c, 0f, 1f); return c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f; }

    static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    static Color HsvColor(double h, double s, double v)
    {
        double r = v, g = v, b = v;
        if (s > 0)
        {
            double hh = (h % 1) * 6; int i = (int)hh; double f = hh - i;
            double p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
            (r, g, b) = i switch { 0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t), 3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q) };
        }

        return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    static void RgbToHsv(Vector3 c, out double h, out double s, out double v)
    {
        double max = Math.Max(c.X, Math.Max(c.Y, c.Z)), min = Math.Min(c.X, Math.Min(c.Y, c.Z)), d = max - min;
        v = max;
        s = max <= 0 ? 0 : d / max;
        if (d <= 0) { h = 0; return; }
        if (max == c.X) h = ((c.Y - c.Z) / d) % 6;
        else if (max == c.Y) h = (c.Z - c.X) / d + 2;
        else h = (c.X - c.Y) / d + 4;
        h /= 6;
        if (h < 0) h += 1;
    }
}
