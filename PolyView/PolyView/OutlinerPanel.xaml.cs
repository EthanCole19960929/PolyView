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

        ApplyLanguage();
    }

    public void Attach(ModelViewport viewport)
    {
        _viewport = viewport;
        viewport.SceneChanged += (_, _) => Rebuild();
        viewport.SelectionChanged += (_, _) => SyncFromViewport();
        viewport.ObjectChanged += (_, id) =>
        {
            foreach (var item in AllItems())
            {
                if (item.ObjectId == id)
                {
                    item.SetVisibleSilently(item.NodeIndex < 0
                        ? viewport.TryGetObjectInfo(id, out var info) && info.Visible
                        : viewport.IsNodeVisible(id, item.NodeIndex));
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
            (item.Expanded ? expanded : collapsed).Add((item.ObjectId, item.NodeIndex));
        }

        _roots.Clear();
        var scene = new OutlinerItem(this, OutlinerKind.Scene, UiText.OutlinerScene, SpecialId, -1);
        var collection = new OutlinerItem(this, OutlinerKind.Collection, "Collection", SpecialId - 1, -1);
        scene.Add(collection);
        collection.Add(new OutlinerItem(this, OutlinerKind.Camera, UiText.KindCamera, SpecialId - 2, -1));
        collection.Add(new OutlinerItem(this, OutlinerKind.Light, UiText.KindLight, SpecialId - 3, -1));
        foreach (int id in _viewport.ObjectIds)
        {
            if (!_viewport.TryGetObjectInfo(id, out var info))
            {
                continue;
            }

            var viewport = _viewport;
            var file = new OutlinerItem(this, OutlinerKind.File, info.Name, id, -1, v => viewport.SetObjectVisible(id, v));
            file.SetVisibleSilently(info.Visible);
            collection.Add(file);
            var nodes = _viewport.GetNodes(id);
            var created = new OutlinerItem[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                int nodeIndex = node.Index;
                var item = new OutlinerItem(this, KindOf(node.Kind), NodeName(node), id, nodeIndex, v => viewport.SetNodeVisible(id, nodeIndex, v));
                item.SetVisibleSilently(_viewport.IsNodeVisible(id, nodeIndex));
                created[i] = item;
                var parent = node.Parent >= 0 && node.Parent < i ? created[node.Parent] : file;
                parent.Add(item);
            }

            // 只有一个根节点、和文件同为网格且没有子节点时，不再重复显示一层。
            if (file.Children.Count == 1 && file.Children[0].Children.Count == 0 && file.Children[0].Kind == OutlinerKind.Mesh)
            {
                file.Children.Clear();
            }
        }

        _roots.Add(scene);
        _roots.Add(new OutlinerItem(this, OutlinerKind.World, UiText.KindWorld, SpecialId - 4, -1));
        foreach (var item in AllItems())
        {
            var key = (item.ObjectId, item.NodeIndex);
            item.SetExpandedSilently(collapsed.Contains(key) ? false
                : expanded.Contains(key) || item.Kind is OutlinerKind.Scene or OutlinerKind.Collection);
        }

        Flatten();
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
            SceneNodeKind.Light => UiText.KindLight,
            _ => UiText.KindEmpty,
        };
    }

    internal void Flatten()
    {
        var selected = ItemList.SelectedItem as OutlinerItem;
        _syncing = true;
        _rows.Clear();
        foreach (var root in _roots)
        {
            Add(root, 0);
        }

        if (selected != null)
        {
            foreach (var row in _rows)
            {
                if (row.ObjectId == selected.ObjectId && row.NodeIndex == selected.NodeIndex)
                {
                    ItemList.SelectedItem = row;
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
        ItemList.SelectedItem = target;
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

        var item = ItemList.SelectedItem as OutlinerItem;
        _syncing = true;
        if (item != null && item.ObjectId >= 0)
        {
            _viewport.SelectNode(item.ObjectId, item.NodeIndex);
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
                text += $"\n\n{NodeName(node)}  [{node.Kind}]";
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

public enum OutlinerKind
{
    Scene,
    Collection,
    World,
    File,
    Mesh,
    Camera,
    Light,
    Empty,
}

public sealed class OutlinerItem : INotifyPropertyChanged
{
    static readonly Brush Orange = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xA3, 0x3C)));
    static readonly Brush White = Freeze(new SolidColorBrush(Color.FromRgb(0xDA, 0xDA, 0xDA)));
    static readonly Brush WorldBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0x60, 0x60)));
    readonly OutlinerPanel _owner;
    readonly Action<bool>? _onVisibleChanged;
    bool _visible = true;
    bool _renderable = true;
    bool _expanded;
    int _depth;

    public OutlinerItem(OutlinerPanel owner, OutlinerKind kind, string name, int objectId, int nodeIndex, Action<bool>? onVisibleChanged = null)
    {
        _owner = owner;
        Kind = kind;
        Name = name;
        ObjectId = objectId;
        NodeIndex = nodeIndex;
        _onVisibleChanged = onVisibleChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public OutlinerKind Kind { get; }
    public string Name { get; }

    /// <summary>对象 id（文件 / 节点行）；场景、集合、视口相机 / 灯光、世界为负数。</summary>
    public int ObjectId { get; }

    /// <summary>MeshData.Nodes 下标；文件行和特殊行为 -1。</summary>
    public int NodeIndex { get; }

    public OutlinerItem? Parent { get; private set; }
    public List<OutlinerItem> Children { get; } = [];

    public void Add(OutlinerItem child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public int Depth
    {
        get => _depth;
        set
        {
            if (_depth != value)
            {
                _depth = value;
                Raise(nameof(IndentMargin));
            }
        }
    }

    public Thickness IndentMargin => new(4 + _depth * 14, 0, 2, 0);

    public bool Matches(string filter)
    {
        if (Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var child in Children)
        {
            if (child.Matches(filter))
            {
                return true;
            }
        }

        return false;
    }

    public bool Expanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value)
            {
                return;
            }

            _expanded = value;
            Raise(nameof(Expanded));
            Raise(nameof(ChevronGlyph));
            _owner.Flatten();
        }
    }

    public void SetExpandedSilently(bool expanded)
    {
        _expanded = expanded;
        Raise(nameof(Expanded));
        Raise(nameof(ChevronGlyph));
    }

    public string ChevronGlyph => _expanded ? "\uE70D" : "\uE76C";

    public Visibility ChevronVisibility => Children.Count > 0 ? Visibility.Visible : Visibility.Hidden;

    // Segoe MDL2 / Segoe UI Symbol：集合、世界、相机、灯、网格（三角形）、空节点
    public string Icon => Kind switch
    {
        OutlinerKind.Scene => "\uE8B7",
        OutlinerKind.Collection => "",
        OutlinerKind.World => "\uE909",
        OutlinerKind.Camera => "\uE722",
        OutlinerKind.Light => "\uEA80",
        OutlinerKind.Empty => "\uE8FD",
        _ => "\u25BD",
    };

    public FontFamily IconFont => Kind is OutlinerKind.File or OutlinerKind.Mesh ? SymbolFont : IconFontFamily;

    static readonly FontFamily IconFontFamily = new("Segoe MDL2 Assets");
    static readonly FontFamily SymbolFont = new("Segoe UI Symbol");

    public Brush IconBrush => Kind switch
    {
        OutlinerKind.Scene => White,
        OutlinerKind.World => WorldBrush,
        _ => Orange,
    };

    public Visibility CheckVisibility => Kind == OutlinerKind.Collection ? Visibility.Visible : Visibility.Collapsed;

    public string DataGlyph => Kind is OutlinerKind.File or OutlinerKind.Mesh ? "\u25BC" : "";

    public Visibility ToggleVisibility => ObjectId >= 0 ? Visibility.Visible : Visibility.Hidden;

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value)
            {
                return;
            }

            _visible = value;
            RaiseVisible();
            _onVisibleChanged?.Invoke(value);
        }
    }

    /// <summary>渲染开关（相机图标）。本程序没有离线渲染，只记录状态，和 Blender 外观保持一致。</summary>
    public bool Renderable
    {
        get => _renderable;
        set
        {
            _renderable = value;
            Raise(nameof(Renderable));
        }
    }

    public string EyeGlyph => _visible ? "\uE890" : "\uED1A";

    public double NameOpacity => _visible ? 1.0 : 0.45;

    public string EyeTip => UiText.ToggleVisibility;

    public void SetVisibleSilently(bool visible)
    {
        if (_visible == visible)
        {
            return;
        }

        _visible = visible;
        RaiseVisible();
    }

    void RaiseVisible()
    {
        Raise(nameof(Visible));
        Raise(nameof(EyeGlyph));
        Raise(nameof(NameOpacity));
    }

    void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
