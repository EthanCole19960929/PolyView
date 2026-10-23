namespace PolyView;

/// <summary>Strings for File > Import and the import options panel (Blender 4.2 wording).</summary>
public static partial class UiText
{
    public static string ImpPreset => Chinese ? "操作项预设" : "Operator Presets";
    public static string ImpPresetNone => Chinese ? "（默认）" : "(Default)";
    public static string ImpPresetAdd => Chinese ? "添加预设" : "Add Preset";
    public static string ImpPresetRemove => Chinese ? "删除预设" : "Remove Preset";
    public static string ImpPresetName => Chinese ? "预设名称" : "Preset name";
    public static string ImpGeneral => Chinese ? "常规" : "General";
    public static string ImpScale => Chinese ? "缩放" : "Scale";
    public static string ImpClamp => Chinese ? "钳制边界框" : "Clamp Bounding Box";
    public static string ImpForward => Chinese ? "前进轴" : "Forward Axis";
    public static string ImpUp => Chinese ? "向上轴" : "Up Axis";
    public static string ImpOptions => Chinese ? "选项" : "Options";
    public static string ImpSplitObject => Chinese ? "按物体拆分" : "Split by Object";
    public static string ImpSplitGroup => Chinese ? "按组拆分" : "Split by Group";
    public static string ImpVertexGroups => Chinese ? "顶点组" : "Vertex Groups";
    public static string ImpValidate => Chinese ? "验证网格" : "Validate Meshes";
    public static string ImpClosedCurves => Chinese ? "侦测闭合曲线" : "Detect Closed Curves";
    public static string ImpPathSeparator => Chinese ? "路径分隔符" : "Path Separator";
    public static string ImpMaterials => Chinese ? "材质" : "Materials";
    public static string ImpNameCollision => Chinese ? "名称冲突" : "Name Collision";
    public static string ImpMakeUnique => Chinese ? "使其独立化" : "Make Unique";
    public static string ImpReferenceExisting => Chinese ? "引用现有" : "Reference Existing";
    public static string ImpFbx => "FBX";
    public static string ImpFbxNormals => Chinese ? "自定义法向" : "Custom Normals";
    public static string ImpFbxAnimation => Chinese ? "动画" : "Animation";
    public static string ImpFbxApplyTransform => Chinese ? "应用变换" : "Apply Transform";
    public static string ImpFbxBoneOrientation => Chinese ? "自动骨骼坐标系" : "Automatic Bone Orientation";
    public static string ImpGltf => "glTF";
    public static string ImpGltfMerge => Chinese ? "合并顶点" : "Merge Vertices";
    public static string ImpGltfShading => Chinese ? "着色" : "Shading";
    public static string ImpGltfBindPose => Chinese ? "推测原始绑定姿态" : "Guess Original Bind Pose";
    public static string ImpGltfLights => Chinese ? "灯光" : "Lights";
    public static string ImpButton(string format) => Chinese ? $"导入 {format}" : $"Import {format}";
    public static string ImpCancel => Chinese ? "取消" : "Cancel";
    public static string ImpFilesTitle(int count) => Chinese ? $"导入 {count} 个文件" : $"Import {count} files";
    public static string ImportAxisConflict => Chinese ? "前进轴和向上轴不能在同一条轴上。" : "Forward and Up axes cannot be on the same axis.";
    public static string ImportFailedTitle => Chinese ? "导入失败" : "Import Failed";
    public static string ImportFailed(string list) => Chinese ? $"以下文件导入失败：\n{list}" : $"These files could not be imported:\n{list}";
    public static string ImportDone(int count) => Chinese ? $"已导入 {count} 个物体" : $"Imported {count} object(s)";
    public static string ImportBusy => Chinese ? "正在导入…" : "Importing…";
}
