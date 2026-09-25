using System;
using System.IO;
namespace ZasLauncherGUI.Rdp;
internal static class RdpTrace
{
    private static readonly object Gate = new();
    // Protocol state only. Never pass clipboard text, file names, hosts, usernames or credentials.
    internal static void Write(string session, string state)
    {
        if (AppContext.TryGetSwitch("ZasLauncher.DisableClipboardSync", out bool disabled) && disabled) return;
        try
        {
            lock (Gate)
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZasLauncher", "logs");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "rdp.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024*1024) File.Move(path, path + ".previous", true);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{session}] {state}\n");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
