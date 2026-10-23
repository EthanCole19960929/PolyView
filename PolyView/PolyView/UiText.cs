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
}