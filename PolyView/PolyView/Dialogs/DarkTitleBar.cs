using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PolyView;

/// <summary>Makes the native Windows title bar dark (#1D1D1D, text #CFCFCF) via DWM. Keeps snap/resize/Aero.</summary>
public static class DarkTitleBar
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19,
        DWMWA_BORDER_COLOR = 34, DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36;

    public static void Apply(Window w)
    {
        if (new WindowInteropHelper(w).Handle != IntPtr.Zero) Set(w);
        else w.SourceInitialized += (_, _) => Set(w);
    }

    static void Set(Window w)
    {
        try
        {
            var h = new WindowInteropHelper(w).Handle;
            int on = 1;
            if (DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, 4) != 0)
                DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, 4);
            int caption = 0x1D1D1D, text = 0xCFCFCF, border = 0x3A3A3A; // COLORREF 0x00BBGGRR (greys are symmetric)
            DwmSetWindowAttribute(h, DWMWA_CAPTION_COLOR, ref caption, 4);
            DwmSetWindowAttribute(h, DWMWA_TEXT_COLOR, ref text, 4);
            DwmSetWindowAttribute(h, DWMWA_BORDER_COLOR, ref border, 4);
        }
        catch { /* older Windows: keep default title bar */ }
    }
}
