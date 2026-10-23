using System.ComponentModel;
using System.Windows;

namespace PolyView.Commands;

/// <summary>Selection commands.</summary>
static class SelectCommands
{
    [PvCommand("select.box", "select", "框选（B / 左键拖动空白处）", "Box select in the 3D viewport. Rect in viewport pixels (DIP, origin top-left). mode: replace | extend | subtract. An object is hit when its origin or any vertex projects inside the rect.")]
    static object Box(MainWindow w,
        [Description("Left edge (px)")] float x,
        [Description("Top edge (px)")] float y,
        [Description("Width (px)")] float width,
        [Description("Height (px)")] float height,
        [Description("replace | extend | subtract")] string mode = "replace")
    {
        var m = BoxSelection.ParseMode(mode);
        var selected = w.Viewport.BoxSelect(new Rect(x, y, Math.Max(0, width), Math.Max(0, height)), m);
        return new { ok = true, selected, active = w.Viewport.SelectedId, viewport = new[] { w.Viewport.ActualWidth, w.Viewport.ActualHeight } };
    }
}
