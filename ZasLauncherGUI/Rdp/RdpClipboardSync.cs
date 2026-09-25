using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace ZasLauncherGUI.Rdp;

// UI-thread owned. Only the selected desktop polls; cancellation is awaited before native disposal.
internal sealed class RdpClipboardSync
{
    private readonly IRdpClipboardConnection _connection;
    private readonly Action<string> _status;
    private readonly Func<long> _version;
    private readonly bool _enabled, _cleanupCache;
    private CancellationTokenSource? _cancellation;
    private Task<bool> _pending = Task.FromResult(false);
    private long _localVersion = -1, _operationVersion, _failedLocalVersion = -2;
    private string? _localFailure;
    private ulong _remoteGeneration;
    private DateTime _nextPoll;
    private bool _suspended, _stopped;
    public static bool Enabled => OperatingSystem.IsMacOS() &&
        !(AppContext.TryGetSwitch("ZasLauncher.DisableClipboardSync", out bool disabled) && disabled);
    public RdpClipboardSync(IRdpClipboardConnection connection, Action<string> status, Func<long>? version = null)
    {
        _connection = connection; _status = status;
        _version = version ?? MacClipboardVersion.Read;
        _enabled = version != null || Enabled;
        _cleanupCache = version == null;
    }
    public void Resume() { _suspended = false; }
    public void Suspend() { _suspended = true; _cancellation?.Cancel(); }
    public async Task StopAsync() { _stopped = true; Suspend(); await _pending; _cancellation?.Dispose(); _cancellation = null; }
    public void Poll(TopLevel top)
    {
        if (!_enabled || _suspended || _stopped || _connection.State != 2) return;
        if (!_pending.IsCompleted)
        {
            if (_version() != _operationVersion) _cancellation?.Cancel();
            return;
        }
        if (DateTime.UtcNow < _nextPoll) return;
        _nextPoll = DateTime.UtcNow.AddMilliseconds(150);
        _ = Start(top, 0);
    }
    // direction: automatic, send local, receive remote. The caller supplies the selected tab's TopLevel.
    public async Task<bool> TransferAsync(TopLevel top, int direction)
    {
        if (!_enabled || _stopped) return false;
        while (!_pending.IsCompleted) await _pending;
        return !_stopped && await Start(top, direction);
    }
    private Task<bool> Start(TopLevel top, int direction)
    {
        _cancellation?.Dispose(); _cancellation = new CancellationTokenSource();
        _operationVersion = _version();
        return _pending = RunAsync(top, direction, _cancellation.Token);
    }
    private async Task<bool> RunAsync(TopLevel top, int direction, CancellationToken cancellation)
    {
        bool sending = false;
        try
        {
            if (top.Clipboard is not { } clipboard || _connection.State != 2) return false;
            bool localChanged = _localVersion != _operationVersion;
            // Do not overwrite a fresh Windows copy merely because another Mac app (e.g.
            // Parallels) changed the pasteboard. Advertise local data on Ctrl+V / explicit send.
            bool send = direction == 1 || (direction == 3 && localChanged);
            if (send)
            {
                sending = true;
                _connection.Trace("local-copy begin");
                ulong previousRemote = _connection.ClipboardOffer().Generation;
                var items = await clipboard.TryGetFilesAsync();
                Check(cancellation);
                var paths = items?.Select(i => i.TryGetLocalPath()).Where(p => p != null).Cast<string>().ToArray();
                if (_cleanupCache) await Task.Run(() => ClipboardFiles.CleanupOld(paths ?? Array.Empty<string>()), cancellation);
                Check(cancellation);
                if (paths is { Length: > 0 })
                {
                    var description = await Task.Run(() => ClipboardFiles.Describe(paths, cancellation), cancellation);
                    Check(cancellation);
                    _connection.Trace("local-copy files count=" + description.Paths.Length);
                    if (!_connection.SetFiles(description.Descriptors, description.Paths)) throw new IOException("Nelze připravit soubory pro RDP.");
                }
                else
                {
                    string? text = await clipboard.TryGetTextAsync();
                    if (text == null) throw new IOException("Ve schránce není text ani soubor k vložení.");
                    Check(cancellation);
                    _connection.Trace("local-copy text");
                    if (!_connection.SetClipboard(text)) throw new IOException("Text schránky překračuje limit 1 MiB.");
                }
                await _connection.WaitClipboardReadyAsync(cancellation);
                Check(cancellation);
                _localVersion = _operationVersion;
                _failedLocalVersion = -2; _localFailure = null;
                // Ignore an older remote offer after a newer copy on the Mac.
                _remoteGeneration = previousRemote;
                _connection.Trace("local-copy acknowledged");
                return true;
            }
            if (direction == 3 && _failedLocalVersion == _operationVersion) { _status(_localFailure!); return false; }
            if (direction == 3) { await _connection.WaitClipboardReadyAsync(cancellation); return true; }
            var offer = _connection.ClipboardOffer();
            if (offer.Kind == 0 || (offer.Generation == _remoteGeneration && direction != 2)) return false;
            var snapshot = _connection.ClipboardSnapshot();
            if (snapshot.Kind == 0) return false;
            _remoteGeneration = snapshot.Generation;
            _connection.Trace("remote-copy begin kind=" + snapshot.Kind);
            if (snapshot.Kind < 0) throw new IOException("Vzdálenou schránku nelze načíst.");
            if (snapshot.Kind == 1)
            {
                string text = Encoding.Unicode.GetString(snapshot.Data).TrimEnd('\0');
                Array.Clear(snapshot.Data);
                Check(cancellation);
                await clipboard.SetTextAsync(text);
            }
            else if (snapshot.Kind == 2)
            {
                string[]? downloaded = null;
                bool published = false;
                try
                {
                    downloaded = await ClipboardFiles.DownloadAsync(_connection, snapshot.Data, snapshot.Generation, cancellation, _status);
                    Check(cancellation);
                    if (_connection.ClipboardOffer().Generation != snapshot.Generation) throw new OperationCanceledException();
                    var files = new List<IStorageItem>();
                    foreach (var path in downloaded)
                    {
                        IStorageItem? item = Directory.Exists(path)
                            ? await top.StorageProvider.TryGetFolderFromPathAsync(path)
                            : await top.StorageProvider.TryGetFileFromPathAsync(path);
                        if (item == null) throw new IOException("Stažený soubor nelze vložit do schránky.");
                        files.Add(item);
                    }
                    Check(cancellation);
                    await clipboard.SetFilesAsync(files);
                    published = true;
                }
                finally { if (!published && downloaded is { Length: > 0 }) ClipboardFiles.RemoveDownload(downloaded[0]); }
            }
            _operationVersion = _localVersion = _version();
            _connection.Trace("remote-copy published");
            _status("Schránka z RDP je připravena na Macu");
            return true;
        }
        catch (OperationCanceledException) { _connection.Trace("clipboard cancelled"); }
        catch (Exception ex)
        {
            _connection.Trace("clipboard failed type=" + ex.GetType().Name);
            string message = "Schránka: " + ex.Message;
            if (sending) { _failedLocalVersion = _operationVersion; _localFailure = message; }
            _status(message); _localVersion = _operationVersion;
        }
        return false;
    }
    private void Check(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (_connection.IsStopping || _connection.State != 2 || _version() != _operationVersion)
            throw new OperationCanceledException();
    }
}
