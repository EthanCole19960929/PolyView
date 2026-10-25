namespace PolyView.Commands;

static class AnimationCommands
{
    [PvCommand("animation.play", "animation", "播放动画", "Play the selected animation or an animation by its displayed name; loads rig and clip in the background")]
    static object Play(MainWindow window, string? name = null) => window.Animations.StartPlayback(name);

    [PvCommand("animation.pause", "animation", "暂停动画", "Pause animation playback at the current pose")]
    static object Pause(MainWindow window) { window.Viewport.PauseAnimation(); return window.Animations.Describe(); }

    [PvCommand("animation.stop", "animation", "停止动画", "Stop animation and restore the original model")]
    static object Stop(MainWindow window) { window.Viewport.StopAnimation(); return window.Animations.Describe(); }

    [PvCommand("animation.seek", "animation", "跳转动画时间", "Seek to time_seconds in the active animation")]
    static object Seek(MainWindow window, double time_seconds) { window.Viewport.SeekAnimation(time_seconds); return window.Animations.Describe(); }

    [PvCommand("animation.set_loop", "animation", "动画循环", "Enable or disable animation looping")]
    static object Loop(MainWindow window, bool enabled) { window.Viewport.SetAnimationLoop(enabled); return window.Animations.Describe(); }

    [PvCommand("animation.set_speed", "animation", "动画速度", "Set playback speed from 0.1 to 4")]
    static object Speed(MainWindow window, double speed) { window.Viewport.SetAnimationSpeed(speed); return window.Animations.Describe(); }

    [PvCommand("animation.list_clips", "animation", "列出模型目录中的动画", "List discovered animation clips for the active model, including recursive subfolders; scanning=true means discovery is still running")]
    static object List(MainWindow window)
    {
        window.Animations.SetModel(window.Viewport.GetAnimationSource());
        return window.Animations.Describe();
    }

    [PvCommand("animation.refresh", "animation", "刷新动画列表", "Rescan the active model folder and all subfolders in the background; use animation.list_clips to read the results")]
    static object Refresh(MainWindow window)
    {
        var model = window.Viewport.GetAnimationSource();
        if (window.Animations.Model != model) window.Animations.SetModel(model);
        else _ = window.Animations.RefreshAsync();
        return window.Animations.Describe();
    }
}
