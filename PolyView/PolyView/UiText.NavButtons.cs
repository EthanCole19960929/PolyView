namespace PolyView;

/// <summary>Strings for the viewport navigation buttons.</summary>
public static partial class UiText
{
    public static string NavZoom => Chinese ? "缩放视图（按住拖动）" : "Zoom in/out in the view (drag)";
    public static string NavPan => Chinese ? "移动视图（按住拖动）" : "Move the view (drag)";
    public static string NavCamera => Chinese ? "切换摄像机视角（小键盘 0）" : "Toggle the camera view (Numpad 0)";
    public static string NavOrtho => Chinese ? "切换正交/透视视图（小键盘 5）" : "Switch the current view between perspective and orthographic (Numpad 5)";
    public static string NavNoCamera => Chinese ? "场景中没有摄像机" : "No camera found in scene";
}
