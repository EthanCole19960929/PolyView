namespace PolyView;

public static class UiText
{
    public static bool Chinese { get; set; } = true;

    public static string WindowTitle => Chinese ? "PolyView 三维查看" : "PolyView";

    public static string Open => Chinese ? "打开模型" : "Open";

    public static string Reset => Chinese ? "复位视角" : "Reset View";

    public static string Language => Chinese ? "English" : "中文";

    public static string Scale => Chinese ? "缩放" : "Scale";

    public static string Hint => Chinese
        ? "拖入或打开 OBJ / STL / GLB / glTF / FBX / DAE / three.js JSON。中键旋转（或左键），Shift+中键平移，滚轮或 Ctrl+中键缩放；Home / 小键盘 . 框选全部，小键盘 1 / 3 / 7 前 / 右 / 顶视图（加 Ctrl 看反面），小键盘 5 透视 / 正交。"
        : "Drop or open OBJ / STL / GLB / glTF / FBX / DAE / three.js JSON. Middle-drag (or left-drag) orbits, Shift+middle pans, wheel or Ctrl+middle zooms; Home / numpad . frames all, numpad 1 / 3 / 7 front / right / top (Ctrl for opposite), numpad 5 toggles perspective / ortho.";

    public static string Reading => Chinese ? "正在读取…" : "Reading…";

    public static string DropRejected => Chinese
        ? "不支持这个文件，请拖入 .obj、.stl、.glb、.gltf、.fbx、.dae 或 three.js .json 模型。"
        : "Unsupported file. Drop a .obj, .stl, .glb, .gltf, .fbx, .dae or three.js .json model.";

    public static string Idle => Chinese ? "空闲" : "idle";

    public static string Ortho => Chinese ? "正交" : "ortho";

    public static string Perspective => Chinese ? "透视" : "persp";

    public static string Ready(string name, int vertices, int triangles) => Chinese
        ? $"{name}    顶点 {vertices:N0}    三角形 {triangles:N0}"
        : $"{name}    vertices {vertices:N0}    triangles {triangles:N0}";

    public static string Fps(double fps, double frameMs) => fps <= 0
        ? Idle
        : Chinese ? $"帧率 {fps:0} FPS（{frameMs:0.0} 毫秒）" : $"{fps:0} FPS ({frameMs:0.0} ms)";

    public static string FileFilter => Chinese
        ? "模型文件|*.obj;*.stl;*.glb;*.gltf;*.fbx;*.dae;*.3ds;*.ply;*.json|GLB / glTF|*.glb;*.gltf|FBX|*.fbx|COLLADA (DAE)|*.dae|OBJ|*.obj|STL|*.stl|three.js JSON|*.json|3DS / PLY|*.3ds;*.ply|所有文件|*.*"
        : "Model files|*.obj;*.stl;*.glb;*.gltf;*.fbx;*.dae;*.3ds;*.ply;*.json|GLB / glTF|*.glb;*.gltf|FBX|*.fbx|COLLADA (DAE)|*.dae|OBJ|*.obj|STL|*.stl|three.js JSON|*.json|3DS / PLY|*.3ds;*.ply|All files|*.*";
}
