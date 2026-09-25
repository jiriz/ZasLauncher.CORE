using System.Threading;
using System.Threading.Tasks;
namespace ZasLauncherGUI.Rdp;
internal interface IRdpClipboardConnection
{
    int State { get; }
    bool IsStopping { get; }
    (int Kind, ulong Generation) ClipboardOffer();
    bool SetClipboard(string text);
    bool SetFiles(byte[] descriptors, string[] paths);
    (int Kind, ulong Generation, byte[] Data) ClipboardSnapshot();
    Task WaitClipboardReadyAsync(CancellationToken cancellation);
    Task<int> ReadFileChunkAsync(ulong generation, uint index, ulong offset, byte[] buffer, int wanted, CancellationToken cancellation);
}
