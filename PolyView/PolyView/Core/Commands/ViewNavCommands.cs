using System.ComponentModel;

namespace PolyView.Commands;

/// <summary>View navigation commands (viewport nav buttons).</summary>
static class ViewNavCommands
{
    [PvCommand("view.toggle_ortho", "view", "切换正交/透视（小键盘 5）", "Toggle orthographic / perspective projection (Numpad 5)")]
    static object ToggleOrtho(MainWindow w)
    {
        w.Viewport.ToggleOrtho();
        return new { ok = true, orthographic = w.Viewport.Orthographic };
    }

    [PvCommand("view.camera", "view", "摄像机视角（小键盘 0）", "Toggle looking through the active camera (selected camera, else the first). Fails when the scene has no camera.")]
    static object Camera(MainWindow w)
    {
        bool ok = w.Viewport.ToggleCameraView();
        return ok ? new { ok = true, camera_view = w.Viewport.InCameraView } : (object)new { ok = false, error = "no camera in the scene" };
    }
}
