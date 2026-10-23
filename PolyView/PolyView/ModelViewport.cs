using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using D3D9 = Vortice.Direct3D9;
using Vortice.DXGI;

namespace PolyView;

public sealed class ModelViewport : UserControl
{
    readonly Image _image = new() { Stretch = Stretch.Fill };
    readonly D3DImage _d3dImage = new();
    SceneRenderer? _renderer;
    D3D9.IDirect3D9Ex? _d3d9;
    D3D9.IDirect3DDevice9Ex? _device9;
    D3D9.IDirect3DTexture9? _sharedTexture;
    OrbitCamera _camera = OrbitCamera.Default();
    Vector3 _scale = Vector3.One;
    MeshData? _mesh;
    Point _lastPoint;
    DragMode _drag;
    int _loadVersion;
    bool _resizePending = true;
    string? _failure;

    public event EventHandler? ViewChanged;

    public string MeshName => _mesh?.Name ?? "";
    public int VertexCount => _mesh?.Vertices.Length ?? 0;
    public int TriangleCount => _mesh?.TriangleCount ?? 0;
    public double FrameMilliseconds { get; private set; }
    public string? Failure => _failure;
    public bool HasMesh => _mesh != null;

    public ModelViewport()
    {
        _image.Source = _d3dImage;
        Content = _image;
        Background = new SolidColorBrush(Color.FromRgb(28, 31, 36));
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => _resizePending = true;
    }

    public void ShowSample()
    {
        ApplyMesh(MeshBuilder.CreateBar());
    }

    public void LoadPath(string path)
    {
        int version = ++_loadVersion;
        Task.Run(() => MeshImporter.Load(path)).ContinueWith(task =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (version != _loadVersion)
                {
                    return;
                }

                var result = task.Result;
                if (!result.Ok || result.Mesh == null)
                {
                    _failure = result.Error;
                    ViewChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }

                _failure = null;
                ApplyMesh(result.Mesh);
            });
        });
    }

    public void ResetView()
    {
        if (_mesh == null)
        {
            return;
        }

        _camera.Frame(_mesh.Min, _mesh.Max);
        _scale = Vector3.One;
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetScale(float uniform, float stretchX)
    {
        _scale = new Vector3(uniform * stretchX, uniform, uniform);
    }

    void ApplyMesh(MeshData mesh)
    {
        _mesh = mesh;
        _renderer?.SetMesh(mesh);
        _camera.Frame(mesh.Min, mesh.Max);
        _scale = Vector3.One;
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _renderer = SceneRenderer.Create();
            CreateD3D9();
            if (_mesh == null)
            {
                ShowSample();
            }
            else
            {
                _renderer.SetMesh(_mesh);
            }

            CompositionTarget.Rendering += OnRendering;
        }
        catch (Exception ex)
        {
            _failure = ex.Message;
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        _sharedTexture?.Dispose();
        _device9?.Dispose();
        _d3d9?.Dispose();
        _renderer?.Dispose();
        _sharedTexture = null;
        _device9 = null;
        _d3d9 = null;
        _renderer = null;
    }

    void OnRendering(object? sender, EventArgs e)
    {
        if (_renderer == null || _device9 == null || !_d3dImage.IsFrontBufferAvailable)
        {
            return;
        }

        if (_resizePending || _d3dImage.PixelWidth == 0)
        {
            ResizeSurface();
            _resizePending = false;
        }

        if (_renderer.ColorTexture == null)
        {
            return;
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        float radius = _mesh == null ? 1f : (_mesh.Max - _mesh.Min).Length() * 0.5f;
        _renderer.Render(_camera, _scale, radius);
        _d3dImage.Lock();
        _d3dImage.AddDirtyRect(new Int32Rect(0, 0, _d3dImage.PixelWidth, _d3dImage.PixelHeight));
        _d3dImage.Unlock();
        watch.Stop();
        FrameMilliseconds = watch.Elapsed.TotalMilliseconds;
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    void ResizeSurface()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        int width = Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX));
        int height = Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY));
        _renderer!.Resize(width, height);
        _sharedTexture?.Dispose();
        using var resource = _renderer.ColorTexture!.QueryInterface<IDXGIResource>();
        var handle = resource.SharedHandle;
        _sharedTexture = _device9!.CreateTexture(
            (uint)width,
            (uint)height,
            1,
            D3D9.Usage.RenderTarget,
            D3D9.Format.A8R8G8B8,
            D3D9.Pool.Default,
            ref handle);
        using var surface = _sharedTexture.GetSurfaceLevel(0);
        _d3dImage.Lock();
        _d3dImage.SetBackBuffer(D3DResourceType.IDirect3DSurface9, surface.NativePointer, true);
        _d3dImage.Unlock();
    }

    void CreateD3D9()
    {
        _d3d9 = D3D9.D3D9.Direct3DCreate9Ex();
        var present = new D3D9.PresentParameters
        {
            Windowed = true,
            SwapEffect = D3D9.SwapEffect.Discard,
            DeviceWindowHandle = GetDesktopWindow(),
            PresentationInterval = D3D9.PresentInterval.Immediate,
            BackBufferWidth = 1,
            BackBufferHeight = 1,
            BackBufferCount = 1,
            BackBufferFormat = D3D9.Format.Unknown,
        };
        var flags = D3D9.CreateFlags.HardwareVertexProcessing | D3D9.CreateFlags.Multithreaded | D3D9.CreateFlags.FpuPreserve;
        D3D9.PresentParameters[] parameters = [present];
        _device9 = _d3d9.CreateDeviceEx(0, D3D9.DeviceType.Hardware, IntPtr.Zero, flags, parameters);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        BeginDrag(DragMode.Orbit, e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        EndDrag(DragMode.Orbit);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            BeginDrag(DragMode.Pan, e);
        }
        else
        {
            base.OnMouseDown(e);
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            EndDrag(DragMode.Pan);
        }
        else
        {
            base.OnMouseUp(e);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag == DragMode.None)
        {
            return;
        }

        var point = e.GetPosition(this);
        var delta = point - _lastPoint;
        _lastPoint = point;
        if (_drag == DragMode.Orbit)
        {
            _camera.Orbit((float)delta.X * 0.01f, (float)delta.Y * 0.01f);
        }
        else
        {
            float pan = _camera.Distance * 0.002f;
            _camera.Pan((float)delta.X * pan, (float)delta.Y * pan);
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        _camera.Zoom(e.Delta);
        e.Handled = true;
    }

    void BeginDrag(DragMode mode, MouseButtonEventArgs e)
    {
        _drag = mode;
        _lastPoint = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    void EndDrag(DragMode mode)
    {
        if (_drag != mode)
        {
            return;
        }

        _drag = DragMode.None;
        ReleaseMouseCapture();
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetDesktopWindow();

    enum DragMode
    {
        None,
        Orbit,
        Pan,
    }
}
