using System.Buffers.Binary;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using ZasLauncherGUI.Rdp;

internal static class ClipboardContract
{
    public static async Task RunWireAsync(TopLevel top,int port)
    {
        var peer = new RdpConnection("127.0.0.1",port,"test","",1024,768);
        long version=1;string status="";
        var sync=new RdpClipboardSync(peer,s=>status=s,()=>version);
        try
        {
            for(int i=0;i<200 && peer.State!=2;i++) await Task.Delay(20);
            Require(peer.State==2,"wire connection");
            await top.Clipboard!.SetTextAsync("LOCAL");
            Require(await sync.TransferAsync(top,3),"managed/native local send: "+status);
            for(int i=0;i<200 && await top.Clipboard.TryGetTextAsync()!="WIRE";i++)
            {
                await sync.TransferAsync(top,0);await Task.Delay(20);
            }
            Require(await top.Clipboard.TryGetTextAsync()=="WIRE","managed/native remote copy: "+status);
            Console.WriteLine("PASS: clipboard coordinator + native adapter + real RDP peer, both text directions");
        }
        finally {await sync.StopAsync();await peer.StopAsync();}
    }
    public static async Task RunSyncAsync(TopLevel top)
    {
        var clipboard=top.Clipboard!;
        var peer=new ClipboardPeer(Array.Empty<string>());
        long version=1;
        var sync=new RdpClipboardSync(peer, _=>{}, ()=>version);
        await clipboard.SetTextAsync("Mac → RDP\nřádek 2");
        bool sent=await sync.TransferAsync(top,3);
        Require(peer.Sent=="Mac → RDP\nřádek 2" && sent,"automatic local paste preparation");
        peer.Remote=(1,1,Encoding.Unicode.GetBytes("Windows → Mac\r\nřádek 2\0"));
        await sync.TransferAsync(top,0);
        Require(await clipboard.TryGetTextAsync()=="Windows → Mac\r\nřádek 2", "remote text");
        sync.Suspend();
        peer.Remote=(1,2,Encoding.Unicode.GetBytes("background\0"));
        sync.Poll(top);
        Require(await clipboard.TryGetTextAsync()=="Windows → Mac\r\nřádek 2", "background tab overwrote clipboard");
        sync.Resume();
        version++;
        await clipboard.SetTextAsync("new Mac copy");
        await sync.TransferAsync(top,3);
        Require(peer.Sent=="new Mac copy", "fresh local copy");
        version++;
        await clipboard.SetTextAsync("Mac copy before ACK");
        peer.OnAck=()=>peer.Remote=(1,3,Encoding.Unicode.GetBytes("new Windows copy during ACK\0"));
        Require(await sync.TransferAsync(top,3),"send awaiting ACK");peer.OnAck=null;
        await sync.TransferAsync(top,0);
        Require(await clipboard.TryGetTextAsync()=="new Windows copy during ACK", "new remote offer was dropped during ACK");
        // An older local change/Parallels mirror must not cause us to discard a new Windows copy.
        version++;await clipboard.SetTextAsync("Mac clipboard mirror");
        peer.Remote=(1,4,Encoding.Unicode.GetBytes("fresh Windows copy\0"));
        await sync.TransferAsync(top,0);
        Require(await clipboard.TryGetTextAsync()=="fresh Windows copy", "remote copy discarded due to older Mac change");
        version++; peer.RejectSend=true;
        await clipboard.SetTextAsync("rejected");
        Require(!await sync.TransferAsync(top,3),"rejected transfer reported success");
        Require(!await sync.TransferAsync(top,3),"retry would paste stale clipboard");
        await sync.StopAsync();
        Console.WriteLine("PASS: automatic bidirectional text, multiline Unicode, suspended tab and newer local copy");
    }
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "zas-clipboard-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Přílohy", "Vnořené"));
        try
        {
            var bytes = Enumerable.Range(0, 2*1024*1024+137).Select(i => (byte)(i%251)).ToArray();
            File.WriteAllBytes(Path.Combine(root, "Přílohy", "Vnořené", "data.bin"), bytes);
            File.WriteAllText(Path.Combine(root, "Přílohy", "prázdný.txt"), "");
            var manifest = ClipboardFiles.Describe(new[] { Path.Combine(root, "Přílohy") });
            var parsed = ClipboardFiles.Parse(manifest.Descriptors);
            Require(parsed.Count == 4 && parsed.Any(p => p.Size == (ulong)bytes.Length), "descriptor roundtrip");
            foreach (var name in new[] { "../escape", "..\\escape", "C:\\file", "\\absolute", "a\\..\\b", "a/b", "a\\", "a.", "a\0b" })
            {
                bool rejected = false; try { ClipboardFiles.ValidateName(name); } catch (IOException) { rejected = true; }
                Require(rejected, "path accepted: " + name);
            }
            var bad = (byte[])manifest.Descriptors.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(bad, 4097);
            RequireThrows(() => ClipboardFiles.Parse(bad));
            var fake = new ClipboardPeer(manifest.Paths);
            var downloaded = ClipboardFiles.DownloadAsync(fake, manifest.Descriptors, 7, default, _ => {}).GetAwaiter().GetResult();
            try
            {
                Require(File.ReadAllBytes(Path.Combine(downloaded[0], "Vnořené", "data.bin")).SequenceEqual(bytes), "chunked file content");
                Require(new FileInfo(Path.Combine(downloaded[0], "prázdný.txt")).Length == 0, "empty file");
                Require(fake.Chunks >= 3 && fake.Largest <= 1024*1024, "bounded chunks");
            }
            finally { ClipboardFiles.RemoveDownload(downloaded[0]); }
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            bool stopped = false;
            try { ClipboardFiles.DownloadAsync(fake, manifest.Descriptors, 7, cancelled.Token, _ => {}).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { stopped = true; }
            Require(stopped, "cancelled download");
            Console.WriteLine("PASS: clipboard file tree, Unicode names, path rejection, empty/large files, bounded chunks and cancellation");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void RequireThrows(Action action) { try { action(); } catch (IOException) { return; } throw new Exception("Expected rejection"); }
    internal sealed class ClipboardPeer(string[] paths) : IRdpClipboardConnection
    {
        public int State => 2;
        public bool IsStopping => false;
        public int Chunks, Largest;
        public string? Sent;
        public bool RejectSend;
        public Action? OnAck;
        public (int Kind, ulong Generation, byte[] Data) Remote = (0,0,Array.Empty<byte>());
        public (int Kind, ulong Generation) ClipboardOffer() => (Remote.Kind,Remote.Generation);
        public bool SetClipboard(string text) { if (RejectSend) return false; Sent=text; return true; }
        public bool SetFiles(byte[] descriptors, string[] paths) => true;
        public (int Kind, ulong Generation, byte[] Data) ClipboardSnapshot() => (Remote.Kind,Remote.Generation,(byte[])Remote.Data.Clone());
        public Task WaitClipboardReadyAsync(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); OnAck?.Invoke(); return Task.CompletedTask; }
        public async Task<int> ReadFileChunkAsync(ulong generation, uint index, ulong offset, byte[] buffer, int wanted, CancellationToken cancellation)
        {
            Require(generation==7, "generation"); Chunks++; Largest=Math.Max(Largest,wanted);
            await using var stream = File.OpenRead(paths[index]); stream.Position=checked((long)offset);
            return await stream.ReadAsync(buffer.AsMemory(0,wanted),cancellation);
        }
    }
}
