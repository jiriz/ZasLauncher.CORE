using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ZasLauncherGUI.Rdp;

namespace ZasLauncherGUI.Class;

/// <summary>One independent FreeRDP session, rendered inside an Avalonia tab.</summary>
public sealed class RdpSessionControl : UserControl
{
    private readonly string _host, _username;
    private string _password;
    private readonly int _port;
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly Border _surface;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly HashSet<int> _keys = new();
    private readonly HashSet<int> _buttons = new();
    private RdpConnection? _connection;
    private RdpClipboardSync? _clipboardSync;
    private Window? _ownerWindow;
    private IPointer? _capturedPointer;
    private bool _releasing;
    private bool _pastePending, _swallowPasteUp;
    private WriteableBitmap? _bitmap;
    private ulong _serial, _cursorSerial;
    private readonly byte[] _cursorPixels = new byte[256*256*4];
    private Cursor? _cursor;
    private bool _started, _closed;
    private Task? _closeTask;
    private bool _inputFailed;
    private int _mouseX, _mouseY, _lastState = -1, _lastError = -1;
    private Size _lastSize;
    private DateTime _sizeChanged;

    public RdpSessionControl(string host, int port, string username, string password, string windowTitle)
    {
        _host = host; _port = port; _username = username; _password = password;
        _surface = new Border { Background = Brushes.Black, Child = _image, Focusable = true,
            ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch };
        // Keep the desktop unobstructed; connection actions belong to its tab header.
        _status.Margin = new Thickness(8, 2);
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        grid.Children.Add(_surface); Grid.SetRow(_status, 1); grid.Children.Add(_status); Content = grid;
        _timer.Tick += (_, _) => Refresh();
        Loaded += async (_, _) =>
        {
            if (_closed) return;
            _ownerWindow = TopLevel.GetTopLevel(this) as Window;
            if (_ownerWindow != null) { _ownerWindow.Deactivated += WindowDeactivated; _ownerWindow.Activated += WindowActivated; }
            ReleaseInputs(resetModifiers: true);
            _clipboardSync?.Resume();
            _timer.Start();
            if (!_started) { _started = true; await ConnectAsync(); }
            Dispatcher.UIThread.Post(() => { if (!_closed && IsLoaded && _ownerWindow?.IsActive == true) _surface.Focus(); });
        };
        // Avalonia unloads inactive tab content. Keep the protocol worker alive.
        Unloaded += (_, _) =>
        {
            _timer.Stop(); _clipboardSync?.Suspend(); ReleaseInputs(resetModifiers: true);
            if (_ownerWindow != null) { _ownerWindow.Deactivated -= WindowDeactivated; _ownerWindow.Activated -= WindowActivated; _ownerWindow = null; }
        };
        _surface.LostFocus += (_, _) => ReleaseInputs();
        _surface.PointerCaptureLost += (_, _) => ReleaseInputs();
        _surface.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _surface.AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        _surface.PointerMoved += (_, e) =>
        {
            var p = e.GetCurrentPoint(_surface).Properties;
            foreach (var button in new[] { (0x1000, p.IsLeftButtonPressed), (0x2000, p.IsRightButtonPressed), (0x4000, p.IsMiddleButtonPressed) })
                if (!button.Item2 && _buttons.Remove(button.Item1)) SendMouse(button.Item1);
            if (Locate(e.GetPosition(_image), clamp: _buttons.Count > 0)) SendMouse(0x0800);
        };
        _surface.PointerPressed += (_, e) =>
        {
            if (!Locate(e.GetPosition(_image))) return;
            _connection?.Trace($"pointer-down frame={_bitmap?.PixelSize} image={_image.Bounds.Size} remote={_mouseX},{_mouseY}");
            _surface.Focus(); _capturedPointer = e.Pointer; e.Pointer.Capture(_surface);
            var kind = e.GetCurrentPoint(_surface).Properties.PointerUpdateKind;
            int button = kind switch { PointerUpdateKind.LeftButtonPressed => 0x1000,
                PointerUpdateKind.RightButtonPressed => 0x2000, PointerUpdateKind.MiddleButtonPressed => 0x4000, _ => 0 };
            if (button != 0 && SendMouse(button | 0x8000)) _buttons.Add(button);
            e.Handled = true;
        };
        _surface.PointerReleased += (_, e) =>
        {
            Locate(e.GetPosition(_image), clamp: true);
            var kind = e.GetCurrentPoint(_surface).Properties.PointerUpdateKind;
            int button = kind switch { PointerUpdateKind.LeftButtonReleased => 0x1000,
                PointerUpdateKind.RightButtonReleased => 0x2000, PointerUpdateKind.MiddleButtonReleased => 0x4000, _ => 0 };
            if (button != 0) { SendMouse(button); _buttons.Remove(button); }
            if (_buttons.Count == 0) { _capturedPointer = null; e.Pointer.Capture(null); }
            e.Handled = true;
        };
        _surface.PointerWheelChanged += (_, e) =>
        {
            if (!Locate(e.GetPosition(_image))) return;
            int delta = Math.Clamp((int)(e.Delta.Y * 120), -255, 255);
            if (delta != 0) SendMouse(0x0200 | (delta < 0 ? 0x0100 : 0) | (delta & 0x1ff));
            e.Handled = true;
        };
    }
    public ContextMenu CreateContextMenu(Control header)
    {
        var menu = new ContextMenu();
        menu.Items.Add(ActionItem("Připojit znovu", ConnectAsync));
        menu.Items.Add(ActionItem("Odpojit", DisconnectAsync));
        menu.Items.Add(new Separator());
        menu.Items.Add(ActionItem("Ctrl+Alt+Del", () =>
        {
            SendKey(0x1d, true); SendKey(0x38, true); SendKey(0x153, true);
            SendKey(0x153, false); SendKey(0x38, false); SendKey(0x1d, false);
            return Task.CompletedTask;
        }));
        menu.Items.Add(new Separator());
        // The header remains attached even when this tab's desktop is unloaded.
        menu.Items.Add(ActionItem("Schránka → RDP", () => SendClipboardAsync(header)));
        menu.Items.Add(ActionItem("RDP → schránka", () => ReceiveClipboardAsync(header)));
        return menu;
    }
    private MenuItem ActionItem(string text, Func<Task> action)
    {
        var item = new MenuItem { Header = text };
        item.Click += async (_, e) =>
        {
            e.Handled = true;
            if (_closed) return;
            item.IsEnabled = false;
            try { await action(); }
            catch (Exception ex) { _status.Text = "RDP: " + ex.Message; }
            finally { item.IsEnabled = !_closed; }
        };
        return item;
    }
    private async Task ConnectAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            await DisconnectCoreAsync();
            if (_closed) return;
            _started = true;
            _status.Text = "Připojuji…";
            _serial = _cursorSerial = 0; _lastState = _lastError = -1;
            _connection = new RdpConnection(_host, _port, _username, _password, 1600, 1000);
            _clipboardSync = new RdpClipboardSync(_connection, text => _status.Text = text);
            _lastSize = default; _inputFailed = false; _surface.Focus();
        }
        catch (DllNotFoundException) { _status.Text = "Chybí knihovna RDP. Použijte kompletní sestavení Launcheru."; }
        catch (Exception ex) { _status.Text = "Připojení selhalo: " + ex.Message; }
        finally { _lifecycle.Release(); }
    }
    private async Task DisconnectAsync()
    {
        await _lifecycle.WaitAsync();
        try { await DisconnectCoreAsync(); }
        finally { _lifecycle.Release(); }
    }
    private async Task DisconnectCoreAsync()
    {
        ReleaseInputs();
        var sync = _clipboardSync; _clipboardSync = null;
        if (sync != null) await sync.StopAsync();
        var connection = _connection; _connection = null;
        if (connection != null) { _status.Text = "Odpojuji…"; await connection.StopAsync(); }
        _image.Source = null; _bitmap?.Dispose(); _bitmap = null;
        _surface.Cursor = null; _cursor?.Dispose(); _cursor = null;
        _status.Text = "Odpojeno";
    }
    public Task CloseAsync() => _closeTask ??= CloseCoreAsync();
    private async Task CloseCoreAsync()
    {
        _closed = true; _timer.Stop();
        await DisconnectAsync();
        _password = string.Empty;
    }
    private void Refresh()
    {
        var c = _connection;
        if (c == null || _closed) return;
        int state = c.State;
        int error = c.Error;
        if (state != _lastState || error != _lastError)
        {
            _status.Text = state switch { 0 or 1 => "Připojuji…", 2 => "Připojeno",
                3 => "Odpojeno", _ => $"Připojení selhalo (0x{error:X8})." };
            _lastState = state; _lastError = error;
            c.Trace($"state={state} connection-error=0x{error:X8}");
        }
        ulong ignored = 0;
        c.Frame(IntPtr.Zero, 0, 0, 0, out int width, out int height, ref ignored);
        if (width > 0 && height > 0)
        {
            if (_bitmap == null || _bitmap.PixelSize != new PixelSize(width, height))
            {
                _image.Source = null; _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                _image.Source = _bitmap; _serial = 0;
            }
            using var buffer = _bitmap.Lock();
            if (c.Frame(buffer.Address, buffer.RowBytes, width, height, out _, out _, ref _serial)) _image.InvalidateVisual();
        }
        if (c.Cursor(_cursorPixels, out int cw, out int ch, out int cx, out int cy, ref _cursorSerial))
        {
            Cursor next;
            if (cw > 0)
            {
                using var cursorImage = new WriteableBitmap(new PixelSize(cw, ch), new Vector(96,96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
                using (var pixels = cursorImage.Lock())
                    for (int y = 0; y < ch; y++) Marshal.Copy(_cursorPixels, y*cw*4, pixels.Address + y*pixels.RowBytes, cw*4);
                next = new Cursor(cursorImage, new PixelPoint(Math.Clamp(cx,0,cw-1),Math.Clamp(cy,0,ch-1)));
            }
            else next = new Cursor(cw < 0 ? StandardCursorType.None : StandardCursorType.Arrow);
            _surface.Cursor = next; _cursor?.Dispose(); _cursor = next;
        }
        if (state == 2)
        {
            if (TopLevel.GetTopLevel(this) is { } top) _clipboardSync?.Poll(top);
            var size = _surface.Bounds.Size;
            if (size != _lastSize) { _lastSize = size; _sizeChanged = DateTime.UtcNow; }
            else if (_sizeChanged != DateTime.MaxValue && DateTime.UtcNow - _sizeChanged > TimeSpan.FromMilliseconds(500))
            {
                // Logical pixels keep text readable on Retina. The renderer scales to physical pixels.
                int w = Math.Clamp((int)size.Width, 200, 4096) & ~1;
                int h = Math.Clamp((int)size.Height, 200, 4096);
                if (c.Input(4, w, h)) _sizeChanged = DateTime.MaxValue;
            }
        }
    }
    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        int code = RdpKeyboard.ScanCode(e.PhysicalKey);
        if (code == 0) return;
        if (code == 0x2e && e.KeyModifiers.HasFlag(KeyModifiers.Control)) _connection?.Trace("remote-copy Ctrl+C");
        if (code == 0x2f && e.KeyModifiers.HasFlag(KeyModifiers.Control) && RdpClipboardSync.Enabled)
        {
            _connection?.Trace("local-paste Ctrl+V");
            e.Handled = true; _swallowPasteUp = true;
            if (_pastePending) return;
            _pastePending = true;
            var connection = _connection;
            try
            {
                if (_clipboardSync is not { } sync || TopLevel.GetTopLevel(this) is not { } top) return;
                if (!await sync.TransferAsync(top, 3)) return;
                if (connection == _connection && !_closed && IsLoaded && _surface.IsFocused)
                {
                    // Ctrl may have been released while files were enumerated.
                    SendKey(0x1d, true); SendKey(0x2f, true); SendKey(0x2f, false);
                    if (!_keys.Contains(0x1d)) SendKey(0x1d, false);
                }
            }
            finally { _pastePending = false; }
            return;
        }
        if (SendKey(code, true)) _keys.Add(code);
        e.Handled = true;
    }
    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        int code = RdpKeyboard.ScanCode(e.PhysicalKey);
        if (code == 0) return;
        if (code == 0x2f && _swallowPasteUp) { _swallowPasteUp = false; e.Handled = true; return; }
        SendKey(code, false); _keys.Remove(code); e.Handled = true;
    }
    private bool SendKey(int code, bool down) => SendInput(2, code, down ? 1 : 0, 0);
    private bool SendMouse(int flags) => SendInput(1, flags, _mouseX, _mouseY);
    private bool SendInput(int kind, int a, int b, int c)
    {
        var connection = _connection;
        if (connection == null) return false;
        if (connection.Input(kind, a, b, c)) return true;
        // Never drop a key-up silently. Abort the session if its bounded queue cannot keep up.
        if (connection.State == 2 && !_inputFailed) { _inputFailed = true; _ = DisconnectAsync(); }
        return false;
    }
    private void WindowDeactivated(object? sender, EventArgs e) { _connection?.Trace("window deactivated"); ReleaseInputs(resetModifiers: true); }
    private void WindowActivated(object? sender, EventArgs e)
    {
        _connection?.Trace("window activated");
        ReleaseInputs(resetModifiers: true);
        if (!_closed && IsLoaded) _surface.Focus();
    }
    private void ReleaseInputs(bool resetModifiers = false)
    {
        if (_releasing) return;
        _releasing = true;
        var pointer = _capturedPointer; _capturedPointer = null;
        pointer?.Capture(null);
        // Direct enqueue avoids recursively starting DisconnectAsync on overflow.
        bool ok = true;
        foreach (int key in _keys) if (_connection != null) ok &= _connection.Input(2, key, 0);
        foreach (int button in _buttons) if (_connection != null) ok &= _connection.Input(1, button, _mouseX, _mouseY);
        if (resetModifiers && _connection?.State == 2)
        {
            // macOS may consume releases during Cmd+Tab; LostFocus alone does not cover window deactivation.
            foreach (int code in new[] { 0x1d, 0x11d, 0x2a, 0x36, 0x38, 0x138, 0x15b, 0x15c })
                ok &= _connection.Input(2, code, 0);
            foreach (int button in new[] { 0x1000, 0x2000, 0x4000 })
                ok &= _connection.Input(1, button, _mouseX, _mouseY);
        }
        _keys.Clear(); _buttons.Clear(); _releasing = false;
        if (!ok && !_inputFailed && _connection?.State == 2)
        {
            _inputFailed = true;
            Dispatcher.UIThread.Post(async () => await DisconnectAsync());
        }
    }
    private bool Locate(Point p, bool clamp = false)
    {
        if (_bitmap == null) return false;
        var bounds = _image.Bounds.Size; var pixels = _bitmap.PixelSize;
        double scale = Math.Min(bounds.Width / pixels.Width, bounds.Height / pixels.Height);
        if (scale <= 0) return false;
        double x = (p.X - (bounds.Width - pixels.Width * scale) / 2) / scale;
        double y = (p.Y - (bounds.Height - pixels.Height * scale) / 2) / scale;
        if (!clamp && (x < 0 || y < 0 || x >= pixels.Width || y >= pixels.Height)) return false;
        _mouseX = Math.Clamp((int)x, 0, pixels.Width - 1); _mouseY = Math.Clamp((int)y, 0, pixels.Height - 1);
        return true;
    }
    private async Task SendClipboardAsync(Control header)
    {
        if (_clipboardSync is { } sync && TopLevel.GetTopLevel(header) is { } top) await sync.TransferAsync(top, 1);
    }
    private async Task ReceiveClipboardAsync(Control header)
    {
        if (_clipboardSync is { } sync && TopLevel.GetTopLevel(header) is { } top) await sync.TransferAsync(top, 2);
    }
}
