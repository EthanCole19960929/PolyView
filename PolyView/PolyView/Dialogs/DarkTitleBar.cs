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

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateSolidBrush(int color);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    static extern IntPtr SetClassLongPtr(IntPtr hwnd, int index, IntPtr value);

    const int GCLP_HBRBACKGROUND = -10;
    static IntPtr _darkBrush;

    static void Set(Window w)
    {
        try
        {
            var h = new WindowInteropHelper(w).Handle;
            // No white flash while resizing: dark Win32 class brush and dark WPF composition background.
            if (_darkBrush == IntPtr.Zero) _darkBrush = CreateSolidBrush(0x1D1D1D);
            SetClassLongPtr(h, GCLP_HBRBACKGROUND, _darkBrush);
            if (HwndSource.FromHwnd(h)?.CompositionTarget is { } target)
                target.BackgroundColor = System.Windows.Media.Color.FromRgb(0x1D, 0x1D, 0x1D);
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
