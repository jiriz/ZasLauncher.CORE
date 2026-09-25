using System;
using System.IO;
using Avalonia.Platform;
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
                SetApplicationIcon(app);
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

    private static void SetApplicationIcon(IntPtr app)
    {
        using var source = AssetLoader.Open(new Uri("avares://ZasLauncherGUI/Assets/launcher.png"));
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        IntPtr data = IntPtr.Zero;
        IntPtr image = IntPtr.Zero;
        try
        {
            // NSData copies the bytes; NSApplication retains its new icon.
            data = objc_msgSend_InitBytes(
                objc_msgSend(objc_getClass("NSData"), sel_registerName("alloc")),
                sel_registerName("initWithBytes:length:"), pinned.AddrOfPinnedObject(), (nuint)bytes.Length);
            if (data == IntPtr.Zero)
                return;
            image = objc_msgSend_Object(
                objc_msgSend(objc_getClass("NSImage"), sel_registerName("alloc")),
                sel_registerName("initWithData:"), data);
            if (image != IntPtr.Zero)
                objc_msgSend_Object(app, sel_registerName("setApplicationIconImage:"), image);
        }
        finally
        {
            if (image != IntPtr.Zero)
                objc_msgSend(image, sel_registerName("release"));
            if (data != IntPtr.Zero)
                objc_msgSend(data, sel_registerName("release"));
            pinned.Free();
        }
    }

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_Object(IntPtr receiver, IntPtr selector, IntPtr value);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_InitBytes(IntPtr receiver, IntPtr selector, IntPtr bytes, nuint length);

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
