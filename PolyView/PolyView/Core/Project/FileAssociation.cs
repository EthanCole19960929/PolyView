using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PolyView.Project;

/// <summary>Per-user .pvproj association (HKCU\Software\Classes): PolyView icon in Explorer and double-click opens PolyView.</summary>
public static class FileAssociation
{
    const string ProgId = "PolyView.Project";

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    const int ShcneAssocChanged = 0x08000000;

    /// <summary>Writes the keys when they are missing or point to another exe, then refreshes Explorer icons.</summary>
    public static void EnsureRegistered()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string icon = $"\"{exe}\",0";
            string command = $"\"{exe}\" \"%1\"";
            bool changed = false;
            changed |= Set(@"Software\Classes\" + ProjectPackage.Extension, null, ProgId);
            changed |= Set(@"Software\Classes\" + ProgId, null, "PolyView Project");
            changed |= Set(@"Software\Classes\" + ProgId + @"\DefaultIcon", null, icon);
            changed |= Set(@"Software\Classes\" + ProgId + @"\shell\open\command", null, command);
            if (changed)
            {
                SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);
            }
        }
        catch (Exception ex)
        {
            App.LogException("FileAssociation", ex);
        }
    }

    static bool Set(string keyPath, string? name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath);
        if (key.GetValue(name) as string == value)
        {
            return false;
        }

        key.SetValue(name, value);
        return true;
    }
}
