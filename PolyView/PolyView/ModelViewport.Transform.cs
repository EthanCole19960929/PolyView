using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PolyView;

/// <summary>
/// 视口的对象变换部分：工具栏（选择 / 移动 / 旋转 / 缩放）、变换手柄、G / R / S 模态变换、撤销 / 重做、
/// 节点选择 / 节点可见性。规格见 docs/05_变换操纵轴规格.md。
/// </summary>
public sealed partial class ModelViewport
{
    readonly ToggleButton[] _toolButtons = new ToggleButton[4];
    readonly TextBlock _modalText = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x4A)),
        Margin = new Thickness(56, 0, 0, 8),
        VerticalAlignment = VerticalAlignment.Bottom,
        HorizontalAlignment = HorizontalAlignment.Left,
        IsHitTestVisible = false,
    };

    readonly List<(int Id, ObjectTransform Old, ObjectTransform New)> _undo = [];
    readonly List<(int Id, ObjectTransform Old, ObjectTransform New)> _redo = [];
    TransformTool _tool = TransformTool.Select;
    int _hoverPart = -1;
    float _gizmoSize = 1f;

    // 进行中的变换
    bool _xformActive;
    bool _xformModal;
    TransformTool _xformTool;
    int _xformPart;          // 0..2 轴，3 = 自由 / 等比
    int _xformHandle = -1;   // 正在拖的手柄部件（高亮用），模态时 -1
    bool _xformPlane;        // true：排除 _xformPart 轴（Shift+轴）
    ObjectTransform _xformStart;
    Point _xformMouse0;
    Vector3 _xformHit0;
    float _xformAngle0;

    int _selectedNode = -1;

    public const int UndoLimit = 100;

    /// <summary>当前工具（工具栏 / W 键切换）。</summary>
    public TransformTool ActiveTool
    {
        get => _tool;
        set
        {
            _tool = value;
            for (int i = 0; i < _toolButtons.Length; i++)
            {
                _toolButtons[i].IsChecked = i == (int)value;
            }

            _hoverPart = -1;
            Invalidate();
        }
    }

    /// <summary>选中的节点下标（MeshData.Nodes），-1 = 整个对象。</summary>
    public int SelectedNode => _selectedNode;

    Grid BuildOverlay(UIElement image)
    {
        var grid = new Grid();
        grid.Children.Add(image);
        var bar = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6),
        };
        // Segoe MDL2：选择、移动、旋转、缩放
        string[] glyphs = ["\uE8B0", "\uE7C2", "\uE7AD", "\uE740"];
        for (int i = 0; i < 4; i++)
        {
            var tool = (TransformTool)i;
            var button = new ToggleButton
            {
                Content = glyphs[i],
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                Width = 34,
                Height = 34,
                Margin = new Thickness(0, 0, 0, 4),
                Focusable = false,
                IsChecked = i == 0,
            };
            button.Click += (_, _) => ActiveTool = tool;
            _toolButtons[i] = button;
            bar.Children.Add(button);
        }

        grid.Children.Add(bar);
        grid.Children.Add(_modalText);
        ApplyToolbarLanguage();
        return grid;
    }

    /// <summary>切换语言时刷新工具栏提示。</summary>
    public void ApplyToolbarLanguage()
    {
        string[] names = [UiText.ToolSelect, UiText.ToolMove, UiText.ToolRotate, UiText.ToolScale];
        string[] hints = ["W", UiText.HintMove, UiText.HintRotate, UiText.HintScale];
        for (int i = 0; i < _toolButtons.Length; i++)
        {
            if (_toolButtons[i] != null)
            {
                _toolButtons[i].ToolTip = names[i] + "\n" + hints[i];
            }
        }
    }

    // ---------------- 变换数据 ----------------

    public bool TryGetTransform(int id, out ObjectTransform transform)
    {
        if (TryGetObject(id, out var o))
        {
            transform = o.Transform;
            return true;
        }

        transform = ObjectTransform.Identity;
        return false;
    }

    /// <summary>设置对象变换并记一条撤销（面板输入用）。</summary>
    public void SetTransform(int id, ObjectTransform transform)
    {
        if (!TryGetObject(id, out var o) || o.Transform.Equals(transform))
        {
            return;
        }

        PushUndo(id, o.Transform, transform);
        ApplyTransform(o, transform, commit: true);
    }

    /// <summary>属性栏用的变换文本。</summary>
    public string GetObjectTransformText(int id)
    {
        if (!TryGetObject(id, out var o))
        {
            return "";
        }

        var t = o.Transform;
        var e = t.EulerDegrees;
        bool zh = UiText.Chinese;
        return $"{(zh ? "位置" : "Loc ")}  {t.Position.X:0.###}, {t.Position.Y:0.###}, {t.Position.Z:0.###}\n"
            + $"{(zh ? "旋转" : "Rot ")}  {e.X:0.#}°, {e.Y:0.#}°, {e.Z:0.#}°\n"
            + $"{(zh ? "缩放" : "Scl ")}  {t.Scale.X:0.###}, {t.Scale.Y:0.###}, {t.Scale.Z:0.###}";
    }

    void ApplyTransform(SceneObject o, ObjectTransform transform, bool commit)
    {
        o.Transform = transform;
        _renderer?.SetWorld(o.Id, o.World);
        if (commit)
        {
            UpdateSceneBounds();
        }

        Invalidate();
        ObjectChanged?.Invoke(this, o.Id);
    }

    void PushUndo(int id, ObjectTransform old, ObjectTransform now)
    {
        _undo.Add((id, old, now));
        if (_undo.Count > UndoLimit)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
    }

    public void Undo() => UndoRedo(_undo, _redo, useOld: true);

    public void Redo() => UndoRedo(_redo, _undo, useOld: false);

    void UndoRedo(List<(int Id, ObjectTransform Old, ObjectTransform New)> from, List<(int Id, ObjectTransform Old, ObjectTransform New)> to, bool useOld)
    {
        while (from.Count > 0)
        {
            var entry = from[^1];
            from.RemoveAt(from.Count - 1);
            if (TryGetObject(entry.Id, out var o))
            {
                to.Add(entry);
                ApplyTransform(o, useOld ? entry.Old : entry.New, commit: true);
                return;
            }
        }
    }

    static MeshData Recenter(MeshData mesh, out Vector3 center)
    {
        center = (mesh.Min + mesh.Max) * 0.5f;
        if (center == Vector3.Zero || !float.IsFinite(center.X + center.Y + center.Z))
        {
            center = Vector3.Zero;
            return mesh;
        }

        var vertices = (Vertex[])mesh.Vertices.Clone();
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i].Position -= center;
        }

        return new MeshData
        {
            Name = mesh.Name,
            Vertices = vertices,
            Indices = mesh.Indices,
            Min = mesh.Min - center,
            Max = mesh.Max - center,
            Parts = mesh.Parts,
            Textures = mesh.Textures,
            Notice = mesh.Notice,
            Nodes = mesh.Nodes,
        };
    }

    // ---------------- 节点 ----------------

    /// <summary>对象的节点树（深度优先，父在子前）；没有对象返回空数组。</summary>
    public IReadOnlyList<SceneNode> GetNodes(int id) => TryGetObject(id, out var o) ? o.Mesh.Nodes : [];

    /// <summary>选中对象里的某个节点：对象被选中，只高亮该节点及其子节点的部件。nodeIndex = -1 选整个对象。</summary>
    public void SelectNode(int id, int nodeIndex)
    {
        Select(id);
        if (!TryGetObject(id, out var o))
        {
            _selectedNode = -1;
            return;
        }

        _selectedNode = nodeIndex;
        _renderer?.SetPartHighlight(id, nodeIndex < 0 ? null : PartMask(o, nodeIndex));
        Invalidate();
    }

    public bool IsNodeVisible(int id, int nodeIndex) =>
        TryGetObject(id, out var o) && !o.HiddenNodes.Contains(nodeIndex);

    /// <summary>节点可见性：隐藏该节点（及子节点）的部件，只影响显示。</summary>
    public void SetNodeVisible(int id, int nodeIndex, bool visible)
    {
        if (!TryGetObject(id, out var o))
        {
            return;
        }

        if (visible ? !o.HiddenNodes.Remove(nodeIndex) : !o.HiddenNodes.Add(nodeIndex))
        {
            return;
        }

        bool[]? hidden = null;
        foreach (int node in o.HiddenNodes)
        {
            var mask = PartMask(o, node);
            hidden ??= new bool[mask.Length];
            for (int i = 0; i < mask.Length; i++)
            {
                hidden[i] |= mask[i];
            }
        }

        _renderer?.SetPartHidden(id, hidden);
        Invalidate();
        ObjectChanged?.Invoke(this, id);
    }

    /// <summary>节点（含子节点）拥有的部件掩码。Mesh 节点 PartIndices 为空且文件没有分段 = 整份索引（部件 0）。</summary>
    static bool[] PartMask(SceneObject o, int nodeIndex)
    {
        var nodes = o.Mesh.Nodes;
        int partCount = Math.Max(1, o.Mesh.Parts.Length);
        var mask = new bool[partCount];
        if (nodeIndex < 0 || nodeIndex >= nodes.Length)
        {
            return mask;
        }

        var stack = new Stack<int>();
        stack.Push(nodeIndex);
        while (stack.Count > 0)
        {
            var node = nodes[stack.Pop()];
            if (node.Kind == SceneNodeKind.Mesh && node.PartIndices.Length == 0 && o.Mesh.Parts.Length == 0)
            {
                Array.Fill(mask, true);
            }

            foreach (int p in node.PartIndices)
            {
                if (p >= 0 && p < partCount)
                {
                    mask[p] = true;
                }
            }

            foreach (int child in node.Children)
            {
                stack.Push(child);
            }
        }

        return mask;
    }

    // ---------------- 手柄 ----------------

    bool GizmoShown => _tool != TransformTool.Select && TryGetObject(_selectedId, out var o) && o.Visible;

    /// <summary>每帧（渲染前）按当前相机重算手柄的屏幕恒定尺寸并交给渲染器。不分配内存。</summary>
    void UpdateGizmo()
    {
        if (_renderer == null)
        {
            return;
        }

        var tool = _xformActive ? _xformTool : _tool;
        if (!(_xformActive || GizmoShown) || !TryGetObject(_selectedId, out var o))
        {
            _renderer.SetGizmo(TransformTool.Select, Matrix4x4.Identity, -1, -1);
            return;
        }

        var center = o.Transform.Position;
        _gizmoSize = GizmoWorldSize(center);
        _renderer.SetGizmo(
            tool,
            Matrix4x4.CreateScale(_gizmoSize) * Matrix4x4.CreateTranslation(center),
            _hoverPart,
            _xformActive ? _xformHandle : -1);
    }

    /// <summary>规格 4：P / H × 2 × d × tan(fov/2)（透视，d 为沿视线深度）或 P / H × 2 × OrthoHalfHeight（正交）。</summary>
    float GizmoWorldSize(Vector3 center)
    {
        float height = (float)Math.Max(1.0, ActualHeight);
        float ratio = Gizmo.ScreenSize / height;
        if (_camera.Orthographic)
        {
            return ratio * 2f * _camera.OrthoHalfHeight;
        }

        var eye = _camera.Eye;
        var forward = Vector3.Normalize(_camera.Target - eye);
        float depth = MathF.Max(1e-4f, Vector3.Dot(center - eye, forward));
        return ratio * 2f * depth * MathF.Tan(_camera.FieldOfView * 0.5f);
    }

    void ViewProjection(out Matrix4x4 view, out Matrix4x4 projection)
    {
        float aspect = (float)(Math.Max(1.0, ActualWidth) / Math.Max(1.0, ActualHeight));
        SceneRenderer.GetViewProjection(_camera, _scale, SceneRadius, aspect, out view, out projection);
    }

    Vector2? Project(Vector3 world)
    {
        ViewProjection(out var view, out var projection);
        var clip = Vector4.Transform(new Vector4(world, 1f), view * projection);
        if (clip.W <= 1e-6f)
        {
            return null;
        }

        float x = clip.X / clip.W;
        float y = clip.Y / clip.W;
        return new Vector2((x + 1f) * 0.5f * (float)ActualWidth, (1f - y) * 0.5f * (float)ActualHeight);
    }

    PickRay RayAt(Point p)
    {
        ViewProjection(out var view, out var projection);
        return Picking.CreateRay((float)p.X, (float)p.Y, (float)ActualWidth, (float)ActualHeight, view, projection);
    }

    int GizmoHit(Point p)
    {
        if (!GizmoShown || !TryGetObject(_selectedId, out var o))
        {
            return -1;
        }

        var center = o.Transform.Position;
        var forward = ViewDirection;
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        return Gizmo.HitTest(_tool, center, GizmoWorldSize(center), right, new Vector2((float)p.X, (float)p.Y), Project);
    }

    // ---------------- 交互 ----------------

    bool TransformMouseDown(MouseButtonEventArgs e)
    {
        if (_xformActive)
        {
            if (e.ChangedButton == MouseButton.Right)
            {
                CancelTransform();
            }
            else if (e.ChangedButton == MouseButton.Left && _xformModal)
            {
                CommitTransform();
            }
            else
            {
                return false;
            }

            e.Handled = true;
            return true;
        }

        if (e.ChangedButton != MouseButton.Left || (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            return false;
        }

        // 手柄优先于对象拾取：命中手柄就开始拖动，不改变选择。
        int part = GizmoHit(e.GetPosition(this));
        if (part < 0)
        {
            return false;
        }

        Focus();
        BeginTransform(_tool, part, plane: false, modal: false, e.GetPosition(this));
        e.Handled = true;
        return true;
    }

    bool TransformMouseUp(MouseButtonEventArgs e)
    {
        if (!_xformActive || _xformModal || e.ChangedButton != MouseButton.Left)
        {
            return false;
        }

        CommitTransform();
        e.Handled = true;
        return true;
    }

    bool TransformMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_xformActive)
        {
            UpdateTransform(p);
            return true;
        }

        if (_drag == DragMode.None && _tool != TransformTool.Select)
        {
            int hover = GizmoHit(p);
            if (hover != _hoverPart)
            {
                _hoverPart = hover;
                Invalidate();
            }
        }

        return false;
    }

    bool TransformHandleKey(Key key, ModifierKeys modifiers)
    {
        bool ctrl = (modifiers & ModifierKeys.Control) != 0;
        bool shift = (modifiers & ModifierKeys.Shift) != 0;
        bool alt = (modifiers & ModifierKeys.Alt) != 0;
        if (_xformActive)
        {
            switch (key)
            {
                case Key.X:
                case Key.Y:
                case Key.Z:
                    int axis = key - Key.X;
                    _xformPart = axis;
                    _xformPlane = shift;
                    _xformHandle = -1;
                    RestartTransformMath();
                    return true;
                case Key.Enter:
                    CommitTransform();
                    return true;
                case Key.Escape:
                    CancelTransform();
                    return true;
                default:
                    return key is Key.G or Key.R or Key.S;
            }
        }

        if (ctrl && key == Key.Z)
        {
            if (shift)
            {
                Redo();
            }
            else
            {
                Undo();
            }

            return true;
        }

        if (ctrl && key == Key.Y)
        {
            Redo();
            return true;
        }

        if (alt && key is Key.G or Key.R or Key.S)
        {
            if (!TryGetObject(_selectedId, out var o))
            {
                return false;
            }

            var t = o.Transform;
            if (key == Key.G)
            {
                t.Position = Vector3.Zero;
            }
            else if (key == Key.R)
            {
                t.Rotation = Quaternion.Identity;
            }
            else
            {
                t.Scale = Vector3.One;
            }

            SetTransform(o.Id, t);
            return true;
        }

        if (modifiers != ModifierKeys.None)
        {
            return false;
        }

        switch (key)
        {
            case Key.W:
                ActiveTool = TransformTool.Select;
                return true;
            case Key.G:
            case Key.R:
            case Key.S:
                if (_selectedId < 0)
                {
                    return false;
                }

                var tool = key == Key.G ? TransformTool.Move : key == Key.R ? TransformTool.Rotate : TransformTool.Scale;
                var mouse = Mouse.GetPosition(this);
                BeginTransform(tool, Gizmo.Center, plane: false, modal: true, mouse);
                CaptureMouse();
                return true;
            default:
                return false;
        }
    }

    void BeginTransform(TransformTool tool, int part, bool plane, bool modal, Point mouse)
    {
        if (!TryGetObject(_selectedId, out var o))
        {
            return;
        }

        _xformActive = true;
        _xformModal = modal;
        _xformTool = tool;
        _xformHandle = modal ? -1 : part;
        // 平面方块 = 锁平面（排除对应的轴）
        if (part > Gizmo.Center)
        {
            plane = true;
            part = Gizmo.PlaneExcludedAxis[part - 4];
        }

        _xformPart = part;
        _xformPlane = plane;
        _xformStart = o.Transform;
        _xformMouse0 = mouse;
        if (!modal)
        {
            CaptureMouse();
        }

        RestartTransformMath();
        Invalidate();
    }

    /// <summary>换轴时以起始状态和起始鼠标位置重新计算基准。</summary>
    void RestartTransformMath()
    {
        if (!TryGetObject(_selectedId, out var o))
        {
            return;
        }

        ApplyTransform(o, _xformStart, commit: false);
        var center = _xformStart.Position;
        TryMoveHit(_xformMouse0, center, out _xformHit0);
        _xformAngle0 = ScreenAngle(_xformMouse0, center);
        UpdateTransform(Mouse.GetPosition(this));
    }

    Vector3 ViewDirection => Vector3.Normalize(_camera.Target - _camera.Eye);

    /// <summary>移动：按锁定方式选平面，返回鼠标射线与平面的交点。</summary>
    bool TryMoveHit(Point mouse, Vector3 origin, out Vector3 hit)
    {
        var ray = RayAt(mouse);
        var view = ViewDirection;
        Vector3 normal;
        if (_xformPart == Gizmo.Center)
        {
            normal = view;
        }
        else if (_xformPlane)
        {
            normal = Gizmo.Axes[_xformPart];
        }
        else
        {
            // 过轴且最面向相机的平面
            var a = Gizmo.Axes[_xformPart];
            normal = Vector3.Cross(a, Vector3.Cross(view, a));
            normal = normal.LengthSquared() < 1e-8f ? view : Vector3.Normalize(normal);
        }

        return Gizmo.RayPlane(ray, origin, normal, out hit);
    }

    float ScreenAngle(Point mouse, Vector3 center)
    {
        var c = Project(center) ?? Vector2.Zero;
        return MathF.Atan2(-(float)(mouse.Y - c.Y), (float)(mouse.X - c.X));
    }

    void UpdateTransform(Point mouse)
    {
        if (!_xformActive || !TryGetObject(_selectedId, out var o))
        {
            return;
        }

        bool snap = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var t = _xformStart;
        var center = _xformStart.Position;
        string status;
        string axisName = _xformPart == Gizmo.Center ? "" : (_xformPlane ? " (Shift+" : " (") + (char)('X' + _xformPart) + ")";
        switch (_xformTool)
        {
            case TransformTool.Move:
            {
                var delta = Vector3.Zero;
                if (TryMoveHit(mouse, center, out var hit))
                {
                    delta = hit - _xformHit0;
                    if (_xformPart != Gizmo.Center && !_xformPlane)
                    {
                        var a = Gizmo.Axes[_xformPart];
                        delta = a * Vector3.Dot(delta, a);
                    }
                    else if (_xformPlane)
                    {
                        var a = Gizmo.Axes[_xformPart];
                        delta -= a * Vector3.Dot(delta, a);
                    }
                }

                if (snap)
                {
                    delta = new Vector3(MathF.Round(delta.X), MathF.Round(delta.Y), MathF.Round(delta.Z));
                }

                t.Position = center + delta;
                status = $"D: {delta.X:0.###}, {delta.Y:0.###}, {delta.Z:0.###}{axisName}";
                break;
            }

            case TransformTool.Rotate:
            {
                float angle = ScreenAngle(mouse, center) - _xformAngle0;
                angle = MathF.IEEERemainder(angle, MathF.Tau);
                var toCamera = -ViewDirection;
                Vector3 axis;
                if (_xformPart == Gizmo.Center || _xformPlane)
                {
                    axis = toCamera;
                }
                else
                {
                    axis = Gizmo.Axes[_xformPart];
                    if (Vector3.Dot(axis, toCamera) < 0)
                    {
                        angle = -angle;
                    }
                }

                float degrees = angle * 180f / MathF.PI;
                if (snap)
                {
                    degrees = MathF.Round(degrees / 5f) * 5f;
                }

                t.Rotation = Quaternion.Normalize(Quaternion.Concatenate(
                    _xformStart.Rotation,
                    Quaternion.CreateFromAxisAngle(axis, degrees * MathF.PI / 180f)));
                status = (UiText.Chinese ? "角度 " : "Angle ") + $"{degrees:0.#}°{axisName}";
                break;
            }

            default:
            {
                var c = Project(center) ?? Vector2.Zero;
                float d0 = Vector2.Distance(new Vector2((float)_xformMouse0.X, (float)_xformMouse0.Y), c);
                float d1 = Vector2.Distance(new Vector2((float)mouse.X, (float)mouse.Y), c);
                // 起点离中心太近时比例不稳定，忽略
                float s = d0 < 5f ? 1f : d1 / d0;
                if (snap)
                {
                    s = MathF.Max(0.1f, MathF.Round(s * 10f) / 10f);
                }

                var factor = Vector3.One;
                if (_xformPart == Gizmo.Center)
                {
                    factor = new Vector3(s);
                }
                else if (_xformPlane)
                {
                    factor = new Vector3(s) - Gizmo.Axes[_xformPart] * (s - 1f);
                }
                else
                {
                    factor += Gizmo.Axes[_xformPart] * (s - 1f);
                }

                t.Scale = _xformStart.Scale * factor;
                status = (UiText.Chinese ? "缩放 " : "Scale ") + $"{s:0.00}{axisName}";
                break;
            }
        }

        _modalText.Text = status;
        ApplyTransform(o, t, commit: false);
    }

    void CommitTransform()
    {
        if (!_xformActive)
        {
            return;
        }

        _xformActive = false;
        _modalText.Text = "";
        ReleaseMouseCapture();
        if (TryGetObject(_selectedId, out var o))
        {
            if (!o.Transform.Equals(_xformStart))
            {
                PushUndo(o.Id, _xformStart, o.Transform);
            }

            ApplyTransform(o, o.Transform, commit: true);
        }

        Invalidate();
    }

    /// <summary>取消进行中的变换并还原（右键 / Esc，或选择改变时）。</summary>
    void CancelTransform()
    {
        if (!_xformActive)
        {
            return;
        }

        _xformActive = false;
        _modalText.Text = "";
        ReleaseMouseCapture();
        if (TryGetObject(_selectedId, out var o))
        {
            ApplyTransform(o, _xformStart, commit: true);
        }

        Invalidate();
    }
}
