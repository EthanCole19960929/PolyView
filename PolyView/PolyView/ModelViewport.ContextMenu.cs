using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PolyView;

/// <summary>
/// Blender 4.2 viewport right-click Object Context Menu (object mode) and its shortcuts:
/// shading (smooth / auto smooth / flat), set origin, copy / paste, duplicate (Shift D) / linked duplicate (Alt D),
/// rename (F2), mirror, snap, move to collection (M), delete (X). Parent / Convert / keyframes are shown disabled (deferred).
/// Every action is also a [PvCommand] (Core/Commands/ObjectMenuCommands.cs).
/// </summary>
public sealed partial class ModelViewport
{
    static string Z(string zh, string en) => UiText.Chinese ? zh : en;

    /// <summary>Blender 3D cursor (world space). Not drawn yet; used by Set Origin / Snap.</summary>
    public Vector3 Cursor3D { get; set; }

    bool _ctxArmed;
    Point _ctxPress;
    readonly List<(MeshData Mesh, ObjectTransform Transform, string Name, string? Source, PrimitiveSpec? Spec)> _objectClipboard = [];

    // ---------------- right click ----------------

    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    {
        _ctxArmed = !e.Handled && !_xformActive;
        _ctxPress = e.GetPosition(this);
        base.OnPreviewMouseRightButtonDown(e);
    }

    protected override void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonUp(e);
        if (!_ctxArmed || _xformActive || (e.GetPosition(this) - _ctxPress).Length > 4)
        {
            _ctxArmed = false;
            return;
        }

        _ctxArmed = false;
        if (e.GetPosition(this).Y < HeaderHeight)
        {
            return;
        }

        e.Handled = true;
        ShowObjectContextMenu();
    }

    public void ShowObjectContextMenu()
    {
        var menu = new ContextMenu
        {
            PlacementTarget = this,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
        };
        if (Window.GetWindow(this) is { } window)
        {
            menu.Resources.MergedDictionaries.Add(window.Resources);
        }

        FillObjectContextMenu(menu.Items);
        menu.IsOpen = true;
    }

    static MenuItem Deferred(string header, string? gesture = null)
    {
        var item = Item(header, null, gesture);
        item.ToolTip = Z("暂未实现（后续开发）", "Not implemented yet");
        ToolTipService.SetShowOnDisabled(item, true);
        return item;
    }

    void FillObjectContextMenu(ItemCollection items)
    {
        bool sel = _selectedId >= 0;
        bool mesh = SelectedObjects().Any(o => o.Mesh.Indices.Length > 0);
        items.Add(new MenuItem { Header = Z("物体", "Object"), IsEnabled = false, Tag = "header" });
        items.Add(new Separator());
        items.Add(Item(Z("平滑着色", "Shade Smooth"), mesh ? () => ShadeSelected(ShadeMode.Smooth) : null));
        items.Add(Item(Z("自动平滑着色", "Shade Auto Smooth"), mesh ? () => ShadeSelected(ShadeMode.Auto) : null));
        items.Add(Item(Z("平直着色", "Shade Flat"), mesh ? () => ShadeSelected(ShadeMode.Flat) : null));
        items.Add(new Separator());

        var convert = new MenuItem { Header = Z("转换为", "Convert") };
        convert.Items.Add(Deferred(Z("网格", "Mesh")));
        convert.Items.Add(Deferred(Z("曲线", "Curve")));
        convert.Items.Add(Deferred(Z("蜡笔", "Grease Pencil")));
        items.Add(convert);

        var origin = new MenuItem { Header = Z("设置原点", "Set Origin") };
        origin.Items.Add(Item(Z("原点 -> 几何中心", "Origin to Geometry"), mesh ? () => SetOriginSelected(OriginMode.OriginToGeometry) : null));
        origin.Items.Add(Item(Z("几何中心 -> 原点", "Geometry to Origin"), mesh ? () => SetOriginSelected(OriginMode.GeometryToOrigin) : null));
        origin.Items.Add(Item(Z("原点 -> 3D 游标", "Origin to 3D Cursor"), mesh ? () => SetOriginSelected(OriginMode.OriginToCursor) : null));
        origin.Items.Add(Item(Z("原点 -> 质心（表面）", "Origin to Center of Mass (Surface)"), mesh ? () => SetOriginSelected(OriginMode.OriginToMass) : null));
        items.Add(origin);
        items.Add(new Separator());

        items.Add(Item(Z("复制物体", "Copy Objects"), sel ? CopySelected : null, "Ctrl C"));
        items.Add(Item(Z("粘贴物体", "Paste Objects"), _objectClipboard.Count > 0 ? () => PasteObjects() : null, "Ctrl V"));
        items.Add(new Separator());
        items.Add(Item(Z("复制物体", "Duplicate Objects"), sel ? () => DuplicateAndMove(linked: false) : null, "Shift D"));
        items.Add(Item(Z("关联复制", "Duplicate Linked"), sel ? () => DuplicateAndMove(linked: true) : null, "Alt D"));
        items.Add(new Separator());
        items.Add(Item(Z("重命名活动物体...", "Rename Active Object..."), sel ? RenameActiveDialog : null, "F2"));
        items.Add(new Separator());

        var mirror = new MenuItem { Header = Z("镜像", "Mirror") };
        mirror.Items.Add(Item(Z("X 全局", "X Global"), sel ? () => MirrorSelected(0) : null));
        mirror.Items.Add(Item(Z("Y 全局", "Y Global"), sel ? () => MirrorSelected(1) : null));
        mirror.Items.Add(Item(Z("Z 全局", "Z Global"), sel ? () => MirrorSelected(2) : null));
        items.Add(mirror);

        var snap = new MenuItem { Header = Z("吸附", "Snap") };
        snap.Items.Add(Item(Z("选中项 -> 游标", "Selection to Cursor"), sel ? () => Snap(SnapMode.SelectionToCursor) : null));
        snap.Items.Add(Item(Z("游标 -> 选中项", "Cursor to Selected"), sel ? () => Snap(SnapMode.CursorToSelected) : null));
        snap.Items.Add(Item(Z("游标 -> 世界原点", "Cursor to World Origin"), () => Snap(SnapMode.CursorToWorld)));
        items.Add(snap);

        var parent = new MenuItem { Header = Z("父级", "Parent") };
        parent.Items.Add(Deferred(Z("设置父级目标", "Object"), "Ctrl P"));
        parent.Items.Add(Deferred(Z("清除父级", "Clear Parent"), "Alt P"));
        items.Add(parent);

        var move = Item(Z("移动至集合", "Move to Collection"),
            sel && MoveToCollectionRequested != null ? () => MoveToCollectionRequested?.Invoke(this, false) : null, "M");
        if (!move.IsEnabled)
        {
            move.ToolTip = Z("先选中物体（集合功能由大纲提供）", "Select an object first (collections live in the outliner)");
            ToolTipService.SetShowOnDisabled(move, true);
        }

        items.Add(move);
        items.Add(new Separator());
        items.Add(Deferred(Z("插入关键帧", "Insert Keyframe"), "I"));
        items.Add(Deferred(Z("使用插帧集插入关键帧...", "Insert Keyframe with Keying Set..."), "K"));
        items.Add(new Separator());
        items.Add(Item(Z("删除", "Delete"), sel ? () => DeleteSelectedConfirm() : null, "X"));
    }

    /// <summary>Context-menu shortcuts. Called from HandleKey (after modal transform keys).</summary>
    bool HandleContextMenuKey(Key key, ModifierKeys m)
    {
        bool sel = _selectedId >= 0;
        switch (key)
        {
            case Key.C when m == ModifierKeys.Control && sel:
                CopySelected();
                return true;
            case Key.V when m == ModifierKeys.Control && _objectClipboard.Count > 0:
                PasteObjects();
                return true;
            case Key.D when m == ModifierKeys.Shift && sel:
                DuplicateAndMove(linked: false);
                return true;
            case Key.D when m == ModifierKeys.Alt && sel:
                DuplicateAndMove(linked: true);
                return true;
            case Key.F2 when m == ModifierKeys.None && sel:
                RenameActiveDialog();
                return true;
            case Key.X when m == ModifierKeys.None && sel:
                DeleteSelectedConfirm();
                return true;
            default:
                return false;
        }
    }

    List<SceneObject> SelectedObjects()
    {
        var ids = SelectedIds;
        return _objects.Where(o => o.Id == _selectedId || ids.Contains(o.Id)).ToList();
    }

    // ---------------- shading ----------------

    public enum ShadeMode { Smooth, Auto, Flat }

    /// <summary>Recompute normals of the selected meshes. Auto = smooth below 30°. Returns the number of objects changed.</summary>
    public int ShadeSelected(ShadeMode mode, float autoAngleDeg = 30f)
    {
        int n = 0;
        var done = new HashSet<MeshData>();
        foreach (var o in SelectedObjects())
        {
            if (o.Mesh.Indices.Length == 0 || !done.Add(o.Mesh))
            {
                continue;
            }

            float angle = mode switch { ShadeMode.Smooth => 180f, ShadeMode.Flat => 0f, _ => autoAngleDeg };
            ReplaceMesh(o.Mesh, MeshOps.Shade(o.Mesh, angle));
            n++;
        }

        return n;
    }

    /// <summary>Swap a mesh data-block for every object using it (linked duplicates) and re-upload to the GPU.</summary>
    void ReplaceMesh(MeshData old, MeshData now)
    {
        foreach (var o in _objects.Where(x => ReferenceEquals(x.Mesh, old)))
        {
            o.Mesh = now;
            _renderer?.AddMesh(o.Id, now);
            _renderer?.SetWorld(o.Id, o.World);
            if (!o.Visible)
            {
                _renderer?.SetVisible(o.Id, false);
            }
        }

        UpdateSceneBounds();
        Invalidate();
        SceneChanged?.Invoke(this, EventArgs.Empty);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------------- origin ----------------

    public enum OriginMode { OriginToGeometry, GeometryToOrigin, OriginToCursor, OriginToMass }

    public int SetOriginSelected(OriginMode mode)
    {
        int n = 0;
        foreach (var o in SelectedObjects())
        {
            if (o.Mesh.Indices.Length == 0)
            {
                continue;
            }

            // Offset in local space by which the vertices move (vertex' = vertex - c).
            Vector3 c = mode switch
            {
                OriginMode.OriginToMass => MeshOps.SurfaceCentroid(o.Mesh),
                OriginMode.OriginToCursor => Matrix4x4.Invert(o.World, out var inv) ? Vector3.Transform(Cursor3D, inv) : Vector3.Zero,
                _ => (o.Mesh.Min + o.Mesh.Max) * 0.5f,
            };
            if (c.LengthSquared() < 1e-12f)
            {
                continue;
            }

            var shifted = MeshOps.Translate(o.Mesh, -c);
            var old = o.Transform;
            ReplaceMesh(o.Mesh, shifted);
            if (mode != OriginMode.GeometryToOrigin)
            {
                var t = o.Transform;
                t.Position = Vector3.Transform(c, o.World);
                ApplyTransform(o, t, commit: true);
                PushUndo(o.Id, old, t);
            }

            n++;
        }

        return n;
    }

    // ---------------- copy / paste / duplicate ----------------

    public void CopySelected()
    {
        _objectClipboard.Clear();
        foreach (var o in SelectedObjects())
        {
            _objectClipboard.Add((o.Mesh, o.Transform, o.Name, o.SourcePath, GetPrimitiveSpec(o.Id)));
        }
    }

    public List<int> PasteObjects()
    {
        var ids = new List<int>();
        foreach (var c in _objectClipboard)
        {
            ids.Add(AddObjectCopy(MeshOps.Clone(c.Mesh), c.Transform, c.Name, c.Source, c.Spec));
        }

        if (ids.Count > 0)
        {
            Select(ids[^1]);
        }

        return ids;
    }

    /// <summary>Duplicate the selection (linked = share the mesh data-block, Blender Alt D). Returns the new ids.</summary>
    public List<int> DuplicateObjects(bool linked)
    {
        var ids = new List<int>();
        foreach (var o in SelectedObjects())
        {
            var mesh = linked ? o.Mesh : MeshOps.Clone(o.Mesh);
            ids.Add(AddObjectCopy(mesh, o.Transform, o.Name, o.SourcePath, GetPrimitiveSpec(o.Id)));
        }

        if (ids.Count > 0)
        {
            Select(ids[^1]);
        }

        return ids;
    }

    void DuplicateAndMove(bool linked)
    {
        if (DuplicateObjects(linked).Count > 0)
        {
            // Blender: duplicate, then grab.
            ViewKey(Key.G, ModifierKeys.None);
        }
    }

    int AddObjectCopy(MeshData mesh, ObjectTransform transform, string name, string? source, PrimitiveSpec? spec)
    {
        var obj = new SceneObject { Id = _nextId++, Name = UniqueName(BaseName(name)), SourcePath = source, Mesh = mesh };
        obj.Transform = transform;
        _objects.Add(obj);
        if (spec != null)
        {
            _primitiveSpecs[obj.Id] = spec;
        }

        _renderer?.AddMesh(obj.Id, mesh);
        _renderer?.SetWorld(obj.Id, obj.World);
        PushUndo(AddEntryId(obj.Id), obj.Transform, obj.Transform);
        UpdateSceneBounds();
        Invalidate();
        SceneChanged?.Invoke(this, EventArgs.Empty);
        ViewChanged?.Invoke(this, EventArgs.Empty);
        return obj.Id;
    }

    // ---------------- rename ----------------

    void RenameActiveDialog()
    {
        if (!TryGetObject(_selectedId, out var o))
        {
            return;
        }

        var box = new TextBox
        {
            Text = o.Name, Margin = new Thickness(10), MinWidth = 260,
            Background = new SolidColorBrush(Color.FromRgb(0x1D, 0x1D, 0x1D)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x47, 0x72, 0xB3)),
            CaretBrush = Brushes.White, Padding = new Thickness(4, 2, 4, 2),
        };
        var dlg = new Window
        {
            Title = Z("重命名活动物体", "Rename Active Object"), Content = box, SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this), Background = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)),
        };
        bool ok = false;
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { ok = true; dlg.Close(); }
            else if (e.Key == Key.Escape) { dlg.Close(); }
        };
        dlg.SourceInitialized += (_, _) => DarkTitleBar.Apply(dlg);
        dlg.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        dlg.ShowDialog();
        if (ok)
        {
            RenameObject(o.Id, box.Text);
        }
    }

    // ---------------- mirror / snap ----------------

    /// <summary>Mirror selected objects across a global axis through each object's origin (0 = X, 1 = Y, 2 = Z).</summary>
    public int MirrorSelected(int axis)
    {
        var s = new Vector3(axis == 0 ? -1 : 1, axis == 1 ? -1 : 1, axis == 2 ? -1 : 1);
        int n = 0;
        foreach (var o in SelectedObjects())
        {
            var p = o.Transform.Position;
            var m = o.World * Matrix4x4.CreateTranslation(-p) * Matrix4x4.CreateScale(s) * Matrix4x4.CreateTranslation(p);
            if (!Matrix4x4.Decompose(m, out var scale, out var rot, out var pos))
            {
                continue;
            }

            var t = new ObjectTransform { Position = pos, Rotation = Quaternion.Normalize(rot), Scale = scale };
            SetTransform(o.Id, t);
            n++;
        }

        return n;
    }

    public enum SnapMode { SelectionToCursor, CursorToSelected, CursorToWorld }

    public void Snap(SnapMode mode)
    {
        var sel = SelectedObjects();
        switch (mode)
        {
            case SnapMode.CursorToWorld:
                Cursor3D = Vector3.Zero;
                break;
            case SnapMode.CursorToSelected when sel.Count > 0:
                Cursor3D = sel.Aggregate(Vector3.Zero, (a, o) => a + o.Transform.Position) / sel.Count;
                break;
            case SnapMode.SelectionToCursor:
                foreach (var o in sel)
                {
                    var t = o.Transform;
                    t.Position = Cursor3D;
                    SetTransform(o.Id, t);
                }

                break;
        }

        Invalidate();
    }

    // ---------------- delete ----------------

    /// <summary>X: ask, then delete the selection. Returns the number deleted.</summary>
    public int DeleteSelectedConfirm(bool ask = true)
    {
        var sel = SelectedObjects();
        if (sel.Count == 0)
        {
            return 0;
        }

        if (ask && PvMessageDialog.Show(Window.GetWindow(this), Z($"删除 {sel.Count} 个物体？", $"Delete {sel.Count} object(s)?"),
                Z("删除", "Delete"), MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            return 0;
        }

        foreach (var o in sel)
        {
            RemoveObject(o.Id);
        }

        return sel.Count;
    }
}

/// <summary>Mesh data-block operations (return new MeshData; inputs are not modified).</summary>
static class MeshOps
{
    static MeshData With(MeshData m, Vertex[] v, uint[] idx, Vector3 min, Vector3 max) =>
        new()
        {
            Name = m.Name, Vertices = v, Indices = idx, Min = min, Max = max, Parts = m.Parts, Textures = m.Textures,
            Notice = m.Notice, Nodes = m.Nodes, World = m.World,
        };

    public static MeshData Clone(MeshData m) => With(m, (Vertex[])m.Vertices.Clone(), (uint[])m.Indices.Clone(), m.Min, m.Max);

    public static MeshData Translate(MeshData m, Vector3 d)
    {
        var v = (Vertex[])m.Vertices.Clone();
        for (int i = 0; i < v.Length; i++)
        {
            v[i].Position += d;
        }

        return With(m, v, (uint[])m.Indices.Clone(), m.Min + d, m.Max + d);
    }

    public static Vector3 SurfaceCentroid(MeshData m)
    {
        var sum = Vector3.Zero;
        float area = 0;
        for (int i = 0; i + 2 < m.Indices.Length; i += 3)
        {
            var a = m.Vertices[m.Indices[i]].Position;
            var b = m.Vertices[m.Indices[i + 1]].Position;
            var c = m.Vertices[m.Indices[i + 2]].Position;
            float w = Vector3.Cross(b - a, c - a).Length() * 0.5f;
            sum += (a + b + c) / 3f * w;
            area += w;
        }

        return area > 1e-12f ? sum / area : (m.Min + m.Max) * 0.5f;
    }

    /// <summary>
    /// Per-corner normals: each corner averages (area weighted) the face normals of triangles sharing its position whose
    /// normal is within angleDeg of its own face. 0 = flat, 180 = smooth. Vertices are split per corner (indices 0..n-1),
    /// so the index order (and therefore MeshPart ranges) stays the same.
    /// </summary>
    public static MeshData Shade(MeshData m, float angleDeg)
    {
        int n = m.Indices.Length - m.Indices.Length % 3;
        var v = new Vertex[n];
        var idx = new uint[n];
        var faceN = new Vector3[n / 3];
        for (int i = 0; i < n; i++)
        {
            v[i] = m.Vertices[m.Indices[i]];
            idx[i] = (uint)i;
        }

        for (int f = 0; f < n / 3; f++)
        {
            // Area-weighted (unnormalised) face normal.
            faceN[f] = Vector3.Cross(v[f * 3 + 1].Position - v[f * 3].Position, v[f * 3 + 2].Position - v[f * 3].Position);
        }

        if (angleDeg <= 0f)
        {
            for (int i = 0; i < n; i++)
            {
                v[i].Normal = SafeNormalize(faceN[i / 3], v[i].Normal);
            }

            return With(m, v, idx, m.Min, m.Max);
        }

        // Group corners by (quantised) position.
        float q = 1e5f / MathF.Max(1e-6f, (m.Max - m.Min).Length());
        var groups = new Dictionary<(long, long, long), List<int>>();
        for (int i = 0; i < n; i++)
        {
            var p = v[i].Position * q;
            var key = ((long)MathF.Round(p.X), (long)MathF.Round(p.Y), (long)MathF.Round(p.Z));
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = [];
            }

            list.Add(i);
        }

        float cos = MathF.Cos(angleDeg * MathF.PI / 180f);
        var result = new Vector3[n];
        foreach (var list in groups.Values)
        {
            foreach (int i in list)
            {
                var own = SafeNormalize(faceN[i / 3], Vector3.UnitZ);
                var sum = Vector3.Zero;
                var seen = new HashSet<int>();
                foreach (int j in list)
                {
                    int f = j / 3;
                    if (!seen.Add(f))
                    {
                        continue;
                    }

                    if (angleDeg >= 180f || Vector3.Dot(own, SafeNormalize(faceN[f], own)) >= cos - 1e-4f)
                    {
                        sum += faceN[f];
                    }
                }

                result[i] = SafeNormalize(sum, own);
            }
        }

        for (int i = 0; i < n; i++)
        {
            v[i].Normal = result[i];
        }

        return With(m, v, idx, m.Min, m.Max);
    }

    static Vector3 SafeNormalize(Vector3 v, Vector3 fallback) => v.LengthSquared() > 1e-20f ? Vector3.Normalize(v) : fallback;
}
