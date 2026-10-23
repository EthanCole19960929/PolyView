using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using D3D9 = Vortice.Direct3D9;
using Vortice.DXGI;

namespace PolyView;

public sealed class ModelViewport : UserControl
{
    readonly Image _image = new() { Stretch = Stretch.Fill };
    readonly D3DImage _d3dImage = new();
    readonly DispatcherTimer _fpsTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly System.Diagnostics.Stopwatch _fpsWatch = new();
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
    bool _dirty = true;
    bool _renderingHooked;
    string? _failure;
    int _framesInWindow;
    double _renderMsInWindow;
    BenchState? _bench;

    /// <summary>模型、错误信息变化时触发（不是每帧）。</summary>
    public event EventHandler? ViewChanged;

    /// <summary>帧率统计更新时触发，约每 0.4 秒一次。</summary>
    public event EventHandler? StatsChanged;

    public string MeshName => _mesh?.Name ?? "";
    public int VertexCount => _mesh?.Vertices.Length ?? 0;
    public int TriangleCount => _mesh?.TriangleCount ?? 0;
    public string MeshNotice => _mesh?.Notice ?? "";
    public string? Failure => _failure;
    public bool HasMesh => _mesh != null;
    public bool Orthographic => _camera.Orthographic;

    /// <summary>最近约 0.4 秒内的平均帧率；0 表示这段时间没有重画（空闲）。</summary>
    public double FramesPerSecond { get; private set; }

    /// <summary>最近一段时间里每帧提交渲染命令的平均 CPU 耗时（毫秒）。</summary>
    public double FrameMilliseconds { get; private set; }

    /// <summary>上一次打开模型时，读取 + 解析文件的耗时（毫秒）。</summary>
    public double LastLoadMilliseconds { get; private set; }

    /// <summary>上一次打开模型时，创建 GPU 缓冲和贴图的耗时（毫秒）。</summary>
    public double LastUploadMilliseconds { get; private set; }

    public string AdapterName => _renderer?.AdapterName ?? "";

    public ModelViewport()
    {
        _image.Source = _d3dImage;
        Content = _image;
        Focusable = true;
        Background = new SolidColorBrush(Color.FromRgb(28, 31, 36));
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) =>
        {
            _resizePending = true;
            Invalidate();
        };
        _d3dImage.IsFrontBufferAvailableChanged += (_, _) =>
        {
            // 锁屏、远程桌面切换等会丢掉前缓冲；恢复后重建共享表面并重画，避免黑屏。
            if (_d3dImage.IsFrontBufferAvailable)
            {
                _resizePending = true;
                Invalidate();
            }
        };
        _fpsTimer.Tick += OnFpsTick;
    }

    /// <summary>请求下一帧重画。画面没变化时不渲染，CPU / GPU 空闲。</summary>
    public void Invalidate() => _dirty = true;

    public void ShowSample()
    {
        ApplyMesh(MeshBuilder.CreateBar());
    }

    public void LoadPath(string path)
    {
        int version = ++_loadVersion;
        Task.Run(() =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = MeshImporter.Load(path);
            return (result, watch.Elapsed.TotalMilliseconds);
        }).ContinueWith(task =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (version != _loadVersion)
                {
                    return;
                }

                var (result, loadMs) = task.Result;
                if (!result.Ok || result.Mesh == null)
                {
                    _failure = result.Error;
                    ViewChanged?.Invoke(this, EventArgs.Empty);
                    _bench?.Fail(result.Error ?? "load failed");
                    return;
                }

                _failure = null;
                LastLoadMilliseconds = loadMs;
                ApplyMesh(result.Mesh);
            });
        });
    }

    public void ResetView()
    {
        _scale = Vector3.One;
        if (_mesh != null)
        {
            bool ortho = _camera.Orthographic;
            _camera.Frame(_mesh.Min, _mesh.Max);
            _camera.Orthographic = ortho;
        }

        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetScale(float uniform)
    {
        _scale = new Vector3(uniform);
        Invalidate();
    }

    /// <summary>Blender 风格快捷键。返回 true 表示已处理。</summary>
    public bool HandleKey(Key key, ModifierKeys modifiers)
    {
        bool ctrl = (modifiers & ModifierKeys.Control) != 0;
        switch (key)
        {
            case Key.Home:
            case Key.Decimal:
                FrameAll();
                return true;
            case Key.NumPad1:
                SetView(ctrl ? MathF.PI : 0f, 0f);
                return true;
            case Key.NumPad3:
                SetView(ctrl ? -MathF.PI / 2f : MathF.PI / 2f, 0f);
                return true;
            case Key.NumPad7:
                SetView(0f, ctrl ? -OrbitCamera.MaxPitch : OrbitCamera.MaxPitch);
                return true;
            case Key.NumPad5:
                _camera.Orthographic = !_camera.Orthographic;
                Invalidate();
                ViewChanged?.Invoke(this, EventArgs.Empty);
                return true;
            default:
                return false;
        }
    }

    void FrameAll()
    {
        if (_mesh == null)
        {
            return;
        }

        float yaw = _camera.Yaw;
        float pitch = _camera.Pitch;
        _camera.Frame(_mesh.Min, _mesh.Max);
        _camera.SetView(yaw, pitch);
        Invalidate();
    }

    void SetView(float yaw, float pitch)
    {
        _camera.SetView(yaw, pitch);
        Invalidate();
    }

    /// <summary>性能测试：打开模型后连续重画，记录帧率、GPU 单帧耗时和内存，写进 outFile 后退出。</summary>
    public void StartBenchmark(string path, string outFile)
    {
        _bench = new BenchState(path, outFile);
        LoadPath(path);
    }

    void ApplyMesh(MeshData mesh)
    {
        _mesh = mesh;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _renderer?.SetMesh(mesh);
        LastUploadMilliseconds = _renderer == null ? 0 : watch.Elapsed.TotalMilliseconds;
        bool ortho = _camera.Orthographic;
        _camera.Frame(mesh.Min, mesh.Max);
        _camera.Orthographic = ortho;
        _scale = Vector3.One;
        Invalidate();
        if (_bench != null && mesh.Name != "")
        {
            _bench.MeshReady(this);
        }

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
                if (_bench == null)
                {
                    ShowSample();
                }
            }
            else
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                _renderer.SetMesh(_mesh);
                LastUploadMilliseconds = watch.Elapsed.TotalMilliseconds;
            }

            if (!_renderingHooked)
            {
                CompositionTarget.Rendering += OnRendering;
                _renderingHooked = true;
            }

            _fpsWatch.Restart();
            _fpsTimer.Start();
            Invalidate();
        }
        catch (Exception ex)
        {
            _failure = ex.Message;
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_renderingHooked)
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderingHooked = false;
        }

        _fpsTimer.Stop();
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
        if (_bench != null && _bench.Running)
        {
            _camera.Yaw += 0.01f;
            _dirty = true;
        }

        if (!_dirty || _renderer == null || _device9 == null || !_d3dImage.IsFrontBufferAvailable)
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

        _dirty = false;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        RenderFrame();
        _d3dImage.Lock();
        _d3dImage.AddDirtyRect(new Int32Rect(0, 0, _d3dImage.PixelWidth, _d3dImage.PixelHeight));
        _d3dImage.Unlock();
        _renderMsInWindow += System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _framesInWindow++;
        _bench?.Frame(this);
    }

    void RenderFrame()
    {
        float radius = _mesh == null ? 1f : (_mesh.Max - _mesh.Min).Length() * 0.5f;
        _renderer!.Render(_camera, _scale, radius);
    }

    void OnFpsTick(object? sender, EventArgs e)
    {
        double seconds = _fpsWatch.Elapsed.TotalSeconds;
        _fpsWatch.Restart();
        double fps = seconds > 0 ? _framesInWindow / seconds : 0;
        double ms = _framesInWindow > 0 ? _renderMsInWindow / _framesInWindow : 0;
        _framesInWindow = 0;
        _renderMsInWindow = 0;
        if (Math.Abs(fps - FramesPerSecond) < 0.05 && Math.Abs(ms - FrameMilliseconds) < 0.05)
        {
            return;
        }

        FramesPerSecond = fps;
        FrameMilliseconds = ms;
        StatsChanged?.Invoke(this, EventArgs.Empty);
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

    // ---- 鼠标：Blender 风格。中键环绕，Shift+中键平移，Ctrl+中键缩放；左键拖动也能环绕（没有中键时用）。----

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        if (e.ChangedButton == MouseButton.Middle)
        {
            var modifiers = Keyboard.Modifiers;
            var mode = (modifiers & ModifierKeys.Shift) != 0 ? DragMode.Pan
                : (modifiers & ModifierKeys.Control) != 0 ? DragMode.Zoom
                : DragMode.Orbit;
            BeginDrag(mode, MouseButton.Middle, e);
        }
        else if (e.ChangedButton == MouseButton.Left && _drag == DragMode.None)
        {
            BeginDrag(DragMode.Orbit, MouseButton.Left, e);
        }
        else
        {
            base.OnMouseDown(e);
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_drag != DragMode.None && e.ChangedButton == _dragButton)
        {
            _drag = DragMode.None;
            ReleaseMouseCapture();
            e.Handled = true;
        }
        else
        {
            base.OnMouseUp(e);
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _drag = DragMode.None;
        base.OnLostMouseCapture(e);
    }

    MouseButton _dragButton;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag == DragMode.None)
        {
            return;
        }

        var point = e.GetPosition(this);
        var delta = point - _lastPoint;
        _lastPoint = point;
        switch (_drag)
        {
            case DragMode.Orbit:
                _camera.Orbit((float)delta.X * 0.01f, (float)delta.Y * 0.01f);
                break;
            case DragMode.Pan:
                _camera.Pan((float)delta.X, (float)delta.Y, (float)ActualHeight);
                break;
            case DragMode.Zoom:
                _camera.ZoomDrag((float)delta.Y);
                break;
        }

        Invalidate();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        _camera.Zoom(e.Delta);
        Invalidate();
        e.Handled = true;
    }

    void BeginDrag(DragMode mode, MouseButton button, MouseButtonEventArgs e)
    {
        _drag = mode;
        _dragButton = button;
        _lastPoint = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetDesktopWindow();

    enum DragMode
    {
        None,
        Orbit,
        Pan,
        Zoom,
    }

    /// <summary>--bench 模式的状态机：预热 → 按显示器刷新节奏连续画 3 秒 → 不等刷新连画 60 帧测 GPU 耗时 → 写结果退出。</summary>
    sealed class BenchState(string path, string outFile)
    {
        readonly System.Diagnostics.Stopwatch _watch = new();
        int _frames;
        int _phase; // 0 等模型，1 预热，2 计时，3 完成

        public bool Running => _phase is 1 or 2;

        public void MeshReady(ModelViewport viewport)
        {
            _phase = 1;
            _frames = 0;
            _watch.Restart();
        }

        public void Frame(ModelViewport viewport)
        {
            if (_phase == 1 && _watch.Elapsed.TotalSeconds >= 1.0)
            {
                _phase = 2;
                _frames = 0;
                _watch.Restart();
                return;
            }

            if (_phase != 2)
            {
                return;
            }

            _frames++;
            if (_watch.Elapsed.TotalSeconds < 3.0)
            {
                return;
            }

            double vsyncFps = _frames / _watch.Elapsed.TotalSeconds;
            _phase = 3;
            var renderer = viewport._renderer!;
            renderer.WaitForGpu();
            const int gpuFrames = 60;
            var gpuWatch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < gpuFrames; i++)
            {
                viewport._camera.Yaw += 0.01f;
                viewport.RenderFrame();
                renderer.WaitForGpu();
            }

            double gpuMs = gpuWatch.Elapsed.TotalMilliseconds / gpuFrames;
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            process.Refresh();
            var line = string.Join('\t',
                System.IO.Path.GetFileName(path),
                viewport.VertexCount,
                viewport.TriangleCount,
                viewport.LastLoadMilliseconds.ToString("0"),
                viewport.LastUploadMilliseconds.ToString("0"),
                vsyncFps.ToString("0.0"),
                gpuMs.ToString("0.00"),
                (1000.0 / gpuMs).ToString("0"),
                (process.WorkingSet64 / 1048576.0).ToString("0"),
                (process.PrivateMemorySize64 / 1048576.0).ToString("0"),
                $"{viewport._d3dImage.PixelWidth}x{viewport._d3dImage.PixelHeight}",
                viewport.AdapterName);
            System.IO.File.AppendAllText(outFile, line + Environment.NewLine);
            Application.Current.Shutdown();
        }

        public void Fail(string error)
        {
            System.IO.File.AppendAllText(outFile, $"{System.IO.Path.GetFileName(path)}\tFAILED\t{error}{Environment.NewLine}");
            Application.Current.Shutdown();
        }
    }
}
