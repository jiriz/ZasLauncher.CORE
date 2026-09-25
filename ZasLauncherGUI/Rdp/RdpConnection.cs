using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Threading;

namespace ZasLauncherGUI.Rdp;

// Owned by one UI thread. Detach the connection from the UI before StopAsync.
internal sealed class RdpConnection : IRdpClipboardConnection
{
    static RdpConnection()
    {
        NativeLibrary.SetDllImportResolver(typeof(RdpConnection).Assembly, (name, _, _) =>
            name == "zasrdp" ? NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "rdp", "libzasrdp.dylib")) : IntPtr.Zero);
    }
    private readonly string _traceId = Guid.NewGuid().ToString("N")[..8];
    private IntPtr _handle;
    private readonly Task<int> _worker;
    private Task? _stopping;
    public int State => _handle == IntPtr.Zero ? 3 : Native.zr_state(_handle);
    public bool IsStopping => _stopping != null;
    public int Error => _worker.IsCompletedSuccessfully ? _worker.Result : 0;

    public RdpConnection(string host, int port, string user, string password, int width, int height)
    {
        _handle = Native.zr_create(host, port, user, password, width, height, CultureInfo.CurrentCulture.KeyboardLayoutId);
        if (_handle == IntPtr.Zero) throw new InvalidOperationException("Nelze vytvořit RDP připojení.");
        Trace("created version=" + typeof(RdpConnection).Assembly.GetName().Version);
        var handle = _handle;
        _worker = Task.Factory.StartNew(() => Native.zr_run(handle), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
    public void Trace(string state)
    {
        var buffer = new byte[256];
        if (_handle != IntPtr.Zero) Native.zr_clip_diagnostics(_handle, buffer, buffer.Length);
        int count = Array.IndexOf(buffer, (byte)0);
        RdpTrace.Write(_traceId, state + " " + Encoding.UTF8.GetString(buffer, 0, count < 0 ? buffer.Length : count));
    }
    public bool Input(int kind, int a, int b = 0, int c = 0) =>
        _handle != IntPtr.Zero && Native.zr_input(_handle, kind, a, b, c) != 0;
    public bool Frame(IntPtr target, int stride, int width, int height,
        out int actualWidth, out int actualHeight, ref ulong serial) =>
        Native.zr_frame(_handle, target, stride, width, height, out actualWidth, out actualHeight, ref serial) != 0;
    public bool SetClipboard(string text)
    {
        if (text.Length > 512 * 1024) return false;
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
    public (int Kind, ulong Generation) ClipboardOffer()
    {
        if (_handle == IntPtr.Zero) return default;
        Native.zr_clip_info(_handle, out int kind, out ulong generation, null, 0);
        return (kind, generation);
    }
    public (int Kind, ulong Generation, byte[] Data) ClipboardSnapshot()
    {
        if (_handle == IntPtr.Zero) return default;
        int count = Native.zr_clip_info(_handle, out int kind, out ulong generation, null, 0);
        if (count <= 0 || count > 4 + 4096 * 592) return (kind, generation, Array.Empty<byte>());
        var data = new byte[count];
        int read = Native.zr_clip_info(_handle, out int actualKind, out ulong actualGeneration, data, count);
        return read == count && kind == actualKind && generation == actualGeneration
            ? (kind, generation, data) : (0, actualGeneration, Array.Empty<byte>());
    }
    public bool SetFiles(byte[] descriptors, string[] paths)
    {
        var pointers = new IntPtr[paths.Length];
        try
        {
            for (int i=0;i<paths.Length;i++) pointers[i] = Marshal.StringToCoTaskMemUTF8(paths[i]);
            return Native.zr_set_files(_handle, descriptors, descriptors.Length, pointers, pointers.Length) != 0;
        }
        finally { foreach (var pointer in pointers) if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer); }
    }
    public async Task<int> ReadFileChunkAsync(ulong generation, uint index, ulong offset, byte[] buffer, int wanted, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (IsStopping || Native.zr_file_start(_handle, generation, index, offset, (uint)wanted) == 0)
            throw new IOException("Obsah vzdálené schránky se změnil nebo přenos již není dostupný.");
        var deadline = DateTime.UtcNow.AddSeconds(30);
        try { while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            if (IsStopping) throw new OperationCanceledException();
            int count = Native.zr_file_read(_handle, generation, buffer, buffer.Length);
            if (count >= 0) return count;
            if (count == -2) throw new IOException("Přenos souboru byl přerušen nebo se změnila schránka.");
            if (DateTime.UtcNow > deadline) throw new IOException("Vzdálený počítač neodpovídá na přenos souboru.");
            await Task.Delay(20, cancellation);
        } }
        finally { if (!IsStopping) Native.zr_file_cancel(_handle); }
    }
    public async Task WaitClipboardReadyAsync(CancellationToken cancellation)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!IsStopping && State == 2)
        {
            cancellation.ThrowIfCancellationRequested();
            int ready = Native.zr_clip_sent(_handle);
            if (ready == 1) return;
            if (ready < 0) throw new IOException("Vzdálený počítač odmítl přenos schránky.");
            if (DateTime.UtcNow > deadline) throw new IOException("Vzdálený počítač nepotvrdil přenos schránky. Ověřte povolení schránky na serveru.");
            await Task.Delay(20, cancellation);
        }
        throw new OperationCanceledException();
    }
    public bool Cursor(byte[] pixels, out int width, out int height, out int x, out int y, ref ulong serial) =>
        Native.zr_cursor(_handle, pixels, pixels.Length, out width, out height, out x, out y, ref serial) != 0;
    public Task StopAsync() => _stopping ??= StopCoreAsync();
    private async Task StopCoreAsync()
    {
        Trace("stop");
        Native.zr_stop(_handle);
        try { await _worker; }
        finally { Native.zr_free(_handle); _handle = IntPtr.Zero; }
    }
    private static class Native
    {
        [DllImport("zasrdp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int zr_cursor(IntPtr session, byte[] pixels, int capacity, out int width, out int height,
            out int x, out int y, ref ulong serial);
        [DllImport("zasrdp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int zr_set_files(IntPtr session, byte[] descriptors, int size, IntPtr[] paths, int count);
        [DllImport("zasrdp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int zr_clip_info(IntPtr session, out int kind, out ulong generation, byte[]? target, int capacity);
        [DllImport("zasrdp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int zr_file_start(IntPtr session, ulong generation, uint index, ulong offset, uint size);
        [DllImport("zasrdp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void zr_file_cancel(IntPtr session);
        [DllImport("zasrdp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int zr_file_read(IntPtr session, ulong generation, byte[] target, int capacity);
        [DllImport("zasrdp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int zr_clip_sent(IntPtr session);
        [DllImport("zasrdp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void zr_clip_diagnostics(IntPtr session, byte[] target, int capacity);
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
