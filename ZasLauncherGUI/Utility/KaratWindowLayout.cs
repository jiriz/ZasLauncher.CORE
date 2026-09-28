using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ZasLauncherGUI.Utility;

internal static class KaratWindowLayout
{
    private static int _running;
    internal static async Task<string> ArrangeAsync()
    {
        if (Interlocked.Exchange(ref _running, 1) != 0) return "Uspořádání již běží.";
        try
        {
            if (OperatingSystem.IsWindows()) return await Task.Run(() => KaratWindowLayoutNative.Apply(false));
            if (!OperatingSystem.IsMacOS()) return "Uspořádání oken je dostupné na Windows a přes Parallels na Macu.";
            var executable = ParallelsPrlctl.ExecutablePath;
            var listing = await RunAsync(executable, "list", "-i", "--json");
            using var data = JsonDocument.Parse(listing);
            using var source = typeof(KaratWindowLayout).Assembly.GetManifestResourceStream("KaratWindowLayoutNative.cs")
                ?? throw new InvalidOperationException("Chybí modul uspořádání oken.");
            using var reader = new StreamReader(source);
            var script = "$ErrorActionPreference='Stop'; Add-Type -TypeDefinition @'\n" + await reader.ReadToEndAsync() +
                "\n'@; [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes([ZasLauncherGUI.Utility.KaratWindowLayoutNative]::Apply($false)))";
            // Keep prlctl's command line below the guest transport limit.
            using var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, true))
            {
                var bytes = Encoding.UTF8.GetBytes(script);
                gzip.Write(bytes, 0, bytes.Length);
            }
            var bootstrap = "$m=New-Object IO.MemoryStream(,[Convert]::FromBase64String('" +
                Convert.ToBase64String(compressed.ToArray()) +
                "')); $g=New-Object IO.Compression.GZipStream($m,[IO.Compression.CompressionMode]::Decompress); " +
                "$r=New-Object IO.StreamReader($g); try { Invoke-Expression ($r.ReadToEnd()) } finally { $r.Dispose(); $g.Dispose(); $m.Dispose() }";
            var report = new StringBuilder();
            foreach (var vm in data.RootElement.EnumerateArray())
            {
                if (!vm.TryGetProperty("State", out var state) || state.GetString() != "running" ||
                    !vm.TryGetProperty("OS", out var os) || !(os.GetString()?.StartsWith("win", StringComparison.OrdinalIgnoreCase) ?? false)) continue;
                var id = vm.GetProperty("ID").GetString()!;
                var name = vm.GetProperty("Name").GetString();
                try
                {
                    var result = await RunAsync(executable, "exec", id, "--current-user", "powershell.exe",
                        "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command", bootstrap);
                    // ASCII transport avoids the Windows console code page corrupting Czech text.
                    report.AppendLine(name + ": " + Encoding.UTF8.GetString(Convert.FromBase64String(result.Trim())));
                }
                catch (Exception ex) { report.AppendLine(name + ": " + ex.Message); }
            }
            return report.Length == 0 ? "Nebyla nalezena žádná běžící Windows VM v Parallels." : report.ToString().Trim();
        }
        finally { Volatile.Write(ref _running, 0); }
    }

    private static async Task<string> RunAsync(string executable, params string[] arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) throw new IOException("Nepodařilo se spustit ovládání oken.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            throw new TimeoutException("Ovládání oken neodpovědělo do 30 sekund.");
        }
        var result = await output;
        var failure = await error;
        if (process.ExitCode != 0) throw new IOException("Uspořádání oken selhalo: " + failure.Trim());
        return result;
    }
}
