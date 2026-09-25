using System;
using System.Runtime.InteropServices;

namespace ZasLauncherGUI.Utility;

internal static class MacAppIdentity
{
    public static void SetProcessName(string name)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        try
        {
            var processInfoClass = objc_getClass("NSProcessInfo");
            var processInfo = objc_msgSend(processInfoClass, sel_registerName("processInfo"));
            var nsName = CreateNSString(name);

            if (processInfo != IntPtr.Zero && nsName != IntPtr.Zero)
                objc_msgSend(processInfo, sel_registerName("setProcessName:"), nsName);
        }
        catch
        {
            // Branding fallback only. App startup should not depend on AppKit interop.
        }
    }

    private static IntPtr CreateNSString(string value)
    {
        var nsStringClass = objc_getClass("NSString");
        return objc_msgSend(nsStringClass, sel_registerName("stringWithUTF8String:"), value);
    }

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, string value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr value);
}
