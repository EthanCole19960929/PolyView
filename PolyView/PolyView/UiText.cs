namespace PolyView;

public static partial class UiText
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

    // Scene outliner
    public static string OutlinerTitle => Chinese ? "场景列表" : "Outliner";
    public static string OutlinerScene => Chinese ? "场景" : "Scene";
    public static string KindMesh => Chinese ? "模型" : "Mesh";
    public static string KindCamera => Chinese ? "相机" : "Camera";
    public static string KindLight => Chinese ? "灯光" : "Light";
    public static string KindEmpty => Chinese ? "空节点" : "Empty";
    public static string PropTitle => Chinese ? "属性" : "Properties";
    public static string PropVertices => Chinese ? "顶点" : "Vertices";
    public static string PropTriangles => Chinese ? "三角形" : "Triangles";
    public static string PropSize => Chinese ? "尺寸" : "Size";
    public static string ToggleVisibility => Chinese ? "显示/隐藏" : "Show/Hide";
    public static string LightPoint => Chinese ? "点光" : "Point";
    public static string LightSun => Chinese ? "日光" : "Sun";
    public static string LightSpot => Chinese ? "聚光" : "Spot";
    public static string LightArea => Chinese ? "面光" : "Area";
    public static string LightAmbient => Chinese ? "环境光" : "Ambient";
    public static string KindWorld => Chinese ? "世界" : "World";
    public static string TipExcludeCollection => Chinese ? "\u4ece\u89c6\u56fe\u5c42\u4e2d\u6392\u9664\n\u53d6\u6d88\u52fe\u9009\u540e\uff0c\u96c6\u5408\u53ca\u5176\u4e2d\u6240\u6709\u7269\u4f53\u5728\u89c6\u53e3\u4e2d\u9690\u85cf\uff0c\u4e5f\u4e0d\u80fd\u88ab\u9009\u4e2d\u3002" : "Exclude from View Layer\nUnchecked: the collection and all its objects are hidden in the viewport and cannot be selected.";
    public static string TipCollectionHide => Chinese ? "\u5728\u89c6\u56fe\u4e2d\u9690\u85cf\n\u9690\u85cf\u96c6\u5408\u5185\u7684\u6240\u6709\u7269\u4f53\u3002" : "Hide in Viewport\nHides every object in the collection.";
    public static string TipDisableRender => Chinese ? "\u7981\u7528\u6e32\u67d3\n\u5173\u95ed\u540e\u8be5\u9879\uff08\u53ca\u96c6\u5408\u5185\u7269\u4f53\uff09\u4e0d\u53c2\u4e0e\u6e32\u67d3\u3002" : "Disable in Renders\nWhen off, this item (and objects in the collection) are left out of renders.";
    public static string OutlinerSearch => Chinese ? "搜索" : "Search";
    public static string TransformTitle => Chinese ? "变换" : "Transform";
    public static string TransformLocation => Chinese ? "位置" : "Location";
    public static string TransformRotation => Chinese ? "旋转" : "Rotation";
    public static string TransformScale => Chinese ? "缩放" : "Scale";
    public static string TipSelectName => Chinese ? "选择" : "Select";
    public static string ShadeWireName => Chinese ? "\u7ebf\u6846" : "Wireframe";
    public static string ShadeWire => Chinese ? "\u5207\u6362\u5230\u7ebf\u6846\u663e\u793a\u3002" : "Display only the edges of objects.";
    public static string ShadeSolidName => Chinese ? "\u5b9e\u4f53" : "Solid";
    public static string ShadeSolid => Chinese ? "\u7528\u5de5\u4f5c\u5ba4\u706f\u5149\u663e\u793a\u6a21\u578b\uff0c\u4e0d\u4f7f\u7528\u573a\u666f\u706f\u5149\u548c\u8d34\u56fe\u3002" : "Studio lighting, no scene lights or textures.";
    public static string ShadeMaterialName => Chinese ? "\u6750\u8d28\u9884\u89c8" : "Material Preview";
    public static string ShadeMaterial => Chinese ? "\u663e\u793a\u57fa\u7840\u8272\u8d34\u56fe\uff0c\u4f7f\u7528\u9ed8\u8ba4\u73af\u5883\u5149\u7167\u3002" : "Base color textures with default environment lighting.";
    public static string ShadeRenderedName => Chinese ? "\u6e32\u67d3" : "Rendered";
    public static string ShadeRendered => Chinese ? "\u4f7f\u7528\u573a\u666f\u706f\u5149\u548c\u4e16\u754c\u73af\u5883\u5149\u663e\u793a\u3002" : "Scene lights and World ambient.";
    public static string MenuAdd => Chinese ? "\u6dfb\u52a0" : "Add";
    public static string TipMoveName => Chinese ? "移动" : "Move";
    public static string TipRotateName => Chinese ? "旋转" : "Rotate";
    public static string TipScaleName => Chinese ? "缩放" : "Scale";
    public static string TipSelect => Chinese ? "选择或移动。" : "Select or move.";
    public static string TipMove => Chinese ? "移动选中元素。" : "Move selected items.";
    public static string TipRotate => Chinese ? "旋转选中元素。" : "Rotate selected items.";
    public static string TipScale => Chinese ? "缩放选中元素。" : "Scale (resize) selected items.";
    public static string Shortcut => Chinese ? "快捷键" : "Shortcut";
    public static string AddTitle => Chinese ? "添加" : "Add";
    public static string AddMesh => Chinese ? "网格" : "Mesh";
    public static string AddCurve => Chinese ? "曲线" : "Curve";
    public static string AddEmpty => Chinese ? "空物体" : "Empty";
    public static string AddLight => Chinese ? "灯光" : "Light";
    public static string AddCamera => Chinese ? "摄像机" : "Camera";
    public static string MenuFile => Chinese ? "文件" : "File";
    public static string MenuEdit => Chinese ? "编辑" : "Edit";
    public static string MenuRender => Chinese ? "渲染" : "Render";
    public static string MenuWindow => Chinese ? "窗口" : "Window";
    public static string MenuHelp => Chinese ? "帮助" : "Help";
    public static string MenuNew => Chinese ? "新建" : "New";
    public static string MenuOpen => Chinese ? "打开..." : "Open...";
    public static string MenuRecent => Chinese ? "打开最近" : "Open Recent";
    public static string MenuSave => Chinese ? "保存" : "Save";
    public static string MenuImport => Chinese ? "导入" : "Import";
    public static string MenuExport => Chinese ? "导出" : "Export";
    public static string MenuQuit => Chinese ? "退出" : "Quit";
    public static string MenuUndo => Chinese ? "撤销" : "Undo";
    public static string MenuRedo => Chinese ? "重做" : "Redo";
    public static string MenuPreferences => Chinese ? "偏好设置..." : "Preferences...";
    public static string MenuLanguage => Chinese ? "界面语言: English" : "Language: 中文";
    public static string MenuRenderImage => Chinese ? "渲染图像" : "Render Image";
    public static string MenuRenderAnimation => Chinese ? "渲染动画" : "Render Animation";
    public static string MenuViewRender => Chinese ? "查看渲染" : "View Render";
    public static string MenuNewWindow => Chinese ? "新建窗口" : "New Window";
    public static string MenuFullscreen => Chinese ? "切换窗口全屏" : "Toggle Window Fullscreen";
    public static string MenuProjection => Chinese ? "切换透视 / 正交" : "Toggle Perspective / Orthographic";
    public static string MenuManual => Chinese ? "手册" : "Manual";
    public static string MenuAbout => Chinese ? "关于 PolyView" : "About PolyView";
    public static string AboutText => Chinese ? "PolyView 三维查看器\n仿照 Blender 的操作和界面。" : "PolyView 3D viewer\nControls and layout modeled on Blender.";
    public static string ViewPerspective => Chinese ? "用户透视" : "User Perspective";
    public static string ViewOrtho => Chinese ? "用户正交" : "User Orthographic";
    public static string[] Workspaces => Chinese
        ? ["布局", "建模", "雕刻", "UV 编辑", "纹理绘制", "着色", "动画", "渲染", "合成"]
        : ["Layout", "Modeling", "Sculpting", "UV Editing", "Texture Paint", "Shading", "Animation", "Rendering", "Compositing"];
}
