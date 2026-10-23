namespace PolyView;

/// <summary>Strings for File > Export.</summary>
public static partial class UiText
{
    public static string ExportFilter => Chinese
        ? "FBX (*.fbx)|*.fbx|Wavefront OBJ (*.obj)|*.obj|glTF 2.0 (*.gltf)|*.gltf|glTF 2.0 二进制 (*.glb)|*.glb|STL (*.stl)|*.stl|Stanford PLY (*.ply)|*.ply|Collada (*.dae)|*.dae"
        : "FBX (*.fbx)|*.fbx|Wavefront OBJ (*.obj)|*.obj|glTF 2.0 (*.gltf)|*.gltf|glTF 2.0 Binary (*.glb)|*.glb|STL (*.stl)|*.stl|Stanford PLY (*.ply)|*.ply|Collada (*.dae)|*.dae";

    public static string DlgExport => Chinese ? "导出" : "Export";

    public static string DlgExportSelection => Chinese ? "导出选中物体" : "Export Selected";

    public static string ExportNoPath => Chinese ? "没有指定导出路径。" : "No export path was given.";

    public static string ExportNothing => Chinese ? "没有可导出的物体（场景为空，或物体都已隐藏）。" : "Nothing to export (the scene is empty or everything is hidden).";

    public static string ExportUnknownFormat(string format) => Chinese
        ? $"不支持的导出格式：{format}。支持 FBX、OBJ、glTF、GLB、STL、PLY、DAE。"
        : $"Unsupported export format: {format}. Supported: FBX, OBJ, glTF, GLB, STL, PLY, DAE.";

    public static string ExportAssimpFailed(string reason) => Chinese ? $"导出失败：{reason}" : $"Export failed: {reason}";

    public static string ExportFailedTitle => Chinese ? "导出失败" : "Export Failed";

    public static string ExportDone(string path, int objects, int triangles) => Chinese
        ? $"已导出 {objects} 个物体（{triangles:N0} 个三角面）：{path}"
        : $"Exported {objects} object(s) ({triangles:N0} triangles): {path}";
}
