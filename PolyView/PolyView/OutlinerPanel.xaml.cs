using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PolyView;

/// <summary>
/// Blender 式场景大纲（右上）+ 变换 / 属性面板（右下，中间可拖动分隔）。
/// 树：场景集合 → Collection → 视口相机、视口灯光、每个文件（其下按 MeshData.Nodes 缩进显示节点）；世界单独一行。
/// 列表是扁平化后的虚拟化 ListBox（Recycling），展开 / 折叠 / 搜索时重新扁平化。
/// </summary>
public partial class OutlinerPanel : UserControl
{
    public const int SpecialId = -1000;

    readonly ObservableCollection<OutlinerItem> _rows = [];
    readonly List<OutlinerItem> _roots = [];
    readonly TextBox[] _transformBoxes = new TextBox[9];
    ModelViewport? _viewport;
    bool _syncing;
    string _filter = "";

    public OutlinerPanel()
    {
        InitializeComponent();
        ItemList.ItemsSource = _rows;
        for (int i = 0; i < 9; i++)
        {
            var box = new TextBox { Style = (Style)Resources["PanelBox"] };
            int index = i;
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    CommitTransformBoxes();
                    e.Handled = true;
                }
            };
            box.LostKeyboardFocus += (_, _) => CommitTransformBoxes();
            Grid.SetRow(box, index / 3);
            Grid.SetColumn(box, 1 + index % 3);
            TransformGrid.Children.Add(box);
            _transformBoxes[i] = box;
        }

        InitCollectionsUi();
        ApplyLanguage();
    }

    public void Attach(ModelViewport viewport)
    {
        _viewport = viewport;
        viewport.SceneChanged += (_, _) => Rebuild();
        viewport.SelectionChanged += (_, _) => SyncFromViewport();
        viewport.MoveToCollectionRequested += (_, link) => ShowMoveToCollectionMenu(SelectedObjectIds(), viewport, link);
        viewport.ObjectChanged += (_, id) =>
        {
            if (_applying)
            {
                return;
            }

            foreach (var item in AllItems())
            {
                if (item.ObjectId == id)
                {
                    if (item.NodeIndex < 0)
                    {
                        if ((Root.FindOwner(id)?.EffectiveShown ?? true) && viewport.TryGetObjectInfo(id, out var info))
                        {
                            _ownVisible[id] = info.Visible;
                            item.SetVisibleSilently(info.Visible);
                        }
                    }
                    else
                    {
                        item.SetVisibleSilently(viewport.IsNodeVisible(id, item.NodeIndex));
                    }
                }
            }

            if (id == viewport.SelectedId)
            {
                UpdateProperties();
            }
        };
        Rebuild();
    }

    public void ApplyLanguage()
    {
        SearchHint.Text = _filter.Length == 0 ? UiText.OutlinerSearch : "";
        TransformHeader.Text = UiText.TransformTitle;
        LocationLabel.Text = UiText.TransformLocation;
        RotationLabel.Text = UiText.TransformRotation;
        ScaleLabel.Text = UiText.TransformScale;
        PropertyHeader.Text = UiText.PropTitle;
        if (_viewport != null)
        {
            Rebuild();
        }
    }

    // ---------------- 树 ----------------

    IEnumerable<OutlinerItem> AllItems()
    {
        var stack = new Stack<OutlinerItem>(_roots);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            yield return item;
            foreach (var child in item.Children)
            {
                stack.Push(child);
            }
        }
    }

    void Rebuild()
    {
        if (_viewport == null)
        {
            return;
        }

        var expanded = new HashSet<(int, int)>();
        var collapsed = new HashSet<(int, int)>();
        foreach (var item in AllItems())
        {
            if (item.Collection == null)
            {
                (item.Expanded ? expanded : collapsed).Add((item.ObjectId, item.NodeIndex));
            }
        }

        SyncMembership();
        _roots.Clear();
        var scene = new OutlinerItem(this, OutlinerKind.Scene, UiText.OutlinerScene, CollectionRowId(Root), -1) { Collection = Root };
        BuildCollection(scene, Root);
        _roots.Add(scene);
        foreach (var item in AllItems())
        {
            if (item.Collection != null)
            {
                item.SetExpandedSilently(item.Kind == OutlinerKind.Scene || item.Collection.Expanded);
                continue;
            }

            var key = (item.ObjectId, item.NodeIndex);
            item.SetExpandedSilently(!collapsed.Contains(key) && expanded.Contains(key));
        }

        ApplyCollection();
        Flatten();
    }

    static int CollectionRowId(SceneCollection c) => -2000 - c.Id;

    void BuildCollection(OutlinerItem row, SceneCollection collection)
    {
        foreach (var child in collection.Children)
        {
            var item = new OutlinerItem(this, OutlinerKind.Collection, child.Name, CollectionRowId(child), -1) { Collection = child };
            item.InitCollection(child.Included, child.Visible, child.Renderable);
            row.Add(item);
            BuildCollection(item, child);
        }

        foreach (int id in collection.Objects)
        {
            BuildObject(row, id);
        }
    }

    void BuildObject(OutlinerItem parentRow, int id)
    {
        if (_viewport == null || !_viewport.TryGetObjectInfo(id, out var info))
        {
            return;
        }

        if (!_ownVisible.ContainsKey(id))
        {
            _ownVisible[id] = info.Visible;
        }

        var viewport = _viewport;
        var nodes = _viewport.GetNodes(id);
        var file = new OutlinerItem(this, FileKind(nodes), info.Name, id, -1, v =>
        {
            _ownVisible[id] = v;
            ApplyCollection();
        });
        file.SetVisibleSilently(_ownVisible[id]);
        file.SetRenderableSilently(!_renderOff.Contains((id, -1)));
        parentRow.Add(file);
        var created = new OutlinerItem[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            int nodeIndex = node.Index;
            var item = new OutlinerItem(this, KindOf(node.Kind), NodeName(node), id, nodeIndex, v => viewport.SetNodeVisible(id, nodeIndex, v));
            item.SetVisibleSilently(_viewport.IsNodeVisible(id, nodeIndex));
            item.SetRenderableSilently(!_renderOff.Contains((id, nodeIndex)));
            created[i] = item;
            var parent = node.Parent >= 0 && node.Parent < i ? created[node.Parent] : file;
            parent.Add(item);
        }

        // A single childless root node of the same kind as the object is not repeated as an extra level.
        if (file.Children.Count == 1 && file.Children[0].Children.Count == 0 && file.Children[0].Kind == file.Kind)
        {
            file.Children.Clear();
        }

        // Blender object / data split: the mesh data-block (green) and its materials sit under the object row.
        if (file.Kind == OutlinerKind.Mesh && file.Children.Count == 0)
        {
            var data = new OutlinerItem(this, OutlinerKind.MeshDataRow, _viewport.GetMeshDataName(id), id, -2);
            file.Add(data);
            int materials = _viewport.GetMaterialCount(id);
            for (int m = 0; m < materials; m++)
            {
                data.Add(new OutlinerItem(this, OutlinerKind.Material, m == 0 ? "Material" : $"Material.{m:000}", id, -3 - m));
            }
        }
    }

    // ---------------- Collection toggles (exclude / hide / disable in renders) ----------------

    readonly Dictionary<int, bool> _ownVisible = [];
    readonly HashSet<(int, int)> _renderOff = [];
    bool _applying;

    internal void OnRenderableChanged(OutlinerItem item)
    {
        if (item.Collection is { } c)
        {
            c.Renderable = item.Renderable;
            ApplyCollection();
            OnCollectionsChanged();
            return;
        }

        if (item.Renderable)
        {
            _renderOff.Remove((item.ObjectId, item.NodeIndex));
        }
        else
        {
            _renderOff.Add((item.ObjectId, item.NodeIndex));
        }
    }

    internal void OnCollectionToggled(OutlinerItem row)
    {
        if (row.Collection is not { } c)
        {
            return;
        }

        c.Included = row.Included;
        c.Visible = row.Visible;
        c.Renderable = row.Renderable;
        ApplyCollection();
        OnCollectionsChanged();
    }

    /// <summary>Pushes collection state to the viewport: an excluded or hidden collection hides (and makes unpickable) every object below it.</summary>
    internal void ApplyCollection()
    {
        if (_viewport == null)
        {
            return;
        }

        var owner = new Dictionary<int, SceneCollection>();
        _applying = true;
        try
        {
            var shownIds = new Dictionary<int, bool>();
            foreach (var c in Root.SelfAndDescendants())
            {
                bool shown = c.EffectiveShown;
                foreach (int id in c.Objects)
                {
                    if (!owner.TryGetValue(id, out var prev) || (shown && !prev.EffectiveShown))
                    {
                        owner[id] = c;
                    }

                    shownIds[id] = shownIds.GetValueOrDefault(id) || shown;
                }
            }

            foreach (var (id, shown) in shownIds)
            {
                bool own = _ownVisible.TryGetValue(id, out var v) ? v : true;
                _viewport.SetObjectVisible(id, own && shown);
            }
        }
        finally
        {
            _applying = false;
        }

        foreach (var item in AllItems())
        {
            if (item.Collection is { Parent: { } p })
            {
                item.SetInherited(!p.EffectiveShown, p.EffectiveExcluded, p.EffectiveRenderOff);
            }
            else if (item.ObjectId >= 0 && owner.TryGetValue(item.ObjectId, out var c))
            {
                item.SetInherited(!c.EffectiveShown, c.EffectiveExcluded, c.EffectiveRenderOff);
            }
        }

        _viewport.Invalidate();
    }

    static void SetInherited(OutlinerItem item, bool off, bool excluded, bool renderOff)
    {
        item.SetInherited(off, excluded, renderOff);
        foreach (var child in item.Children)
        {
            SetInherited(child, off, excluded, renderOff);
        }
    }

    static OutlinerKind FileKind(IReadOnlyList<SceneNode> nodes)
    {
        if (nodes.Count == 0)
        {
            return OutlinerKind.Mesh;
        }

        bool allLights = true, allCameras = true, anyMesh = false;
        foreach (var n in nodes)
        {
            allLights &= n.Kind is SceneNodeKind.Light or SceneNodeKind.Empty;
            allCameras &= n.Kind is SceneNodeKind.Camera or SceneNodeKind.Empty;
            anyMesh |= n.Kind == SceneNodeKind.Mesh;
        }

        if (!anyMesh && allLights && nodes.Any(n => n.Kind == SceneNodeKind.Light))
        {
            return OutlinerKind.Light;
        }

        if (!anyMesh && allCameras && nodes.Any(n => n.Kind == SceneNodeKind.Camera))
        {
            return OutlinerKind.Camera;
        }

        return anyMesh ? OutlinerKind.Mesh : OutlinerKind.Empty;
    }

    static OutlinerKind KindOf(SceneNodeKind kind) => kind switch
    {
        SceneNodeKind.Mesh => OutlinerKind.Mesh,
        SceneNodeKind.Camera => OutlinerKind.Camera,
        SceneNodeKind.Light => OutlinerKind.Light,
        _ => OutlinerKind.Empty,
    };

    static string NodeName(SceneNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.Name))
        {
            return node.Name;
        }

        return node.Kind switch
        {
            SceneNodeKind.Mesh => UiText.KindMesh,
            SceneNodeKind.Camera => UiText.KindCamera,
            SceneNodeKind.Light => node.Light != null ? ModelViewport.LightTypeName(node.Light.Type) : UiText.KindLight,
            _ => UiText.KindEmpty,
        };
    }

    internal void Flatten()
    {
        var selectedKeys = ItemList.SelectedItems.OfType<OutlinerItem>().Select(RowKey).ToHashSet();
        _syncing = true;
        _rows.Clear();
        foreach (var root in _roots)
        {
            Add(root, 0);
        }

        if (selectedKeys.Count > 0)
        {
            foreach (var row in _rows)
            {
                if (selectedKeys.Contains(RowKey(row)))
                {
                    ItemList.SelectedItems.Add(row);
                }
            }
        }

        _syncing = false;
    }

    void Add(OutlinerItem item, int depth)
    {
        if (_filter.Length > 0 && !item.Matches(_filter))
        {
            return;
        }

        item.Depth = depth;
        _rows.Add(item);
        // 搜索时自动展开有匹配的分支
        if (item.Expanded || _filter.Length > 0)
        {
            foreach (var child in item.Children)
            {
                Add(child, depth + 1);
            }
        }
    }

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filter = SearchBox.Text.Trim();
        SearchHint.Text = _filter.Length == 0 ? UiText.OutlinerSearch : "";
        Flatten();
    }

    // ---------------- 选择同步 ----------------

    void SyncFromViewport()
    {
        if (_viewport == null || _syncing)
        {
            return;
        }

        int id = _viewport.SelectedId;
        int node = _viewport.SelectedNode;
        if (id >= 0 && Root.FindOwner(id) is { } owner)
        {
            _active = owner;
        }
        OutlinerItem? target = null;
        if (id >= 0)
        {
            foreach (var item in AllItems())
            {
                if (item.ObjectId == id && item.NodeIndex == node)
                {
                    target = item;
                }
            }

            // 展开祖先，保证能滚动到
            for (var p = target?.Parent; p != null; p = p.Parent)
            {
                p.SetExpandedSilently(true);
            }

            Flatten();
        }

        _syncing = true;
        // Keep a multi-selection when the viewport's active object is already part of it.
        bool keep = target != null && ItemList.SelectedItems.OfType<OutlinerItem>().Any(r => RowKey(r) == RowKey(target));
        if (!keep)
        {
            ItemList.SelectedItem = target;
        }

        if (target != null)
        {
            ItemList.ScrollIntoView(target);
        }

        _syncing = false;
        UpdateProperties();
    }

    void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _viewport == null)
        {
            return;
        }

        // Active row = the one just clicked (Extended selection: Ctrl toggles, Shift selects a range).
        var item = e.AddedItems.Count > 0 ? e.AddedItems[^1] as OutlinerItem
            : ItemList.SelectedItems.Count > 0 ? ItemList.SelectedItems[^1] as OutlinerItem : null;
        if (e.AddedItems.Count == 0 && e.RemovedItems.Count > 0 && ItemList.SelectedItems.Count > 0)
        {
            return;
        }

        _syncing = true;
        if (item?.Collection != null)
        {
            _active = item.Collection;
        }

        if (item != null && item.ObjectId >= 0)
        {
            _active = Root.FindOwner(item.ObjectId) ?? _active;
            _viewport.SelectNode(item.ObjectId, item.IsDataRow ? -1 : item.NodeIndex);
        }
        else
        {
            // 场景 / 集合 / 视口相机 / 灯光 / 世界：视口里不选中网格
            _viewport.Select(-1);
        }

        _syncing = false;
        UpdateProperties();
    }

    // ---------------- 属性 / 变换 ----------------

    void UpdateProperties()
    {
        var item = ItemList.SelectedItem as OutlinerItem;
        bool hasObject = _viewport != null && _viewport.TryGetObjectInfo(_viewport.SelectedId, out _);
        TransformGrid.IsEnabled = hasObject;
        if (_viewport != null && _viewport.TryGetTransform(_viewport.SelectedId, out var t) && hasObject)
        {
            var e = t.EulerDegrees;
            float[] values = [t.Position.X, t.Position.Y, t.Position.Z, e.X, e.Y, e.Z, t.Scale.X, t.Scale.Y, t.Scale.Z];
            for (int i = 0; i < 9; i++)
            {
                if (!_transformBoxes[i].IsKeyboardFocused)
                {
                    _transformBoxes[i].Text = values[i].ToString(i is >= 3 and < 6 ? "0.#" : "0.###", CultureInfo.InvariantCulture);
                }
            }
        }
        else
        {
            foreach (var box in _transformBoxes)
            {
                box.Text = "";
            }
        }

        if (_viewport == null)
        {
            PropertyText.Text = "";
            return;
        }

        if (item?.Kind == OutlinerKind.Camera && item.ObjectId < 0)
        {
            PropertyText.Text = _viewport.Orthographic ? UiText.Ortho : UiText.Perspective;
            return;
        }

        if (item?.Kind == OutlinerKind.Light && item.ObjectId < 0)
        {
            PropertyText.Text = UiText.Chinese ? "主光从相机右上方照射，随视角转动。" : "Key light from the camera's upper right; follows the view.";
            return;
        }

        if (!_viewport.TryGetObjectInfo(_viewport.SelectedId, out var info))
        {
            PropertyText.Text = "";
            return;
        }

        static string V(Vector3 v) => $"{v.X:0.###}, {v.Y:0.###}, {v.Z:0.###}";
        var text = $"{info.Name}  (ID {info.Id})\n"
            + $"{UiText.PropVertices}  {info.VertexCount:N0}\n"
            + $"{UiText.PropTriangles}  {info.TriangleCount:N0}\n"
            + $"{UiText.PropSize}  {V(info.Size)}\n"
            + $"Min  {V(info.Min)}\nMax  {V(info.Max)}";
        if (_viewport.SelectedNode >= 0)
        {
            var nodes = _viewport.GetNodes(info.Id);
            if (_viewport.SelectedNode < nodes.Count)
            {
                var node = nodes[_viewport.SelectedNode];
                text += $"\n\n{NodeName(node)}  [{(node.Light != null ? ModelViewport.LightTypeName(node.Light.Type) : node.Kind.ToString())}]";
            }
        }

        if (!string.IsNullOrEmpty(info.SourcePath))
        {
            text += "\n" + info.SourcePath;
        }

        PropertyText.Text = text;
    }

    void CommitTransformBoxes()
    {
        if (_viewport == null || !_viewport.TryGetTransform(_viewport.SelectedId, out var t))
        {
            return;
        }

        var values = new float[9];
        var e = t.EulerDegrees;
        float[] current = [t.Position.X, t.Position.Y, t.Position.Z, e.X, e.Y, e.Z, t.Scale.X, t.Scale.Y, t.Scale.Z];
        for (int i = 0; i < 9; i++)
        {
            values[i] = float.TryParse(_transformBoxes[i].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && float.IsFinite(v)
                ? v
                : current[i];
        }

        var next = t;
        next.Position = new Vector3(values[0], values[1], values[2]);
        var euler = new Vector3(values[3], values[4], values[5]);
        if (Vector3.Distance(euler, e) > 1e-3f)
        {
            next.Rotation = ObjectTransform.FromEuler(euler);
        }

        next.Scale = new Vector3(values[6], values[7], values[8]);
        _viewport.SetTransform(_viewport.SelectedId, next);
    }
}

