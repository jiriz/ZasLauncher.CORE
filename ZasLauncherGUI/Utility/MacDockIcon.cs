using System;
using System.Runtime.InteropServices;

namespace ZasLauncherGUI.Utility;

internal static class MacDockIcon
{
    private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";
    private const nint NSApplicationActivationPolicyRegular = 0;
    private const nint NSApplicationActivationPolicyAccessory = 1;

    public static void SetVisible(bool visible)
    {
        if (!OperatingSystem.IsMacOS())
            return;

        try
        {
            var appClass = objc_getClass("NSApplication");
            var sharedApplicationSelector = sel_registerName("sharedApplication");
            var setActivationPolicySelector = sel_registerName("setActivationPolicy:");
            var app = objc_msgSend(appClass, sharedApplicationSelector);

            if (app == IntPtr.Zero)
                return;

            objc_msgSend_SetActivationPolicy(
                app,
                setActivationPolicySelector,
                visible
                    ? NSApplicationActivationPolicyRegular
                    : NSApplicationActivationPolicyAccessory);

            if (visible)
            {
                objc_msgSend_ActivateIgnoringOtherApps(
                    app,
                    sel_registerName("activateIgnoringOtherApps:"),
                    true);
            }
        }
        catch
        {
            // Dock visibility is a macOS convenience; failing here must not break RDP.
        }
    }

    [DllImport(ObjCLibrary)]
    private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(ObjCLibrary)]
    private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool objc_msgSend_SetActivationPolicy(
        IntPtr receiver,
        IntPtr selector,
        nint policy);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_ActivateIgnoringOtherApps(
        IntPtr receiver,
        IntPtr selector,
        [MarshalAs(UnmanagedType.I1)] bool flag);
}
