using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;

namespace ZasLauncherGUI.Utility;

public static class ProcessLauncher
{
    public static async Task StartAsync(
        string path,
        string args,
        string appParameters,
        bool shiftControlPressed)
    {
        var exe = path;
        var arguments = args;

        if (path.Contains("|"))
        {
            var parts = path.Split('|', 2);

            exe = parts[0];

            if (string.IsNullOrWhiteSpace(arguments) && parts.Length > 1)
                arguments = parts[1];
        }

        arguments = ModifyArgsByParameters(arguments, appParameters);

        if (shiftControlPressed)
            arguments = arguments.Replace("#DO_LOGIN;", "#.DO_LOGIN;");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await StartWindowsAsync(exe, arguments, appParameters);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && IsWindowsPath(exe))
        {
            await StartWindowsExeViaParallelsPrlctlAsync(exe, arguments);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            StartMac(exe, arguments);
        }
        else
        {
            StartLinux(exe, arguments);
        }
    }

    private static async Task StartWindowsAsync(
        string exe,
        string args,
        string appParameters)
    {
        using var process = new Process();

        process.StartInfo.FileName = exe;
        process.StartInfo.Arguments = args;
        process.StartInfo.UseShellExecute = true;
        process.StartInfo.CreateNoWindow = true;

        process.Start();

        while (string.IsNullOrEmpty(process.MainWindowTitle))
        {
            await Task.Delay(500);

            process.Refresh();

            if (process.HasExited)
                return;
        }

        if (process.WaitForInputIdle(15000))
        {
            if (appParameters.Contains("#MAXIMALIZED=1;"))
            {
                ShowWindow(process.MainWindowHandle, SW_MAXIMIZE);
            }
        }
    }

    private static async Task StartWindowsExeViaParallelsPrlctlAsync(
        string exe,
        string args)
    {
        var vmName = await FindFirstRunningWindowsVmAsync()
            ?? throw new Exception(
                "Nebyla nalezena žádná běžící Windows VM v Parallels.");

        var normalizedExe = exe.Replace('/', '\\');

        var lastSlash = normalizedExe.LastIndexOf('\\');

        if (lastSlash <= 0 || lastSlash >= normalizedExe.Length - 1)
            throw new Exception($"Neplatná cesta k Windows aplikaci: {exe}");

        var workingDirectory = normalizedExe[..lastSlash];
        var exeName = normalizedExe[(lastSlash + 1)..];

        var psi = new ProcessStartInfo
        {
            FileName = ParallelsPrlctl.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("exec");
        psi.ArgumentList.Add(vmName);
        psi.ArgumentList.Add("--current-user");
        psi.ArgumentList.Add("cmd.exe");
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add("start");
        psi.ArgumentList.Add("");
        psi.ArgumentList.Add("/d");
        psi.ArgumentList.Add(workingDirectory);
        psi.ArgumentList.Add(exeName);

        if (!string.IsNullOrWhiteSpace(args))
            psi.ArgumentList.Add(args);

        Process.Start(psi);
    }

    /// <summary>
    /// Vrátí název první běžící Windows VM v Parallels (dle výstupu prlctl list --json).
    /// </summary>
    private static async Task<string?> FindFirstRunningWindowsVmAsync()
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            FileName = ParallelsPrlctl.ExecutablePath
        };

        psi.ArgumentList.Add("list");
        psi.ArgumentList.Add("--json");

        using var process = Process.Start(psi);

        if (process == null)
            throw new Exception("Nepodařilo se spustit prlctl.");

        var json = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception(
                "Příkaz prlctl list --json selhal." +
                (string.IsNullOrWhiteSpace(error) ? string.Empty : "\n\n" + error.Trim()));
        }

        using var doc = JsonDocument.Parse(json);

        foreach (var vm in doc.RootElement.EnumerateArray())
        {
            var status = vm.TryGetProperty("status", out var s)
                ? s.GetString()
                : null;

            var osType = vm.TryGetProperty("os", out var o)
                ? o.GetString()
                : null;

            // Bereme první VM ve stavu "running", jejíž OS obsahuje "win"
            if (string.Equals(status, "running", StringComparison.OrdinalIgnoreCase) &&
                osType != null &&
                osType.Contains("win", StringComparison.OrdinalIgnoreCase))
            {
                return vm.TryGetProperty("name", out var n)
                    ? n.GetString()
                    : null;
            }
        }

        // Záloha: první running VM bez ohledu na OS
        foreach (var vm in doc.RootElement.EnumerateArray())
        {
            var status = vm.TryGetProperty("status", out var s)
                ? s.GetString()
                : null;

            if (string.Equals(status, "running", StringComparison.OrdinalIgnoreCase))
            {
                return vm.TryGetProperty("name", out var n)
                    ? n.GetString()
                    : null;
            }
        }

        return null;
    }

    private static void StartMac(
        string exeOrFile,
        string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "open",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-n");
        psi.ArgumentList.Add(exeOrFile);

        if (!string.IsNullOrWhiteSpace(args))
        {
            psi.ArgumentList.Add("--args");
            psi.ArgumentList.Add(args);
        }

        Process.Start(psi);
    }

    private static void StartLinux(
        string exeOrFile,
        string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exeOrFile,
            UseShellExecute = true,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(args))
            psi.Arguments = args;

        Process.Start(psi);
    }

    private static bool IsWindowsPath(string path)
    {
        return path.Length > 2 &&
               char.IsLetter(path[0]) &&
               path[1] == ':' &&
               path[2] == '\\';
    }

    private static string ModifyArgsByParameters(
        string args,
        string appParameters)
    {
        return args;
    }

    private const int SW_MAXIMIZE = 3;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(
        IntPtr hWnd,
        int nCmdShow);
}
