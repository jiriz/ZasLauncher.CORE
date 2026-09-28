// Also compiled by Windows PowerShell 5.1 in the interactive Parallels guest.
// Keep this source compatible with C# 5 and independent of Avalonia.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ZasLauncherGUI.Utility
{
    public static class KaratWindowLayoutNative
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
        private delegate bool EnumWindow(IntPtr window, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr data);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        // Physical monitor coordinates, not coordinates of the whole multi-monitor desktop.
        public static bool TrySpecialBounds(Rect monitor, Rect work, out Rect bounds)
        {
            bounds = work;
            int width = monitor.Right - monitor.Left, height = monitor.Bottom - monitor.Top;
            if (width != 5120 || height <= 0 || width <= 2L * height) return false;
            bounds.Left = Math.Max(work.Left, monitor.Left + 1500);
            bounds.Right = Math.Min(work.Right, monitor.Left + 3620);
            return bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
        }

        public static string Apply(bool dryRun)
        {
            var pids = new HashSet<uint>();
            foreach (var process in Process.GetProcessesByName("ISKarat.Loader.Win"))
                using (process) pids.Add((uint)process.Id);
            int found = 0, changed = 0, failed = 0, wide = 0;
            IntPtr oldDpi = IntPtr.Zero;
            try
            {
                try { oldDpi = SetThreadDpiAwarenessContext(new IntPtr(-4)); }
                catch (EntryPointNotFoundException) { }
                EnumWindow callback = delegate(IntPtr window, IntPtr unused)
                {
                    uint pid; GetWindowThreadProcessId(window, out pid);
                    // Leave dialogs and tool windows alone; restore/maximize main windows only.
                    if (!pids.Contains(pid) || !IsWindowVisible(window) || GetWindow(window, 4) != IntPtr.Zero) return true;
                    found++;
                    var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
                    if (!GetMonitorInfo(MonitorFromWindow(window, 2), ref info)) { failed++; return true; }
                    Rect bounds;
                    bool special = TrySpecialBounds(info.Monitor, info.Work, out bounds);
                    if (special) wide++;
                    if (dryRun) return true;
                    bool ok;
                    if (special)
                    {
                        ok = ShowWindowAsync(window, 9); // Restore before applying a custom rectangle.
                        ok = SetWindowPos(window, IntPtr.Zero, bounds.Left, bounds.Top,
                            bounds.Right - bounds.Left, bounds.Bottom - bounds.Top,
                            0x0004 | 0x0010 | 0x4000) && ok; // No z-order/focus change; asynchronous positioning.
                    }
                    else ok = ShowWindowAsync(window, 3);
                    if (ok) changed++; else failed++;
                    return true;
                };
                if (!EnumWindows(callback, IntPtr.Zero)) throw new InvalidOperationException("Nelze načíst okna KARATu.");
                GC.KeepAlive(callback);
            }
            finally { if (oldDpi != IntPtr.Zero) SetThreadDpiAwarenessContext(oldDpi); }
            return string.Format("Nalezeno: {0}, {1}: {2}, široký monitor: {3}, chyby: {4}.",
                found, dryRun ? "bez změn" : "předáno k uspořádání", changed, wide, failed);
        }
    }
}
