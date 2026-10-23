namespace PolyView;

/// <summary>Strings for project files (.pvproj). Saving a project is 保存 / 另存为, never 导出.</summary>
public static partial class UiText
{
    public static string MenuSaveAs => Chinese ? "另存为..." : "Save As...";

    public static string ProjectFilter => Chinese
        ? "PolyView 工程 (*.pvproj)|*.pvproj"
        : "PolyView project (*.pvproj)|*.pvproj";

    public static string OpenAnyFilter => Chinese
        ? "PolyView 工程和模型|*.pvproj;*.obj;*.stl;*.glb;*.gltf;*.fbx;*.dae;*.3ds;*.ply;*.json|"
        : "PolyView projects and models|*.pvproj;*.obj;*.stl;*.glb;*.gltf;*.fbx;*.dae;*.3ds;*.ply;*.json|";

    public static string Untitled => Chinese ? "未命名" : "Untitled";

    public static string ProjectSaved(string path) => Chinese ? $"已保存：{path}" : $"Saved: {path}";

    public static string ProjectSaveFailed(string error) => Chinese ? $"保存失败：{error}" : $"Save failed: {error}";

    public static string ProjectOpening => Chinese ? "正在打开工程…" : "Opening project…";

    public static string ProjectOpened(string name) => Chinese ? $"已打开工程：{name}" : $"Opened project: {name}";

    public static string ProjectOpenFailed(string error) => Chinese ? $"打开工程失败：{error}" : $"Could not open the project: {error}";

    public static string ProjectMissing(string list) => Chinese
        ? $"这些模型文件找不到，没能复原：\n{list}"
        : $"These model files could not be found and were not restored:\n{list}";

    public static string ProjectTooNew => Chinese
        ? "这个工程是用更新版本的 PolyView 保存的，请先升级 PolyView。"
        : "This project was saved by a newer PolyView. Please update PolyView.";

    public static string AskSaveChanges(string name) => Chinese
        ? $"“{name}”有未保存的修改，要保存吗？"
        : $"Save changes to \"{name}\" before closing?";
}
