using System;
using System.Runtime.InteropServices;

namespace ZasLauncherGUI.Utility;

public static class KeyboardUtils
{
    public static bool IsOnlyShiftPressed()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var shift = IsWindowsKeyDown(0x10); // VK_SHIFT
            var ctrl = IsWindowsKeyDown(0x11);  // VK_CONTROL

            return shift && !ctrl;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            const ulong ShiftMask = 1UL << 17;
            const ulong ControlMask = 1UL << 18;

            var flags = CGEventSourceFlagsState(1);

            var shift = (flags & ShiftMask) != 0;
            var ctrl = (flags & ControlMask) != 0;

            return shift && !ctrl;
        }

        return false;
    }

    // ================= WINDOWS =================

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int keyCode);

    private static bool IsWindowsKeyDown(int keyCode)
    {
        return (GetKeyState(keyCode) & 0x8000) != 0;
    }

    // ================= MACOS =================

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern ulong CGEventSourceFlagsState(uint stateID);
}