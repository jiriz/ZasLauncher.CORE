using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Threading;

namespace ZasLauncherGUI.Rdp;

// Owned by one UI thread. Detach the connection from the UI before StopAsync.
internal sealed class RdpConnection
{
    static RdpConnection()
    {
        NativeLibrary.SetDllImportResolver(typeof(RdpConnection).Assembly, (name, _, _) =>
            name == "zasrdp" ? NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "rdp", "libzasrdp.dylib")) : IntPtr.Zero);
    }
    private IntPtr _handle;
    private readonly Task<int> _worker;
    private Task? _stopping;
    public int State => Native.zr_state(_handle);
    public int Error => _worker.IsCompletedSuccessfully ? _worker.Result : 0;

    public RdpConnection(string host, int port, string user, string password, int width, int height)
    {
        _handle = Native.zr_create(host, port, user, password, width, height, CultureInfo.CurrentCulture.KeyboardLayoutId);
        if (_handle == IntPtr.Zero) throw new InvalidOperationException("Nelze vytvořit RDP připojení.");
        var handle = _handle;
        _worker = Task.Factory.StartNew(() => Native.zr_run(handle), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public bool Input(int kind, int a, int b = 0, int c = 0) =>
        _handle != IntPtr.Zero && Native.zr_input(_handle, kind, a, b, c) != 0;
    public bool Frame(IntPtr target, int stride, int width, int height,
        out int actualWidth, out int actualHeight, ref ulong serial) =>
        Native.zr_frame(_handle, target, stride, width, height, out actualWidth, out actualHeight, ref serial) != 0;
    public bool SetClipboard(string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text);
        try { return Native.zr_set_clip(_handle, bytes, bytes.Length) != 0; }
        finally { Array.Clear(bytes); }
    }
    public string? GetClipboard(ref ulong serial)
    {
        int count = Native.zr_get_clip(_handle, null, 0, ref serial);
        if (count <= 0 || count > 1024 * 1024 + 2) return null;
        var bytes = new byte[count];
        try
        {
            var before = serial;
            count = Native.zr_get_clip(_handle, bytes, bytes.Length, ref serial);
            return serial != before && count <= bytes.Length
                ? Encoding.Unicode.GetString(bytes, 0, count).TrimEnd('\0') : null;
        }
        finally { Array.Clear(bytes); }
    }
    public bool Cursor(byte[] pixels, out int width, out int height, out int x, out int y, ref ulong serial) =>
        Native.zr_cursor(_handle, pixels, pixels.Length, out width, out height, out x, out y, ref serial) != 0;
    public Task StopAsync() => _stopping ??= StopCoreAsync();
    private async Task StopCoreAsync()
    {
        Native.zr_stop(_handle);
        try { await _worker; }
        finally { Native.zr_free(_handle); _handle = IntPtr.Zero; }
    }
    private static class Native
    {
        [DllImport("zasrdp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int zr_cursor(IntPtr session, byte[] pixels, int capacity, out int width, out int height,
            out int x, out int y, ref ulong serial);
        private const string Library = "zasrdp";
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr zr_create([MarshalAs(UnmanagedType.LPUTF8Str)] string host, int port,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string user, [MarshalAs(UnmanagedType.LPUTF8Str)] string password,
            int width, int height, int keyboardLayout);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int zr_run(IntPtr session);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void zr_stop(IntPtr session);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void zr_free(IntPtr session);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int zr_state(IntPtr session);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int zr_input(IntPtr session, int kind, int a, int b, int c);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int zr_frame(IntPtr session, IntPtr buffer,
            int stride, int width, int height, out int actualWidth, out int actualHeight, ref ulong serial);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int zr_set_clip(IntPtr session, byte[] text, int bytes);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int zr_get_clip(IntPtr session, byte[]? text, int capacity, ref ulong serial);
    }
}
