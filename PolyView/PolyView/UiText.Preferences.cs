namespace PolyView;

public static partial class UiText
{
    public static string PrefTitle => Chinese ? "PolyView 偏好设置" : "PolyView Preferences";
    public static string PrefSearch => Chinese ? "搜索" : "Search";

    public static readonly string?[] PrefCategoryKeys = ["Interface", "SaveLoad", "AI"];

    public static string PrefCategory(string key) => key switch
    {
        "Interface" => Chinese ? "界面" : "Interface",
        "SaveLoad" => Chinese ? "保存&加载" : "Save & Load",
        "AI" => "AI / MCP",
        _ => key,
    };

    public static string T(string zh, string en) => Chinese ? zh : en;

    public static string[] PrefSections(string key) => key switch
    {
        "Interface" => Chinese ? ["语言"] : ["Language"],
        "SaveLoad" => Chinese ? ["工程文件"] : ["Project Files"],
        "AI" => Chinese ? ["MCP 服务", "AI 客户端"] : ["MCP Server", "AI Clients"],
        _ => [],
    };

    public static string PrefLanguage => Chinese ? "语言" : "Language";
    public static string PrefLanguageHint => Chinese ? "切换后立即生效并保存" : "Applied immediately and saved";
    public static string PrefDefaultSaveFolder => Chinese ? "默认工程保存文件夹" : "Default Project Folder";
}
