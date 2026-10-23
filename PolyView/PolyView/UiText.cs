namespace PolyView;

public static class UiText
{
    public static bool Chinese { get; set; } = true;

    public static string WindowTitle => Chinese ? "PolyView 三维查看" : "PolyView";

    public static string Open => Chinese ? "打开模型" : "Open";

    public static string Reset => Chinese ? "复位视角" : "Reset View";

    public static string Language => Chinese ? "English" : "中文";

    public static string Scale => Chinese ? "缩放" : "Scale";

    public static string Stretch => Chinese ? "X 拉伸" : "Stretch X";

    public static string Hint => Chinese
        ? "把 OBJ、STL 或 GLB/glTF 拖进窗口，或点击「打开模型」。左键旋转，滚轮缩放，中键平移。"
        : "Drop an OBJ, STL or GLB/glTF file here, or click Open. Left-drag orbits, wheel zooms, middle-drag pans.";

    public static string Reading => Chinese ? "正在读取…" : "Reading…";

    public static string DropRejected => Chinese
        ? "不支持这个文件，请拖入 .obj、.stl、.glb 或 .gltf 模型。"
        : "Unsupported file. Drop a .obj, .stl, .glb or .gltf model.";

    public static string Ready(string name, int vertices, int triangles, double frameMs) => Chinese
        ? $"{name}    顶点 {vertices}    三角形 {triangles}    {frameMs:0.0} 毫秒"
        : $"{name}    vertices {vertices}    triangles {triangles}    {frameMs:0.0} ms";

    public static string SampleReady(int vertices, int triangles, double frameMs) => Chinese
        ? $"示例长方体    顶点 {vertices}    三角形 {triangles}    {frameMs:0.0} 毫秒"
        : $"Sample bar    vertices {vertices}    triangles {triangles}    {frameMs:0.0} ms";

    public static string FileFilter => Chinese
        ? "模型文件|*.obj;*.stl;*.glb;*.gltf|OBJ|*.obj|STL|*.stl|GLB / glTF|*.glb;*.gltf|所有文件|*.*"
        : "Model files|*.obj;*.stl;*.glb;*.gltf|OBJ|*.obj|STL|*.stl|GLB / glTF|*.glb;*.gltf|All files|*.*";
}
