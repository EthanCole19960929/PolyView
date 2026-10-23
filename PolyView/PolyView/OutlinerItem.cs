using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace PolyView;

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
    /// <summary>Mesh data-block row under an object (green icon), Blender's object/data split.</summary>
    MeshDataRow,
    /// <summary>Material slot row under a mesh data row.</summary>
    Material,
}

/// <summary>One outliner row. Icons are vector geometry (Blender 4.2 look): orange object icon, teal data icon.</summary>
public sealed class OutlinerItem : INotifyPropertyChanged
{
    static readonly Brush Orange = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0x9A, 0x4B)));
    static readonly Brush Teal = Freeze(new SolidColorBrush(Color.FromRgb(0x55, 0xC7, 0xA6)));
    static readonly Brush Green = Freeze(new SolidColorBrush(Color.FromRgb(0x7B, 0xC8, 0x5A)));
    static readonly Brush MaterialRed = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x6C)));
    static readonly Geometry MaterialBall = G("M8,2.5 A5.5,5.5 0 1 1 7.99,2.5 Z");
    static readonly Brush White = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)));

    static readonly Geometry CollectionBox = G("M2,4.5 L8,1.5 L14,4.5 L14,11.5 L8,14.5 L2,11.5 Z M2,4.5 L8,7.5 L14,4.5 M8,7.5 L8,14.5");
    static readonly Geometry MeshObject = G("M2,3.5 L14,3.5 L8,13.5 Z");
    static readonly Geometry CameraObject = G("M1,5 H10 V12 H1 Z M10,7.2 L15,4.5 V12.5 L10,9.8 Z");
    static readonly Geometry LightObject = G("M8,1 C11,1 12.5,3.3 12.5,5.5 C12.5,7.6 10.8,8.6 10.5,10.5 H5.5 C5.2,8.6 3.5,7.6 3.5,5.5 C3.5,3.3 5,1 8,1 Z M5.5,11.5 H10.5 V13 H5.5 Z M6.5,14 H9.5 V15 H6.5 Z");
    static readonly Geometry EmptyObject = G("M8,1.5 V14.5 M1.5,8 H14.5 M3.5,3.5 L12.5,12.5 M12.5,3.5 L3.5,12.5");
    static readonly Geometry MeshData = G("M2,13 L8,2.5 L14,13 Z M1,12 H3 V14 H1 Z M7,1.5 H9 V3.5 H7 Z M13,12 H15 V14 H13 Z");
    static readonly Geometry CameraData = G("M1,4 H11 V12 H1 Z M3,12 L6,15 L9,12 M11,6 L15,4 V12 L11,10");
    static readonly Geometry LightData = G("M8,1.5 A4.5,4.5 0 0 1 10.5,10 V12 H5.5 V10 A4.5,4.5 0 0 1 8,1.5 Z M6,14 H10");
    static readonly Geometry EyeOpen = G("M1,8 C3.5,3.5 12.5,3.5 15,8 C12.5,12.5 3.5,12.5 1,8 Z M8,5.8 A2.2,2.2 0 1 1 7.99,5.8 Z");
    static readonly Geometry EyeClosed = G("M1,7 C3.5,11 12.5,11 15,7 M3.5,9.5 L2.3,11.5 M8,10.5 V12.8 M12.5,9.5 L13.7,11.5");

    readonly OutlinerPanel _owner;
    readonly Action<bool>? _onVisibleChanged;
    bool _visible = true;
    bool _renderable = true;
    bool _included = true;
    bool _expanded;
    bool _inheritedOff;
    bool _inheritedExcluded;
    bool _inheritedRenderOff;
    int _depth;

    public OutlinerItem(OutlinerPanel owner, OutlinerKind kind, string name, int objectId, int nodeIndex, Action<bool>? onVisibleChanged = null)
    {
        _owner = owner;
        Kind = kind == OutlinerKind.File ? OutlinerKind.Mesh : kind;
        Name = name;
        ObjectId = objectId;
        NodeIndex = nodeIndex;
        _onVisibleChanged = onVisibleChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public OutlinerKind Kind { get; }
    public string Name { get; private set; }

    /// <summary>Collection behind a Scene / Collection row.</summary>
    public SceneCollection? Collection { get; set; }

    public bool IsDataRow => Kind is OutlinerKind.MeshDataRow or OutlinerKind.Material;

    bool _editing;

    public bool IsEditing
    {
        get => _editing;
        set
        {
            _editing = value;
            Raise(nameof(IsEditing));
            Raise(nameof(EditVisibility));
            Raise(nameof(NameVisibility));
        }
    }

    public Visibility EditVisibility => _editing ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NameVisibility => _editing ? Visibility.Hidden : Visibility.Visible;

    public void SetName(string name)
    {
        Name = name;
        Raise(nameof(Name));
    }

    /// <summary>Object id (object / node rows); negative for Scene Collection and Collection.</summary>
    public int ObjectId { get; }

    /// <summary>MeshData.Nodes index; -1 for object rows and special rows.</summary>
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
                Raise(nameof(IndentWidth));
                Raise(nameof(GuideLines));
            }
        }
    }

    const double Step = 16;

    // Scene Collection sits flush left; every deeper level shifts by one arrow width.
    public GridLength IndentWidth => new(Math.Max(0, _depth - 1) * Step + 2);

    /// <summary>Thin vertical guide lines under the expand arrows of ancestors (Blender draws them for children of a collection).</summary>
    public IReadOnlyList<double> GuideLines
    {
        get
        {
            var list = new List<double>();
            for (int k = 1; k < _depth; k++)
            {
                list.Add((k - 1) * Step + 2 + Step + 9);
            }

            return list;
        }
    }

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
            if (Collection != null)
            {
                Collection.Expanded = value;
            }

            Raise(nameof(Expanded));
            _owner.Flatten();
        }
    }

    public void SetExpandedSilently(bool expanded)
    {
        _expanded = expanded;
        Raise(nameof(Expanded));
    }

    public Visibility ChevronVisibility => Kind == OutlinerKind.Scene ? Visibility.Collapsed
        : Children.Count > 0 ? Visibility.Visible : Visibility.Hidden;

    public Geometry? IconFill => Kind switch
    {
        OutlinerKind.Mesh => MeshObject,
        OutlinerKind.Camera => CameraObject,
        OutlinerKind.Light => LightObject,
        OutlinerKind.Material => MaterialBall,
        _ => null,
    };

    public Geometry? IconStroke => Kind switch
    {
        OutlinerKind.Scene or OutlinerKind.Collection => CollectionBox,
        OutlinerKind.Empty => EmptyObject,
        OutlinerKind.MeshDataRow => MeshData,
        _ => null,
    };

    public Brush IconBrush => Kind switch
    {
        OutlinerKind.Scene or OutlinerKind.Collection => White,
        OutlinerKind.MeshDataRow => Green,
        OutlinerKind.Material => MaterialRed,
        _ => Orange,
    };

    public Geometry? DataIcon => IsDataRow ? null : Kind switch
    {
        OutlinerKind.Mesh => MeshData,
        OutlinerKind.Camera => CameraData,
        OutlinerKind.Light => LightData,
        _ => null,
    };

    public Brush? DataFill => null;

    public bool IsCollection => Kind == OutlinerKind.Collection;

    public Visibility CheckVisibility => IsCollection ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CheckMarkVisibility => _included ? Visibility.Visible : Visibility.Hidden;

    public Visibility ToggleVisibility => (ObjectId >= 0 && !IsDataRow) || IsCollection ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Collection "exclude from view layer" checkbox.</summary>
    public bool Included
    {
        get => _included;
        set
        {
            if (_included == value)
            {
                return;
            }

            _included = value;
            Raise(nameof(Included));
            Raise(nameof(CheckMarkVisibility));
            Raise(nameof(RowOpacity));
            _owner.OnCollectionToggled(this);
        }
    }

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
            if (IsCollection)
            {
                _owner.OnCollectionToggled(this);
            }
            else
            {
                _onVisibleChanged?.Invoke(value);
            }
        }
    }

    /// <summary>Render toggle (camera icon). PolyView has no offline render; the state is stored and inherited, like Blender's "Disable in Renders".</summary>
    public bool Renderable
    {
        get => _renderable;
        set
        {
            if (_renderable == value)
            {
                return;
            }

            _renderable = value;
            Raise(nameof(Renderable));
            Raise(nameof(RenderOpacity));
            _owner.OnRenderableChanged(this);
        }
    }

    public void InitCollection(bool included, bool visible, bool renderable)
    {
        _included = included;
        _visible = visible;
        _renderable = renderable;
    }

    public void SetRenderableSilently(bool renderable)
    {
        _renderable = renderable;
        Raise(nameof(Renderable));
        Raise(nameof(RenderOpacity));
    }

    public void SetInherited(bool off, bool excluded, bool renderOff)
    {
        _inheritedOff = off;
        _inheritedExcluded = excluded;
        _inheritedRenderOff = renderOff;
        Raise(nameof(RowOpacity));
        Raise(nameof(ToggleOpacity));
        Raise(nameof(RenderOpacity));
    }

    public Geometry EyeData => _visible ? EyeOpen : EyeClosed;

    public double RowOpacity => (IsCollection ? _included && !_inheritedExcluded : _visible && !_inheritedOff) ? 1.0 : 0.4;

    public double ToggleOpacity => _inheritedExcluded ? 0.25 : _inheritedOff ? 0.5 : _visible ? 1.0 : 0.55;

    public double RenderOpacity => _inheritedRenderOff ? 0.25 : _renderable ? 1.0 : 0.4;

    public string EyeTip => IsCollection ? UiText.TipCollectionHide : UiText.ToggleVisibility;

    public string RenderTip => UiText.TipDisableRender;

    public string ExcludeTip => UiText.TipExcludeCollection;

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
        Raise(nameof(EyeData));
        Raise(nameof(RowOpacity));
        Raise(nameof(ToggleOpacity));
    }

    void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    static Geometry G(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}