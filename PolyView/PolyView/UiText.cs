namespace PolyView;

public static class UiText
{
    public static bool Chinese { get; set; } = true;

    public static string WindowTitle => Chinese ? "PolyView 三维查看" : "PolyView";

    public static string Open => Chinese ? "打开模型" : "Open";

    public static string Reset => Chinese ? "复位视角" : "Reset View";

    public static string Language => Chinese ? "English" : "中文";

    public static string Hint => Chinese
        ? "拖入或打开 OBJ / STL / GLB / glTF / FBX / DAE / three.js JSON。中键旋转（无中键时 Alt+左键），Shift+中键平移（或 Alt+Shift+左键），滚轮或 Ctrl+中键缩放；Home / 小键盘 . 框选全部，小键盘 1 / 3 / 7 前 / 右 / 顶视图（加 Ctrl 看反面），小键盘 5 透视 / 正交。"
        : "Drag in or open an OBJ, STL, GLB, glTF, FBX, DAE or three.js JSON file. Middle-drag (or Alt+left-drag) to orbit, Shift+middle-drag (or Alt+Shift+left-drag) to pan, and scroll or Ctrl+middle-drag to zoom. Press Home or Numpad . to frame everything, Numpad 1 / 3 / 7 for the front, right and top views (add Ctrl for the opposite side), and Numpad 5 to switch between perspective and orthographic.";

    public static string Reading => Chinese ? "正在读取…" : "Reading…";

    public static string DropRejected => Chinese
        ? "不支持这个文件，请拖入 .obj、.stl、.glb、.gltf、.fbx、.dae 或 three.js .json 模型。"
        : "This file type isn't supported. Use a .obj, .stl, .glb, .gltf, .fbx, .dae or three.js .json model.";

    public static string Ortho => Chinese ? "正交" : "Orthographic";

    public static string Perspective => Chinese ? "透视" : "Perspective";

    public static string Ready(string name, int vertices, int triangles) => Chinese
        ? $"{name}    顶点 {vertices:N0}    三角形 {triangles:N0}"
        : $"{name}    Vertices: {vertices:N0}    Triangles: {triangles:N0}";

    public static string FileFilter => Chinese
        ? "模型文件|*.obj;*.stl;*.glb;*.gltf;*.fbx;*.dae;*.3ds;*.ply;*.json|GLB / glTF|*.glb;*.gltf|FBX|*.fbx|COLLADA (DAE)|*.dae|OBJ|*.obj|STL|*.stl|three.js JSON|*.json|3DS / PLY|*.3ds;*.ply|所有文件|*.*"
        : "Model files|*.obj;*.stl;*.glb;*.gltf;*.fbx;*.dae;*.3ds;*.ply;*.json|GLB / glTF|*.glb;*.gltf|FBX|*.fbx|COLLADA (DAE)|*.dae|OBJ|*.obj|STL|*.stl|three.js JSON|*.json|3DS / PLY|*.3ds;*.ply|All files|*.*";

    public static string ToolSelect => Chinese ? "选择" : "Select";

    public static string ToolMove => Chinese ? "移动 (G)" : "Move (G)";

    public static string ToolRotate => Chinese ? "旋转 (R)" : "Rotate (R)";

    public static string ToolScale => Chinese ? "缩放 (S)" : "Scale (S)";

    public static string HintMove => Chinese
        ? "拖动箭头沿该轴移动；按 X / Y / Z 锁定轴，右键或 Esc 取消。"
        : "Drag an arrow to move along that axis. Press X, Y or Z to lock an axis; right-click or Esc to cancel.";

    public static string HintRotate => Chinese
        ? "拖动圆环绕该轴旋转；按 X / Y / Z 锁定轴，右键或 Esc 取消。"
        : "Drag a ring to rotate around that axis. Press X, Y or Z to lock an axis; right-click or Esc to cancel.";

    public static string HintScale => Chinese
        ? "拖动轴端方块沿该轴缩放，拖中心等比缩放；右键或 Esc 取消。"
        : "Drag a box handle to scale along that axis, or drag the center to scale evenly. Right-click or Esc to cancel.";

    // Scene outliner
    public static string OutlinerTitle => Chinese ? "场景列表" : "Outliner";
    public static string OutlinerScene => Chinese ? "场景" : "Scene";
    public static string KindMesh => Chinese ? "模型" : "Mesh";
    public static string KindCamera => Chinese ? "相机" : "Camera";
    public static string KindLight => Chinese ? "灯光" : "Light";
    public static string KindEmpty => Chinese ? "空节点" : "Empty";
    public static string PropTitle => Chinese ? "属性" : "Properties";
    public static string PropVertices => Chinese ? "顶点" : "Vertices";
    public static string PropTriangles => Chinese ? "三角形" : "Triangles";
    public static string PropSize => Chinese ? "尺寸" : "Size";
    public static string ToggleVisibility => Chinese ? "显示/隐藏" : "Show/Hide";
    public static string LightPoint => Chinese ? "点光" : "Point";
    public static string LightSun => Chinese ? "日光" : "Sun";
    public static string LightSpot => Chinese ? "聚光" : "Spot";
    public static string LightArea => Chinese ? "面光" : "Area";
    public static string LightAmbient => Chinese ? "环境光" : "Ambient";
    public static string KindWorld => Chinese ? "世界" : "World";
    public static string OutlinerSearch => Chinese ? "搜索" : "Search";
    public static string TransformTitle => Chinese ? "变换" : "Transform";
    public static string TransformLocation => Chinese ? "位置" : "Location";
    public static string TransformRotation => Chinese ? "旋转" : "Rotation";
    public static string TransformScale => Chinese ? "缩放" : "Scale";}
