namespace PolyView;

/// <summary>Strings for the Blender 4.2 Add menu (Shift+A) and the 3D viewport header.</summary>
public static partial class UiText
{
    static string AZ(string zh, string en) => Chinese ? zh : en;

    public static string AddSearch => AZ("搜索…", "Search...");
    public static string AddSearchHint => AZ("输入以搜索…", "Type to search...");
    public static string AddSurface => AZ("表(曲)面", "Surface");
    public static string AddMetaball => AZ("融球", "Metaball");
    public static string AddText => AZ("文本", "Text");
    public static string AddPointCloud => AZ("点云", "Point Cloud");
    public static string AddVolume => AZ("体积", "Volume");
    public static string AddGreasePencil => AZ("蜡笔", "Grease Pencil");
    public static string AddArmature => AZ("骨架", "Armature");
    public static string AddLattice => AZ("晶格", "Lattice");
    public static string AddImage => AZ("图像", "Image");
    public static string AddLightProbe => AZ("光照探头", "Light Probe");
    public static string AddSpeaker => AZ("扬声器", "Speaker");
    public static string AddForceField => AZ("力场", "Force Field");
    public static string AddCollectionInstance => AZ("集合实例", "Collection Instance");

    /// <summary>Menu label / default object name for a primitive kind (Blender zh_HANS translation).</summary>
    public static string PrimitiveName(string kind) => kind switch
    {
        "plane" => AZ("平面", "Plane"),
        "cube" => AZ("立方体", "Cube"),
        "circle" => AZ("圆环", "Circle"),
        "uvsphere" => AZ("经纬球", "UV Sphere"),
        "icosphere" => AZ("棱角球", "Ico Sphere"),
        "cylinder" => AZ("柱体", "Cylinder"),
        "cone" => AZ("锥体", "Cone"),
        "torus" => AZ("环体", "Torus"),
        "grid" => AZ("栅格", "Grid"),
        "monkey" => AZ("猴头", "Monkey"),
        "text" => AZ("文本", "Text"),
        "pointcloud" => AZ("点云", "PointCloud"),
        "camera" => AZ("摄像机", "Camera"),
        "curve:bezier" => AZ("贝塞尔曲线", "Bezier"),
        "curve:circle" => AZ("圆环", "Circle"),
        "curve:nurbs" => AZ("NURBS 曲线", "Nurbs Curve"),
        "curve:nurbscircle" => AZ("NURBS 圆环", "Nurbs Circle"),
        "curve:path" => AZ("路径", "Path"),
        "empty:plain_axes" => AZ("纯轴", "Plain Axes"),
        "empty:arrows" => AZ("箭头", "Arrows"),
        "empty:single_arrow" => AZ("单箭头", "Single Arrow"),
        "empty:circle" => AZ("圆环", "Circle"),
        "empty:cube" => AZ("立方体", "Cube"),
        "empty:sphere" => AZ("球体", "Sphere"),
        "empty:cone" => AZ("锥体", "Cone"),
        "empty:image" => AZ("图像", "Image"),
        _ => kind,
    };

    /// <summary>Blender's default data name of the new object (what the outliner shows).</summary>
    public static string PrimitiveObjectName(string kind) => kind switch
    {
        "curve:bezier" => AZ("贝塞尔曲线", "BezierCurve"),
        "curve:circle" => AZ("贝塞尔圆", "BezierCircle"),
        "curve:nurbs" => AZ("NURBS 曲线", "NurbsCurve"),
        "curve:nurbscircle" => AZ("NURBS 圆环", "NurbsCircle"),
        "curve:path" => AZ("NURBS 路径", "NurbsPath"),
        _ when kind.StartsWith("empty:") => AZ("空物体", "Empty"),
        _ => PrimitiveName(kind),
    };

    // 3D viewport header
    public static string HdrEditorType => AZ("编辑器类型", "Editor Type");
    public static string Hdr3DViewport => AZ("3D 视图", "3D Viewport");
    public static string HdrImageEditor => AZ("图像编辑器", "Image Editor");
    public static string HdrShaderEditor => AZ("着色器编辑器", "Shader Editor");
    public static string HdrTimeline => AZ("时间线", "Timeline");
    public static string HdrOutliner => AZ("大纲视图", "Outliner");
    public static string HdrProperties => AZ("属性", "Properties");
    public static string HdrObjectMode => AZ("物体模式", "Object Mode");
    public static string HdrEditMode => AZ("编辑模式", "Edit Mode");
    public static string HdrSculptMode => AZ("雕刻模式", "Sculpt Mode");
    public static string HdrVertexPaint => AZ("顶点绘制", "Vertex Paint");
    public static string HdrWeightPaint => AZ("权重绘制", "Weight Paint");
    public static string HdrTexturePaint => AZ("纹理绘制", "Texture Paint");
    public static string HdrView => AZ("视图", "View");
    public static string HdrSelect => AZ("选择", "Select");
    public static string HdrObject => AZ("物体", "Object");
    public static string HdrToolbar => AZ("工具栏", "Toolbar");
    public static string HdrSidebar => AZ("侧栏", "Sidebar");
    public static string HdrFrameSelected => AZ("框显所选", "Frame Selected");
    public static string HdrFrameAll => AZ("框显全部", "Frame All");
    public static string HdrPerspOrtho => AZ("透视/正交", "Perspective/Orthographic");
    public static string HdrViewpoint => AZ("视图", "Viewpoint");
    public static string HdrViewFront => AZ("前视图", "Front");
    public static string HdrViewBack => AZ("后视图", "Back");
    public static string HdrViewRight => AZ("右视图", "Right");
    public static string HdrViewLeft => AZ("左视图", "Left");
    public static string HdrViewTop => AZ("顶视图", "Top");
    public static string HdrViewBottom => AZ("底视图", "Bottom");
    public static string HdrCameraView => AZ("摄像机", "Camera");
    public static string HdrSelectAll => AZ("全选", "All");
    public static string HdrSelectNone => AZ("无", "None");
    public static string HdrSelectInvert => AZ("反选", "Invert");
    public static string HdrSelectBox => AZ("框选", "Box Select");
    public static string HdrTransform => AZ("变换", "Transform");
    public static string HdrMove => AZ("移动", "Move");
    public static string HdrRotate => AZ("旋转", "Rotate");
    public static string HdrScale => AZ("缩放", "Scale");
    public static string HdrDuplicate => AZ("复制物体", "Duplicate Objects");
    public static string HdrDelete => AZ("删除", "Delete");
    public static string HdrShowHide => AZ("显示/隐藏", "Show/Hide");
    public static string HdrHideSelected => AZ("隐藏所选", "Hide Selected");
    public static string HdrShowHidden => AZ("显示隐藏物体", "Show Hidden");
    public static string HdrApply => AZ("应用", "Apply");
    public static string HdrParent => AZ("父级", "Parent");
    public static string HdrSnap => AZ("吸附", "Snap");
}
