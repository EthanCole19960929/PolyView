namespace PolyView;

public static partial class UiText
{
    public static string AnimationTitle => Chinese ? "动画列表" : "Animations";
    public static string AnimationPlay => Chinese ? "播放" : "Play";
    public static string AnimationPause => Chinese ? "暂停" : "Pause";
    public static string AnimationStop => Chinese ? "停止" : "Stop";
    public static string AnimationLoop => Chinese ? "循环" : "Loop";
    public static string AnimationSeek => Chinese ? "拖动以预览动画" : "Drag to seek";
    public static string AnimationLoading => Chinese ? "正在加载骨骼和动画…" : "Loading skeleton and animation…";
    public static string AnimationRefresh => Chinese ? "刷新" : "Refresh";
    public static string AnimationRefreshHint => Chinese ? "重新扫描模型所在文件夹及所有子文件夹" : "Rescan the model folder and all subfolders";
    public static string AnimationSearch => Chinese ? "搜索动画名称" : "Search animation names";
    public static string AnimationName => Chinese ? "动画名称" : "Animation";
    public static string AnimationDuration => Chinese ? "时长" : "Duration";
    public static string AnimationFile => Chinese ? "文件（相对路径）" : "File (relative path)";
    public static string AnimationScanning => Chinese ? "正在查找动画…" : "Finding animations…";
    public static string AnimationScanFailed => Chinese ? "动画扫描失败，详情见下方" : "Animation scan failed; see details below";
    public static string AnimationEmpty => Chinese ? "没有找到动画，或没有匹配搜索条件的动画" : "No animations found or matching the search";
    public static string AnimationProgress(int files) => Chinese ? $"正在扫描文件夹及子文件夹 · 已检查 {files} 个文件" : $"Scanning folder and subfolders · {files} files checked";
    public static string AnimationFound(int clips, int errors) => Chinese
        ? $"共 {clips} 个动画 · 打开模型或项目时自动刷新{(errors > 0 ? $" · {errors} 项无法读取（悬停查看）" : "")}"
        : $"{clips} animations · Refreshed when opening the model or project{(errors > 0 ? $" · {errors} read errors (hover for details)" : "")}";
}
