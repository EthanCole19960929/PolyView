using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PolyView;

/// <summary>
/// Blender navigation gizmo (viewport top-right): positive axes are filled circles with a letter and a line
/// from the center, negative axes are hollow circles. Rotates with the view and is depth-sorted.
/// Click an axis to snap the view to it; drag to orbit. Hover shows a translucent disc.
/// </summary>
public sealed partial class ModelViewport
{
    const double NavSize = 84;
    const double NavAxisLength = 30;
    const double NavBallRadius = 9;

    readonly Canvas _nav = new()
    {
        Width = NavSize,
        Height = NavSize,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, HeaderHeight + 12, 12, 0),
        Background = Brushes.Transparent,
        Cursor = Cursors.Hand,
    };

    readonly Ellipse _navDisc = new()
    {
        Width = NavSize,
        Height = NavSize,
        Fill = new SolidColorBrush(Color.FromArgb(0x38, 0xC8, 0xC8, 0xC8)),
        Visibility = Visibility.Hidden,
        IsHitTestVisible = false,
    };

    readonly Line[] _navLines = new Line[3];
    readonly Ellipse[] _navBalls = new Ellipse[6];
    readonly TextBlock[] _navLabels = new TextBlock[3];
    Point _navDown;
    Point _navLast;
    bool _navPressed;
    bool _navDragged;
    int _navPressedAxis = -1;

    // Blender axes in app space (Y-up): X = X, Blender Y = app -Z, Blender Z = app Y. Order: +X -X +Y -Y +Z -Z.
    static readonly Vector3[] NavDirections =
    [
        Vector3.UnitX, -Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitY, -Vector3.UnitY,
    ];

    static readonly Color[] NavColors =
    [
        Color.FromRgb(0xFF, 0x33, 0x52),
        Color.FromRgb(0x8B, 0xDC, 0x00),
        Color.FromRgb(0x28, 0x90, 0xFF),
    ];

    UIElement BuildNavGizmo()
    {
        _nav.Children.Add(_navDisc);
        string[] letters = ["X", "Y", "Z"];
        for (int a = 0; a < 3; a++)
        {
            var color = NavColors[a];
            _navLines[a] = new Line { Stroke = new SolidColorBrush(color), StrokeThickness = 2, IsHitTestVisible = false };
            _nav.Children.Add(_navLines[a]);
            for (int sign = 0; sign < 2; sign++)
            {
                int index = a * 2 + sign;
                _navBalls[index] = new Ellipse
                {
                    Width = NavBallRadius * 2,
                    Height = NavBallRadius * 2,
                    Fill = sign == 0
                        ? new SolidColorBrush(color)
                        : new SolidColorBrush(Color.FromArgb(0x55, color.R, color.G, color.B)),
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = sign == 0 ? 0 : 1.5,
                    Tag = index,
                };
                _nav.Children.Add(_navBalls[index]);
            }

            _navLabels[a] = new TextBlock
            {
                Text = letters[a],
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)),
                Width = NavBallRadius * 2,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false,
            };
            _nav.Children.Add(_navLabels[a]);
        }

        _nav.MouseEnter += (_, _) => _navDisc.Visibility = Visibility.Visible;
        _nav.MouseLeave += (_, _) =>
        {
            if (!_navPressed)
            {
                _navDisc.Visibility = Visibility.Hidden;
            }
        };
        _nav.MouseLeftButtonDown += NavMouseDown;
        _nav.MouseMove += NavMouseMove;
        _nav.MouseLeftButtonUp += NavMouseUp;
        _nav.LostMouseCapture += (_, _) =>
        {
            _navPressed = false;
            if (!_nav.IsMouseOver)
            {
                _navDisc.Visibility = Visibility.Hidden;
            }
        };
        UpdateNavGizmo();
        return _nav;
    }

    /// <summary>Re-place the circles for the current view (called before each rendered frame).</summary>
    void UpdateNavGizmo()
    {
        var toTarget = _camera.Target - _camera.Eye;
        if (toTarget.LengthSquared() < 1e-12f || !float.IsFinite(toTarget.X + toTarget.Y + toTarget.Z))
        {
            toTarget = -Vector3.UnitZ;
        }

        var forward = Vector3.Normalize(toTarget);
        var right = Vector3.Cross(forward, Vector3.UnitY);
        right = right.LengthSquared() < 1e-8f ? Vector3.UnitX : Vector3.Normalize(right);
        var up = Vector3.Cross(right, forward);
        double c = NavSize / 2;
        for (int i = 0; i < 6; i++)
        {
            var d = NavDirections[i];
            double x = c + Vector3.Dot(d, right) * NavAxisLength;
            double y = c - Vector3.Dot(d, up) * NavAxisLength;
            float depth = -Vector3.Dot(d, forward); // larger = closer to the viewer
            int z = 10 + (int)(depth * 100);
            var ball = _navBalls[i];
            Canvas.SetLeft(ball, x - NavBallRadius);
            Canvas.SetTop(ball, y - NavBallRadius);
            Panel.SetZIndex(ball, z);
            if (i % 2 == 0)
            {
                int a = i / 2;
                var line = _navLines[a];
                line.X1 = c;
                line.Y1 = c;
                line.X2 = x;
                line.Y2 = y;
                Panel.SetZIndex(line, z - 1);
                var label = _navLabels[a];
                Canvas.SetLeft(label, x - NavBallRadius);
                Canvas.SetTop(label, y - 8);
                Panel.SetZIndex(label, z + 1);
            }
        }
    }

    void NavMouseDown(object sender, MouseButtonEventArgs e)
    {
        _navDown = _navLast = e.GetPosition(_nav);
        _navPressed = true;
        _navDragged = false;
        _navPressedAxis = e.OriginalSource is Ellipse { Tag: int index } ? index : -1;
        _nav.CaptureMouse();
        e.Handled = true;
    }

    void NavMouseMove(object sender, MouseEventArgs e)
    {
        if (!_navPressed)
        {
            return;
        }

        var p = e.GetPosition(_nav);
        if (!_navDragged && (p - _navDown).Length < 3)
        {
            return;
        }

        _navDragged = true;
        var delta = p - _navLast;
        _navLast = p;
        _camera.Orbit((float)delta.X * 0.01f, (float)delta.Y * 0.01f);
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    void NavMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_navPressed)
        {
            return;
        }

        bool click = !_navDragged;
        _nav.ReleaseMouseCapture();
        _navPressed = false;
        e.Handled = true;
        if (!click || _navPressedAxis < 0)
        {
            return;
        }

        // Same as Numpad 3 / Ctrl+3, Ctrl+1 / 1, 7 / Ctrl+7: view from the clicked axis toward the center.
        switch (_navPressedAxis)
        {
            case 0: SetView(MathF.PI / 2f, 0f); break;
            case 1: SetView(-MathF.PI / 2f, 0f); break;
            case 2: SetView(MathF.PI, 0f); break;
            case 3: SetView(0f, 0f); break;
            case 4: SetView(0f, OrbitCamera.MaxPitch); break;
            default: SetView(0f, -OrbitCamera.MaxPitch); break;
        }

        ViewChanged?.Invoke(this, EventArgs.Empty);
    }
}
