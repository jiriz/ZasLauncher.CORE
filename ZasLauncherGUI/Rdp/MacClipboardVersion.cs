using System;
using System.Runtime.InteropServices;
namespace ZasLauncherGUI.Rdp;
internal static class MacClipboardVersion
{
    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern IntPtr objc_getClass(string name);
    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern IntPtr sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint="objc_msgSend")] private static extern IntPtr Send(IntPtr receiver,IntPtr selector);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint="objc_msgSend")] private static extern long SendLong(IntPtr receiver,IntPtr selector);
    public static long Read() => SendLong(Send(objc_getClass("NSPasteboard"),sel_registerName("generalPasteboard")),sel_registerName("changeCount"));
}
