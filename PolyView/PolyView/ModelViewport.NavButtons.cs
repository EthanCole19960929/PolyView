using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace PolyView;

/// <summary>
/// Blender navigation buttons on the right under the axis gizmo: zoom (drag), pan (drag), camera view (Numpad 0)
/// and perspective / orthographic (Numpad 5). Camera view frames the scene through the active camera (the selected
/// camera, else the first camera) at its render aspect, with a Blender passepartout around the frame.
/// </summary>
public sealed partial class ModelViewport
{
    const double NavButtonSize = 28;

    StackPanel? _navButtons;
    Point _navButtonLast;
    int _navButtonDrag = -1; // 0 zoom, 1 pan
    bool _cameraView;
    OrbitCamera _cameraViewCamera;
    OrbitCamera _beforeCameraView;
    float _cameraAspect = 16f / 9f;
    Canvas? _passCanvas;
    Path? _passShade;
    Rectangle? _passFrame;
    TextBlock? _viewMessage;

    /// <summary>True while looking through the scene camera (Numpad 0).</summary>
    public bool InCameraView => _cameraView;

    void EnsureNavButtons()
    {
        if (_navButtons != null || Content is not Panel panel)
        {
            return;
        }

        _navButtons = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            // Centred under the 84 px axis gizmo.
            Margin = new Thickness(0, HeaderHeight + 12 + 84 + 6, 12 + (84 - NavButtonSize) / 2, 0),
        };
        _navButtons.Children.Add(NavButton(UiText.NavZoom, ZoomIcon(), dragMode: 0, click: null));
        _navButtons.Children.Add(NavButton(UiText.NavPan, HandIcon(), dragMode: 1, click: null));
        _navButtons.Children.Add(NavButton(UiText.NavCamera, CameraIcon(), dragMode: -1, click: () => ToggleCameraView()));
        _navButtons.Children.Add(NavButton(UiText.NavOrtho, GridIcon(), dragMode: -1, click: ToggleOrtho));

        _passShade = new Path { Fill = new SolidColorBrush(Color.FromArgb(0x80, 0x10, 0x10, 0x10)), IsHitTestVisible = false };
        _passFrame = new Rectangle { Stroke = new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)), StrokeThickness = 1, IsHitTestVisible = false };
        _passCanvas = new Canvas { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _passCanvas.Children.Add(_passShade);
        _passCanvas.Children.Add(_passFrame);
        _viewMessage = new TextBlock
        {
            Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x20, 0x20, 0x20)), Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, HeaderHeight + 40, 0, 0),
            IsHitTestVisible = false, Opacity = 0,
        };

        // Passepartout under the overlays, buttons on top.
        panel.Children.Insert(Math.Min(1, panel.Children.Count), _passCanvas);
        panel.Children.Add(_viewMessage);
        panel.Children.Add(_navButtons);
        SizeChanged += (_, _) => { if (_cameraView) ApplyCameraFrame(); };
    }

    Border NavButton(string tip, UIElement icon, int dragMode, Action? click)
    {
        var normal = new SolidColorBrush(Color.FromArgb(0x60, 0x20, 0x20, 0x20));
        var hover = new SolidColorBrush(Color.FromArgb(0xA0, 0x50, 0x50, 0x50));
        var b = new Border
        {
            Width = NavButtonSize, Height = NavButtonSize, CornerRadius = new CornerRadius(NavButtonSize / 2), Background = normal,
            Margin = new Thickness(0, 0, 0, 6), Child = icon, ToolTip = tip, Cursor = dragMode >= 0 ? Cursors.SizeAll : Cursors.Hand,
        };
        b.MouseEnter += (_, _) => b.Background = hover;
        b.MouseLeave += (_, _) => { if (_navButtonDrag < 0) b.Background = normal; };
        b.MouseLeftButtonDown += (_, e) =>
        {
            Focus();
            if (dragMode >= 0)
            {
                _navButtonDrag = dragMode;
                _navButtonLast = e.GetPosition(this);
                b.CaptureMouse();
            }

            e.Handled = true;
        };
        b.MouseMove += (_, e) =>
        {
            if (_navButtonDrag < 0 || !b.IsMouseCaptured)
            {
                return;
            }

            var p = e.GetPosition(this);
            var d = p - _navButtonLast;
            _navButtonLast = p;
            if (_navButtonDrag == 0)
            {
                // Blender zoom button: drag down / right zooms in... drag up zooms out.
                _camera.ZoomDrag((float)(-d.Y - d.X));
            }
            else
            {
                _camera.Pan((float)d.X, (float)d.Y, (float)ActualHeight);
            }

            Invalidate();
            ViewChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        };
        b.MouseLeftButtonUp += (_, e) =>
        {
            bool wasDrag = _navButtonDrag >= 0;
            _navButtonDrag = -1;
            if (b.IsMouseCaptured)
            {
                b.ReleaseMouseCapture();
            }

            if (!b.IsMouseOver)
            {
                b.Background = normal;
            }

            if (!wasDrag)
            {
                click?.Invoke();
            }

            e.Handled = true;
        };
        return b;
    }

    // ---------------- perspective / orthographic ----------------

    /// <summary>Numpad 5. True orthographic projection; the ortho half height equals the perspective half height at the target, so the framing does not jump.</summary>
    public void ToggleOrtho()
    {
        _camera.Orthographic = !_camera.Orthographic;
        if (_cameraView)
        {
            _cameraViewCamera = _camera;
        }

        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------------- camera view ----------------

    /// <summary>The camera to look through: the selected camera object, else the first camera in the scene.</summary>
    bool TryGetActiveCamera(out SceneObject obj, out SceneNode node)
    {
        foreach (var o in _objects.OrderBy(o => o.Id == _selectedId ? 0 : 1))
        {
            if (!o.Visible && o.Id != _selectedId)
            {
                continue;
            }

            foreach (var n in o.Mesh.Nodes)
            {
                if (n.Kind == SceneNodeKind.Camera && n.Camera != null)
                {
                    obj = o;
                    node = n;
                    return true;
                }
            }
        }

        obj = null!;
        node = null!;
        return false;
    }

    /// <summary>Numpad 0 / camera button. Returns false (and shows a message) when the scene has no camera.</summary>
    public bool ToggleCameraView()
    {
        EnsureNavButtons();
        if (_cameraView)
        {
            ExitCameraView(restore: true);
            return true;
        }

        if (!TryGetActiveCamera(out var o, out var node))
        {
            ShowViewMessage(UiText.NavNoCamera);
            return false;
        }

        var cam = node.Camera!;
        var m = node.World * Matrix4x4.CreateTranslation(-o.ImportOffset) * o.World * Matrix4x4.CreateScale(_scale);
        var eye = Vector3.Transform(Vector3.Zero, m);
        var forward = Vector3.TransformNormal(-Vector3.UnitZ, m);
        if (forward.LengthSquared() < 1e-12f)
        {
            forward = -Vector3.UnitZ;
        }

        forward = Vector3.Normalize(forward);
        _cameraAspect = cam.AspectRatio > 0 ? cam.AspectRatio : 16f / 9f;
        _beforeCameraView = _camera;
        float distance = MathF.Max(0.1f, Vector3.Dot((_sceneMin + _sceneMax) * 0.5f * _scale - eye, forward));
        var c = _camera;
        c.Target = eye + forward * distance;
        c.Distance = distance;
        c.Pitch = Math.Clamp(MathF.Asin(Math.Clamp(-forward.Y, -1f, 1f)), -OrbitCamera.MaxPitch, OrbitCamera.MaxPitch);
        c.Yaw = MathF.Atan2(-forward.X, -forward.Z);
        c.Orthographic = cam.Orthographic;
        _cameraYFov = cam.Orthographic ? 0 : (cam.YFov > 0 ? cam.YFov : 2f * MathF.Atan(18f / 50f / _cameraAspect));
        _cameraOrthoHalf = cam.OrthoHalfHeight;
        _camera = c;
        _cameraView = true;
        ApplyCameraFrame();
        CompositionTarget.Rendering += WatchCameraView;
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    float _cameraYFov;
    float _cameraOrthoHalf;

    /// <summary>Fit the camera frame (render aspect) into the viewport with a margin and widen the view so the frame shows exactly the camera's field of view.</summary>
    Rect CameraFrameRect()
    {
        // Centred on the viewport (the projection centre), clear of the header.
        double w = Math.Max(1, ActualWidth), h = Math.Max(1, ActualHeight);
        double fh = Math.Max(10, Math.Min(h - 2 * (HeaderHeight + 16), w * 0.86 / _cameraAspect));
        double fw = fh * _cameraAspect;
        return new Rect((w - fw) / 2, (h - fh) / 2, fw, fh);
    }

    void ApplyCameraFrame()
    {
        var frame = CameraFrameRect();
        float fraction = (float)(frame.Height / Math.Max(1, ActualHeight));
        if (_camera.Orthographic)
        {
            float half = _cameraOrthoHalf > 0 ? _cameraOrthoHalf : _camera.OrthoHalfHeight;
            _camera.FieldOfView = 2f * MathF.Atan(half / fraction / _camera.Distance);
        }
        else
        {
            _camera.FieldOfView = 2f * MathF.Atan(MathF.Tan(_cameraYFov * 0.5f) / fraction);
        }

        _cameraViewCamera = _camera;
        UpdatePassepartout();
    }

    void UpdatePassepartout()
    {
        if (_passCanvas == null || _passShade == null || _passFrame == null)
        {
            return;
        }

        if (!_cameraView)
        {
            _passCanvas.Visibility = Visibility.Collapsed;
            return;
        }

        var frame = CameraFrameRect();
        var outer = new RectangleGeometry(new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)));
        var inner = new RectangleGeometry(frame);
        _passShade.Data = new GeometryGroup { FillRule = FillRule.EvenOdd, Children = { outer, inner } };
        Canvas.SetLeft(_passFrame, frame.X);
        Canvas.SetTop(_passFrame, frame.Y);
        _passFrame.Width = frame.Width;
        _passFrame.Height = frame.Height;
        _passCanvas.Visibility = Visibility.Visible;
    }

    void WatchCameraView(object? sender, EventArgs e)
    {
        if (!_cameraView)
        {
            CompositionTarget.Rendering -= WatchCameraView;
            return;
        }

        // Orbit / pan / zoom / view keys leave camera view (keep the new view, like Blender).
        var a = _camera;
        var b = _cameraViewCamera;
        if (a.Target != b.Target || a.Yaw != b.Yaw || a.Pitch != b.Pitch || a.Distance != b.Distance || a.Orthographic != b.Orthographic)
        {
            ExitCameraView(restore: false);
        }
    }

    void ExitCameraView(bool restore)
    {
        if (!_cameraView)
        {
            return;
        }

        _cameraView = false;
        CompositionTarget.Rendering -= WatchCameraView;
        if (restore)
        {
            _camera = _beforeCameraView;
        }
        else
        {
            _camera.FieldOfView = _beforeCameraView.FieldOfView;
        }

        UpdatePassepartout();
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    void ShowViewMessage(string text)
    {
        EnsureNavButtons();
        if (_viewMessage == null)
        {
            return;
        }

        _viewMessage.Text = text;
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.5))));
        _viewMessage.BeginAnimation(OpacityProperty, fade);
    }

    // ---------------- icons ----------------

    static Path Icon(string data, bool fill = false) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = fill ? null : Brushes.White,
        Fill = fill ? Brushes.White : null,
        StrokeThickness = 1.4,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        Width = 16,
        Height = 16,
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false,
    };

    static Path ZoomIcon() => Icon("M6.5,1.5 A5,5 0 1 1 6.49,1.5 Z M10.2,10.2 L14.5,14.5 M4.5,6.5 H8.5 M6.5,4.5 V8.5");
    static Path HandIcon() => Icon("M4.5,8 V3.5 A1,1 0 0 1 6.5,3.5 V7.5 V2.5 A1,1 0 0 1 8.5,2.5 V7.5 V3 A1,1 0 0 1 10.5,3 V8 V5 A1,1 0 0 1 12.5,5 V10 C12.5,13 10.5,14.5 8.5,14.5 C6.5,14.5 5.5,13.5 4,11.5 L2.2,9 A1,1 0 0 1 3.8,7.8 L4.5,8.7");
    static Path CameraIcon() => Icon("M1.5,4.5 H10.5 V11.5 H1.5 Z M10.5,6.5 L14.5,4.5 V11.5 L10.5,9.5");
    static Path GridIcon() => Icon("M1.5,1.5 H14.5 V14.5 H1.5 Z M5.8,1.5 V14.5 M10.2,1.5 V14.5 M1.5,5.8 H14.5 M1.5,10.2 H14.5");
}
