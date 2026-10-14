using System;
using System.Runtime.InteropServices;

namespace ZasLauncherGUI.Rdp;

// One activity per protocol worker, including sessions in inactive tabs.
// Allow system/display sleep; prevent App Nap while a requested RDP session runs.
internal sealed class MacRdpActivity : IDisposable
{
    private IntPtr _activity;
    private readonly IntPtr _process;
    public bool Active => _activity != IntPtr.Zero;
    public MacRdpActivity()
    {
        if (!OperatingSystem.IsMacOS()) return;
        _process = Send(objc_getClass("NSProcessInfo"), sel_registerName("processInfo"));
        var pool = Send(objc_getClass("NSAutoreleasePool"), sel_registerName("new"));
        try
        {
            var reason = Text(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), "ZasLauncher RDP connection");
            const ulong options = 0x00FFFFFFUL & ~(1UL << 20); // NSActivityUserInitiatedAllowingIdleSystemSleep
            _activity = Begin(_process, sel_registerName("beginActivityWithOptions:reason:"), options, reason);
            if (_activity != IntPtr.Zero) Send(_activity, sel_registerName("retain"));
        }
        finally { Send(pool, sel_registerName("drain")); }
    }
    public void Dispose()
    {
        if (_activity == IntPtr.Zero) return;
        SendArg(_process, sel_registerName("endActivity:"), _activity);
        Send(_activity, sel_registerName("release"));
        _activity = IntPtr.Zero;
    }
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint="objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint="objc_msgSend")] private static extern void SendArg(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport(ObjC, EntryPoint="objc_msgSend")] private static extern IntPtr Text(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport(ObjC, EntryPoint="objc_msgSend")] private static extern IntPtr Begin(IntPtr receiver, IntPtr selector, ulong options, IntPtr reason);
}
