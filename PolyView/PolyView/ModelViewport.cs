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

public sealed partial class ModelViewport : UserControl
{
    readonly Image _image = new() { Stretch = Stretch.Fill };
    readonly D3DImage _d3dImage = new();
    SceneRenderer? _renderer;
    D3D9.IDirect3D9Ex? _d3d9;
    D3D9.IDirect3DDevice9Ex? _device9;
    D3D9.IDirect3DTexture9? _sharedTexture;
    OrbitCamera _camera = OrbitCamera.Default();
    Vector3 _scale = Vector3.One;
    readonly List<SceneObject> _objects = [];
    int _nextId = 1;
    int _selectedId = -1;
    Vector3 _sceneMin = -Vector3.One;
    Vector3 _sceneMax = Vector3.One;
    Point _pressPoint;
    bool _leftPressed;
    bool _wireframe;
    Point _lastPoint;
    DragMode _drag;
    int _loadVersion;
    bool _resizePending = true;
    bool _dirty = true;
    bool _renderingHooked;
    string? _failure;
    BenchState? _bench;

    /// <summary>模型、错误信息变化时触发（不是每帧）。</summary>
    public event EventHandler? ViewChanged;


    /// <summary>选中对象变化；参数为新的对象 id，-1 表示取消选择。</summary>
    public event EventHandler<int>? SelectionChanged;

    /// <summary>对象增删（打开 / 追加 / 移除 / 清空）后触发，场景树据此重建。</summary>
    public event EventHandler? SceneChanged;

    // 状态栏：有选中时显示选中对象，否则显示整个场景（单个对象名或「N 个对象」+ 合计）。
    public string MeshName => TryGetObject(_selectedId, out var o) ? o.Name
        : _objects.Count == 1 ? _objects[0].Name
        : _objects.Count == 0 ? "" : $"{_objects.Count} objects";
    public int VertexCount => TryGetObject(_selectedId, out var o) ? o.VertexCount : SumVertices();
    public int TriangleCount => TryGetObject(_selectedId, out var o) ? o.TriangleCount : SumTriangles();
    public string MeshNotice => TryGetObject(_selectedId, out var o) ? o.Mesh.Notice
        : _objects.Count == 1 ? _objects[0].Mesh.Notice : "";
    public string? Failure => _failure;
    public bool HasMesh => _objects.Count > 0;

    /// <summary>当前场景所有对象 id（按加载顺序）。</summary>
    public IReadOnlyList<int> ObjectIds => _objects.ConvertAll(o => o.Id);

    /// <summary>当前选中对象 id，-1 表示无；赋值等同 Select。</summary>
    public int SelectedId
    {
        get => _selectedId;
        set => Select(value);
    }

    /// <summary>线框显示（快捷键 Shift+Z）。</summary>
    public bool Wireframe
    {
        get => _wireframe;
        set
        {
            _wireframe = value;
            if (_renderer != null)
            {
                _renderer.Wireframe = value;
            }

            Invalidate();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public bool Orthographic => _camera.Orthographic;

    /// <summary>上一次打开模型时，读取 + 解析文件的耗时（毫秒）。</summary>
    public double LastLoadMilliseconds { get; private set; }

    /// <summary>上一次打开模型时，创建 GPU 缓冲和贴图的耗时（毫秒）。</summary>
    public double LastUploadMilliseconds { get; private set; }

    public string AdapterName => _renderer?.AdapterName ?? "";

    public ModelViewport()
    {
        _image.Source = _d3dImage;
        Content = BuildOverlay(_image);
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
    }

    /// <summary>请求下一帧重画。画面没变化时不渲染，CPU / GPU 空闲。</summary>
    public void Invalidate() => _dirty = true;

    public void ShowSample()
    {
        ApplyMesh(MeshBuilder.CreateBar(), null, append: false);
    }

    public bool TryGetObjectInfo(int id, out SceneObjectInfo info)
    {
        if (TryGetObject(id, out var o))
        {
            info = new SceneObjectInfo(o.Id, o.Name, o.SourcePath, o.VertexCount, o.TriangleCount, o.Min, o.Max, o.Visible);
            return true;
        }

        info = default;
        return false;
    }

    /// <summary>选中指定对象（-1 或不存在的 id 视为取消选择），变化时触发 SelectionChanged。</summary>
    public void Select(int id)
    {
        CancelTransform();

        if (!TryGetObject(id, out _))
        {
            id = -1;
        }

        if (id == _selectedId)
        {
            return;
        }

        _selectedId = id;
        _selectedNode = -1;
        if (_renderer != null)
        {
            _renderer.SetPartHighlight(id, null);
        }

        if (_renderer != null)
        {
            _renderer.SelectedId = id;
        }

        Invalidate();
        SelectionChanged?.Invoke(this, id);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>视口坐标（DIP，相对本控件左上角）下的射线拾取，返回最近命中的对象 id，没打中返回 -1。</summary>
    public int PickAt(Point position)
    {
        if (_objects.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return -1;
        }

        float aspect = (float)(ActualWidth / ActualHeight);
        SceneRenderer.GetViewProjection(_camera, _scale, SceneRadius, aspect, out var view, out var projection);
        var ray = Picking.CreateRay((float)position.X, (float)position.Y, (float)ActualWidth, (float)ActualHeight, view, projection);
        return Picking.Pick(ray, _objects, Matrix4x4.CreateScale(_scale)).ObjectId;
    }

    /// <summary>对象属性（可见性 / 变换）变化，参数为对象 id。</summary>
    public event EventHandler<int>? ObjectChanged;

    /// <summary>显示 / 隐藏对象：只影响显示和拾取，不删除。</summary>
    public void SetObjectVisible(int id, bool visible)
    {
        if (!TryGetObject(id, out var o) || o.Visible == visible)
        {
            return;
        }

        o.Visible = visible;
        _renderer?.SetVisible(id, visible);
        Invalidate();
        ObjectChanged?.Invoke(this, id);
    }

    /// <summary>移除一个对象并释放其 GPU 资源。</summary>
    public void RemoveObject(int id)
    {
        int index = _objects.FindIndex(o => o.Id == id);
        if (index < 0)
        {
            return;
        }

        _objects.RemoveAt(index);
        _renderer?.RemoveMesh(id);
        if (_selectedId == id)
        {
            Select(-1);
        }

        UpdateSceneBounds();
        Invalidate();
        SceneChanged?.Invoke(this, EventArgs.Empty);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearScene()
    {
        _objects.Clear();
        _renderer?.ClearMeshes();
        Select(-1);
        UpdateSceneBounds();
        Invalidate();
        SceneChanged?.Invoke(this, EventArgs.Empty);
    }

    bool TryGetObject(int id, out SceneObject obj)
    {
        for (int i = 0; i < _objects.Count; i++)
        {
            if (_objects[i].Id == id)
            {
                obj = _objects[i];
                return true;
            }
        }

        obj = null!;
        return false;
    }

    int SumVertices()
    {
        int sum = 0;
        foreach (var o in _objects)
        {
            sum += o.VertexCount;
        }

        return sum;
    }

    int SumTriangles()
    {
        int sum = 0;
        foreach (var o in _objects)
        {
            sum += o.TriangleCount;
        }

        return sum;
    }

    float SceneRadius => MathF.Max((_sceneMax - _sceneMin).Length() * 0.5f, 1e-4f);

    void UpdateSceneBounds()
    {
        if (_objects.Count == 0)
        {
            _sceneMin = -Vector3.One;
            _sceneMax = Vector3.One;
        }
        else
        {
            _sceneMin = new Vector3(float.MaxValue);
            _sceneMax = new Vector3(float.MinValue);
            foreach (var o in _objects)
            {
                _sceneMin = Vector3.Min(_sceneMin, o.Min);
                _sceneMax = Vector3.Max(_sceneMax, o.Max);
            }
        }

        _renderer?.UpdateGrid(_sceneMin, _sceneMax);
    }

    /// <summary>替换场景：清空后只放这个文件。</summary>
    public void LoadPath(string path) => LoadPaths([path]);

    /// <summary>追加到场景（多文件打开），新对象获得新 id，不影响已有对象。</summary>
    public void AddPath(string path)
    {
        Task.Run(() => MeshImporter.Load(path)).ContinueWith(task =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                var result = task.Result;
                if (!result.Ok || result.Mesh == null)
                {
                    _failure = result.Error;
                    ViewChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }

                _failure = null;
                ApplyMesh(result.Mesh, path, append: true);
            });
        });
    }

    /// <summary>一次打开多个文件：后台依次导入，回到 UI 线程后第一个替换场景、其余追加。失败的跳过并记到 Failure。</summary>
    public void LoadPaths(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        var list = paths.ToArray();
        int version = ++_loadVersion;
        Task.Run(() =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var results = Array.ConvertAll(list, MeshImporter.Load);
            return (results, watch.Elapsed.TotalMilliseconds);
        }).ContinueWith(task =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (version != _loadVersion)
                {
                    return;
                }

                var (results, loadMs) = task.Result;
                bool append = false;
                string? failure = null;
                for (int i = 0; i < list.Length; i++)
                {
                    var result = results[i];
                    if (!result.Ok || result.Mesh == null)
                    {
                        failure = list.Length == 1 ? result.Error : $"{System.IO.Path.GetFileName(list[i])}: {result.Error}";
                        continue;
                    }

                    _failure = null;
                    LastLoadMilliseconds = loadMs;
                    ApplyMesh(result.Mesh, list[i], append);
                    append = true;
                }

                if (failure != null)
                {
                    _failure = failure;
                    ViewChanged?.Invoke(this, EventArgs.Empty);
                    if (!append)
                    {
                        _bench?.Fail(failure);
                    }
                }
            });
        });
    }

    public void ResetView()
    {
        // 复位视角：相机和缩放都回默认（透视 / 正交模式保留）。
        _scale = Vector3.One;
        bool ortho = _camera.Orthographic;
        _camera = OrbitCamera.Default();
        _camera.Frame(_sceneMin, _sceneMax);
        _camera.Orthographic = ortho;

        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Blender 风格快捷键。返回 true 表示已处理。</summary>
    public bool HandleKey(Key key, ModifierKeys modifiers)
    {
        if (TransformHandleKey(key, modifiers))
        {
            return true;
        }

        bool ctrl = (modifiers & ModifierKeys.Control) != 0;
        if (key == Key.A && modifiers == ModifierKeys.Shift)
        {
            ShowAddMenu();
            return true;
        }

        if (key == Key.Z && modifiers == ModifierKeys.Shift)
        {
            Wireframe = !Wireframe;
            return true;
        }

        switch (key)
        {
            case Key.Escape:
                Select(-1);
                return true;
            case Key.Delete:
                if (_selectedId < 0)
                {
                    return false;
                }

                RemoveObject(_selectedId);
                return true;
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
        if (_objects.Count == 0)
        {
            return;
        }

        float yaw = _camera.Yaw;
        float pitch = _camera.Pitch;
        _camera.Frame(_sceneMin, _sceneMax);
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

    void ApplyMesh(MeshData mesh, string? path, bool append)
    {
        if (!append)
        {
            _objects.Clear();
            _renderer?.ClearMeshes();
            Select(-1);
        }

        // 顶点平移到以包围盒中心为原点，Position = 原中心：渲染结果不变，旋转 / 缩放围绕对象中心。
        mesh = Recenter(mesh, out var center);
        var obj = new SceneObject
        {
            Id = _nextId++,
            Name = path == null ? mesh.Name : System.IO.Path.GetFileName(path),
            SourcePath = path,
            Mesh = mesh,
            ImportOffset = center,
        };
        obj.Transform.Position = center;
        _objects.Add(obj);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _renderer?.AddMesh(obj.Id, mesh);
        _renderer?.SetWorld(obj.Id, obj.World);
        LastUploadMilliseconds = _renderer == null ? 0 : watch.Elapsed.TotalMilliseconds;
        UpdateSceneBounds();
        bool ortho = _camera.Orthographic;
        _camera.Frame(_sceneMin, _sceneMax);
        _camera.Orthographic = ortho;
        _scale = Vector3.One;
        Invalidate();
        if (_bench != null && mesh.Name != "")
        {
            _bench.MeshReady(this);
        }

        SceneChanged?.Invoke(this, EventArgs.Empty);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _renderer = SceneRenderer.Create();
            CreateD3D9();
            _renderer.Wireframe = _wireframe;
            _renderer.SelectedId = _selectedId;
            if (_objects.Count == 0)
            {
                if (_bench == null)
                {
                    ShowSample();
                }
            }
            else
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                foreach (var o in _objects)
                {
                    _renderer.AddMesh(o.Id, o.Mesh);
                    _renderer.SetVisible(o.Id, o.Visible);
                    _renderer.SetWorld(o.Id, o.World);
                }

                LastUploadMilliseconds = watch.Elapsed.TotalMilliseconds;
                UpdateSceneBounds();
            }

            if (!_renderingHooked)
            {
                CompositionTarget.Rendering += OnRendering;
                _renderingHooked = true;
            }

            Invalidate();
        }
        catch (Exception ex)
        {
            App.LogException("OnLoaded", ex);
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
        try
        {
            RenderTick();
        }
        catch (Exception ex)
        {
            // Never let a render exception kill the app: log it and show it in the status bar.
            App.LogException("Render", ex);
            _failure = ex.Message;
            _dirty = false;
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    void RenderTick()
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
        RenderFrame();
        _d3dImage.Lock();
        _d3dImage.AddDirtyRect(new Int32Rect(0, 0, _d3dImage.PixelWidth, _d3dImage.PixelHeight));
        _d3dImage.Unlock();
        _bench?.Frame(this);
    }

    void RenderFrame()
    {
        UpdateGizmo();
        UpdateNavGizmo();
        UpdateLights();
        _renderer!.Render(_camera, _scale, SceneRadius);
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

    // ---- 鼠标：Blender 风格。中键环绕，Shift+中键平移，Ctrl+中键缩放；Alt+左键也能环绕（没有中键时用），普通左键留给选择。----

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (TransformMouseDown(e))
        {
            return;
        }

        Focus();
        if (e.ChangedButton == MouseButton.Middle)
        {
            var modifiers = Keyboard.Modifiers;
            var mode = (modifiers & ModifierKeys.Shift) != 0 ? DragMode.Pan
                : (modifiers & ModifierKeys.Control) != 0 ? DragMode.Zoom
                : DragMode.Orbit;
            BeginDrag(mode, MouseButton.Middle, e);
        }
        else if (e.ChangedButton == MouseButton.Left && _drag == DragMode.None && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            // 没有中键时的备用：Alt+左键环绕，Alt+Shift+左键平移；普通左键留给选择 / 变换手柄
            BeginDrag((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? DragMode.Pan : DragMode.Orbit, MouseButton.Left, e);
        }
        else if (e.ChangedButton == MouseButton.Left && _drag == DragMode.None)
        {
            // 普通左键：记下按下位置，松开时没怎么移动就当作单击拾取。
            _leftPressed = true;
            _pressPoint = e.GetPosition(this);
            e.Handled = true;
        }
        else
        {
            base.OnMouseDown(e);
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (TransformMouseUp(e))
        {
            return;
        }

        if (_drag != DragMode.None && e.ChangedButton == _dragButton)
        {
            _drag = DragMode.None;
            ReleaseMouseCapture();
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left && _leftPressed)
        {
            _leftPressed = false;
            var point = e.GetPosition(this);
            if ((point - _pressPoint).Length < 4)
            {
                // 打中最近的对象就选中它；点空白处取消选择。
                if (TryPickLight(point, out int lightObject, out int lightNode))
                {
                    SelectNode(lightObject, lightNode);
                }
                else
                {
                    Select(PickAt(point));
                }
            }

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
        if (TransformMouseMove(e))
        {
            return;
        }

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
