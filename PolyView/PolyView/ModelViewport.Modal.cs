using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace PolyView;

/// <summary>Blender modal-transform visuals: white outline, header text, dashed line from origin to the cursor and a custom cursor icon.</summary>
public sealed partial class ModelViewport
{
    static readonly Brush ModalWhite = MakeFrozen(Color.FromRgb(0xF0, 0xF0, 0xF0));
    static readonly Brush ModalOrange = MakeFrozen(Color.FromRgb(0xFF, 0xA0, 0x28));

    readonly Canvas _modalCanvas = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly Line _modalLine = new()
    {
        Stroke = ModalWhite,
        StrokeThickness = 1,
        StrokeDashArray = [4, 3],
        SnapsToDevicePixels = true,
    };

    readonly Ellipse _modalDot = new() { Width = 5, Height = 5, Fill = ModalOrange };

    // Double arrow (scale) and curved arrow (rotate), centred on (0,0), white with dark edge.
    readonly Path _modalCursor = new()
    {
        Fill = ModalWhite,
        Stroke = MakeFrozen(Color.FromRgb(0x10, 0x10, 0x10)),
        StrokeThickness = 0.8,
        RenderTransform = new RotateTransform(),
    };

    static readonly Geometry ScaleCursor = Geometry.Parse("M-11,0 L-6,-4.5 L-6,-1.5 L6,-1.5 L6,-4.5 L11,0 L6,4.5 L6,1.5 L-6,1.5 L-6,4.5 Z");
    static readonly Geometry RotateCursor = Geometry.Parse("M-8,3 A9,9 0 0 1 8,3 L10.5,0.5 L11,8 L4,6.5 L6.3,4.7 A7,7 0 0 0 -6.3,4.7 L-4,6.5 L-11,8 L-10.5,0.5 Z");

    static Brush MakeFrozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    bool _modalOverlayInit;

    void UpdateModalOverlay(Point mouse, Vector3 originWorld)
    {
        if (_renderer != null)
        {
            _renderer.OutlineWhite = true;
        }

        _modalText.Visibility = string.IsNullOrEmpty(_modalText.Text) ? Visibility.Collapsed : Visibility.Visible;
        if (!_modalOverlayInit)
        {
            _modalCanvas.Children.Add(_modalLine);
            _modalCanvas.Children.Add(_modalDot);
            _modalCanvas.Children.Add(_modalCursor);
            _modalOverlayInit = true;
        }

        bool showLine = _xformTool is TransformTool.Scale or TransformTool.Rotate;
        var origin = Project(originWorld);
        if (!showLine || origin == null)
        {
            _modalCanvas.Visibility = Visibility.Collapsed;
            Cursor = null;
            return;
        }

        var o = origin.Value;
        _modalCanvas.Visibility = Visibility.Visible;
        _modalLine.X1 = o.X;
        _modalLine.Y1 = o.Y;
        _modalLine.X2 = mouse.X;
        _modalLine.Y2 = mouse.Y;
        Canvas.SetLeft(_modalDot, o.X - 2.5);
        Canvas.SetTop(_modalDot, o.Y - 2.5);
        _modalCursor.Data = _xformTool == TransformTool.Scale ? ScaleCursor : RotateCursor;
        Canvas.SetLeft(_modalCursor, mouse.X);
        Canvas.SetTop(_modalCursor, mouse.Y);
        double angle = Math.Atan2(mouse.Y - o.Y, mouse.X - o.X) * 180 / Math.PI;
        // Scale arrow points along the line; rotate arrow is tangent to the circle around the origin.
        ((RotateTransform)_modalCursor.RenderTransform).Angle = _xformTool == TransformTool.Scale ? angle : angle + 90;
        Cursor = Cursors.None;
    }

    void HideModalOverlay()
    {
        if (_renderer != null)
        {
            _renderer.OutlineWhite = false;
        }

        _modalCanvas.Visibility = Visibility.Collapsed;
        _modalText.Visibility = Visibility.Collapsed;
        Cursor = null;
        Invalidate();
    }
}