namespace PolyView;

public static partial class UiText
{
    public static string PrefTitle => Chinese ? "Blender 偏好设置" : "Blender Preferences";
    public static string PrefSearch => Chinese ? "搜索" : "Search";

    /// <summary>Category keys in display order; null marks a group gap.</summary>
    public static readonly string?[] PrefCategoryKeys =
    [
        "Interface", "Viewport", "Lights", "Editing", "Animation", null,
        "Extensions", null,
        "Addons", "Themes", null,
        "AssetLibs", null,
        "Input", "Navigation", "Keymap", null,
        "System", "SaveLoad", "FilePaths",
    ];

    public static string PrefCategory(string key) => key switch
    {
        "Interface" => Chinese ? "界面" : "Interface",
        "Viewport" => Chinese ? "视图" : "Viewport",
        "Lights" => Chinese ? "灯光" : "Lights",
        "Editing" => Chinese ? "编辑" : "Editing",
        "Animation" => Chinese ? "动画" : "Animation",
        "Extensions" => Chinese ? "获取扩展" : "Get Extensions",
        "Addons" => Chinese ? "插件" : "Add-ons",
        "Themes" => Chinese ? "主题" : "Themes",
        "AssetLibs" => Chinese ? "资产库" : "Asset Libraries",
        "Input" => Chinese ? "输入" : "Input",
        "Navigation" => Chinese ? "视图导航" : "Navigation",
        "Keymap" => Chinese ? "键位映射" : "Keymap",
        "System" => Chinese ? "系统" : "System",
        "SaveLoad" => Chinese ? "保存&加载" : "Save & Load",
        "FilePaths" => Chinese ? "文件路径" : "File Paths",
        _ => key,
    };

    public static string T(string zh, string en) => Chinese ? zh : en;

    /// <summary>Placeholder section titles per category.</summary>
    public static string[] PrefSections(string key) => key switch
    {
        "Interface" => Chinese ? ["显示", "编辑器", "语言", "无障碍", "文本渲染", "菜单"] : ["Display", "Editors", "Language", "Accessibility", "Text Rendering", "Menus"],
        "Viewport" => Chinese ? ["显示", "质量", "纹理", "选择"] : ["Display", "Quality", "Textures", "Selection"],
        "Lights" => Chinese ? ["工作室灯光", "材质预览 HDRI"] : ["Studio Lights", "Material Preview HDRI"],
        "Editing" => Chinese ? ["物体", "3D 游标", "注记", "杂项"] : ["Objects", "3D Cursor", "Annotations", "Miscellaneous"],
        "Animation" => Chinese ? ["时间轴", "关键帧", "F 曲线"] : ["Timeline", "Keyframes", "F-Curves"],
        "Addons" => Chinese ? ["已安装插件"] : ["Installed Add-ons"],
        "Themes" => Chinese ? ["用户界面", "3D 视图", "大纲视图"] : ["User Interface", "3D Viewport", "Outliner"],
        "Input" => Chinese ? ["键盘", "鼠标", "触控板"] : ["Keyboard", "Mouse", "Touchpad"],
        "Navigation" => Chinese ? ["环绕 & 平移", "缩放", "飞行 & 漫游"] : ["Orbit & Pan", "Zoom", "Fly & Walk"],
        "Keymap" => Chinese ? ["预设", "首选项"] : ["Preset", "Preferences"],
        "Extensions" => Chinese ? ["在线访问", "仓库"] : ["Online Access", "Repositories"],
        "AssetLibs" => Chinese ? ["资产库"] : ["Asset Libraries"],
        "System" => Chinese ? ["Direct3D 后端", "内存 & 限制", "声音"] : ["Direct3D Backend", "Memory & Limits", "Sound"],
        "SaveLoad" => Chinese ? ["保存", "工程文件", "自动保存"] : ["Saving", "Project Files", "Auto Save"],
        "FilePaths" => Chinese ? ["数据", "渲染", "应用程序"] : ["Data", "Render", "Applications"],
        _ => [],
    };

    public static string PrefLanguage => Chinese ? "语言" : "Language";
    public static string PrefLanguageHint => Chinese ? "切换后立即生效并保存" : "Applied immediately and saved";
    public static string PrefDefaultSaveFolder => Chinese ? "默认工程保存文件夹" : "Default Project Folder";
    public static string PrefPlaceholder => Chinese ? "（尚未实现）" : "(Not implemented yet)";
}
