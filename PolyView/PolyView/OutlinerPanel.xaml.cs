using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PolyView;

/// <summary>
/// 场景大纲 + 属性栏：列出相机、灯光和每个已加载的模型（目前一个文件一行，导入器输出节点层级后再加缩进子项）。
/// 和视口双向同步选择；眼睛按钮切换可见性；虚拟化列表，千级条目也不卡。
/// </summary>
public partial class OutlinerPanel : UserControl
{
    // 相机 / 灯光不是可拾取的网格对象，用负数 id 区分（-1 已用于「未选中」）。
    public const int CameraId = -100;
    public const int LightId = -101;

    readonly ObservableCollection<OutlinerItem> _items = [];
    ModelViewport? _viewport;
    bool _syncing;

    public OutlinerPanel()
    {
        InitializeComponent();
        ItemList.ItemsSource = _items;
        ApplyLanguage();
    }

    static bool Zh => UiText.Chinese;

    public void Attach(ModelViewport viewport)
    {
        _viewport = viewport;
        viewport.SceneChanged += (_, _) => Rebuild();
        viewport.SelectionChanged += (_, id) => SyncFromViewport(id);
        viewport.ObjectChanged += (_, id) =>
        {
            var item = Find(id);
            if (item != null && viewport.TryGetObjectInfo(id, out var info))
            {
                item.SetVisibleSilently(info.Visible);
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
        SceneHeader.Text = Zh ? "场景大纲" : "Outliner";
        PropertyHeader.Text = Zh ? "属性" : "Properties";
        foreach (var item in _items)
        {
            item.Refresh();
        }

        if (_items.Count > 0)
        {
            _items[0].Name = Zh ? "相机" : "Camera";
        }

        if (_items.Count > 1)
        {
            _items[1].Name = Zh ? "灯光（跟随相机）" : "Light (follows camera)";
        }

        UpdateProperties();
    }

    void Rebuild()
    {
        if (_viewport == null)
        {
            return;
        }

        _syncing = true;
        _items.Clear();
        _items.Add(new OutlinerItem(CameraId, OutlinerKind.Camera, Zh ? "相机" : "Camera", null));
        _items.Add(new OutlinerItem(LightId, OutlinerKind.Light, Zh ? "灯光（跟随相机）" : "Light (follows camera)", null));
        foreach (int id in _viewport.ObjectIds)
        {
            if (_viewport.TryGetObjectInfo(id, out var info))
            {
                var viewport = _viewport;
                var item = new OutlinerItem(id, OutlinerKind.Mesh, info.Name, visible => viewport.SetObjectVisible(id, visible));
                item.SetVisibleSilently(info.Visible);
                _items.Add(item);
            }
        }

        _syncing = false;
        SyncFromViewport(_viewport.SelectedId);
    }

    OutlinerItem? Find(int id)
    {
        foreach (var item in _items)
        {
            if (item.Id == id)
            {
                return item;
            }
        }

        return null;
    }

    void SyncFromViewport(int id)
    {
        _syncing = true;
        var item = id >= 0 ? Find(id) : null;
        ItemList.SelectedItem = item;
        if (item != null)
        {
            ItemList.ScrollIntoView(item);
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
        // 相机 / 灯光行：视口里不选中任何网格，只在属性栏显示说明。
        _viewport.Select(item is { Kind: OutlinerKind.Mesh } ? item.Id : -1);
        if (item != null && item.Kind != OutlinerKind.Mesh && ItemList.SelectedItem != item)
        {
            _syncing = true;
            ItemList.SelectedItem = item;
            _syncing = false;
        }

        UpdateProperties();
    }

    void UpdateProperties()
    {
        if (_viewport == null)
        {
            PropertyText.Text = "";
            return;
        }

        var selected = ItemList.SelectedItem as OutlinerItem;
        if (selected?.Kind == OutlinerKind.Camera)
        {
            PropertyText.Text = Zh
                ? $"投影  {(_viewport.Orthographic ? "正交" : "透视")}"
                : $"Projection  {(_viewport.Orthographic ? "Orthographic" : "Perspective")}";
            return;
        }

        if (selected?.Kind == OutlinerKind.Light)
        {
            PropertyText.Text = Zh ? "主光从相机右上方照射，随视角转动。" : "Key light from the camera's upper right; follows the view.";
            return;
        }

        if (!_viewport.TryGetObjectInfo(_viewport.SelectedId, out var info))
        {
            PropertyText.Text = Zh ? "（未选中）" : "(nothing selected)";
            return;
        }

        PropertyText.Text = FormatInfo(info, _viewport.GetObjectTransformText(info.Id));
    }

    static string FormatInfo(in SceneObjectInfo info, string transform)
    {
        static string V(Vector3 v) => $"{v.X:0.###}, {v.Y:0.###}, {v.Z:0.###}";
        var text = Zh
            ? $"名称  {info.Name}\nID    {info.Id}\n顶点  {info.VertexCount:N0}\n面    {info.TriangleCount:N0}\n最小  {V(info.Min)}\n最大  {V(info.Max)}\n尺寸  {V(info.Size)}"
            : $"Name  {info.Name}\nID    {info.Id}\nVerts {info.VertexCount:N0}\nFaces {info.TriangleCount:N0}\nMin   {V(info.Min)}\nMax   {V(info.Max)}\nSize  {V(info.Size)}";
        if (transform.Length > 0)
        {
            text += "\n" + transform;
        }

        if (!string.IsNullOrEmpty(info.SourcePath))
        {
            text += (Zh ? "\n文件  " : "\nFile  ") + info.SourcePath;
        }

        return text;
    }
}

public enum OutlinerKind
{
    Mesh,
    Camera,
    Light,
}

public sealed class OutlinerItem : INotifyPropertyChanged
{
    static readonly Brush MeshBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF0, 0xA0, 0x40)));
    static readonly Brush CameraBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x9A, 0xC8, 0xF0)));
    static readonly Brush LightBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0xE0, 0x6A)));
    readonly Action<bool>? _onVisibleChanged;
    string _name;
    bool _visible = true;

    public OutlinerItem(int id, OutlinerKind kind, string name, Action<bool>? onVisibleChanged, int depth = 0)
    {
        Id = id;
        Kind = kind;
        _name = name;
        _onVisibleChanged = onVisibleChanged;
        IndentMargin = new Thickness(depth * 14, 1, 0, 1);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Id { get; }
    public OutlinerKind Kind { get; }
    public Thickness IndentMargin { get; }

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            Raise(nameof(Name));
        }
    }

    // Segoe MDL2 Assets：网格 = 3D 立方体，相机，灯泡。
    public string Icon => Kind switch
    {
        OutlinerKind.Camera => "\uE722",
        OutlinerKind.Light => "\uEA80",
        _ => "\uE81E",
    };

    public Brush IconBrush => Kind switch
    {
        OutlinerKind.Camera => CameraBrush,
        OutlinerKind.Light => LightBrush,
        _ => MeshBrush,
    };

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

    public Visibility EyeVisibility => Kind == OutlinerKind.Mesh ? Visibility.Visible : Visibility.Collapsed;

    // 睁眼 / 闭眼
    public string EyeGlyph => _visible ? "\uE890" : "\uED1A";

    public double NameOpacity => _visible ? 1.0 : 0.45;

    public string EyeTip => UiText.Chinese ? "显示 / 隐藏" : "Show / hide";

    public void SetVisibleSilently(bool visible)
    {
        if (_visible == visible)
        {
            return;
        }

        _visible = visible;
        RaiseVisible();
    }

    public void Refresh() => Raise(nameof(EyeTip));

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
