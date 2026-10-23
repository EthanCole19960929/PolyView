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
        ? "把 OBJ 或 STL 拖进窗口，或点击「打开」。左键旋转，滚轮缩放，中键平移。"
        : "Drop an OBJ or STL file here, or click Open. Left-drag orbits, wheel zooms, middle-drag pans.";

    public static string Reading => Chinese ? "正在读取…" : "Reading…";

    public static string DropRejected => Chinese
        ? "请拖入 .obj 或 .stl 文件。"
        : "Drop a .obj or .stl file.";

    public static string Ready(string name, int vertices, int triangles, double frameMs) => Chinese
        ? $"{name}    顶点 {vertices}    三角形 {triangles}    {frameMs:0.0} 毫秒"
        : $"{name}    vertices {vertices}    triangles {triangles}    {frameMs:0.0} ms";

    public static string SampleReady(int vertices, int triangles, double frameMs) => Chinese
        ? $"示例长方体    顶点 {vertices}    三角形 {triangles}    {frameMs:0.0} 毫秒"
        : $"Sample bar    vertices {vertices}    triangles {triangles}    {frameMs:0.0} ms";

    public static string FileFilter => Chinese
        ? "模型文件|*.obj;*.stl|OBJ|*.obj|STL|*.stl"
        : "Model files|*.obj;*.stl|OBJ|*.obj|STL|*.stl";
}
