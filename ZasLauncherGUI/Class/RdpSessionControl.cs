using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ZasLauncherGUI.Class;

/// <summary>
/// Spouští RDP session pomocí sdl-freerdp (SDL/Metal, bez XQuartz).
/// </summary>
[SupportedOSPlatform("macos")]
public class RdpSessionControl : UserControl
{
    // sdl-freerdp creates a high-DPI SDL window on macOS. These pixel dimensions
    // produce a large but still screen-fitting logical window on Retina displays.
    private const int InitialWidth = 3200;
    private const int InitialHeight = 1900;
    private const int RemoteScalePercent = 180;

    private readonly string _host;
    private readonly string _username;
    private readonly string _password;
    private readonly string _windowTitle;
    private readonly string _instanceId = Guid.NewGuid().ToString("N")[..8];

    private Process? _rdpProcess;
    private string? _appPath;
    private string? _appExecutablePath;
    private string? _processDisplayName;
    private TextBlock? _statusText;
    private bool _hasAutoConnected;
    private bool _isDisconnecting;

    public RdpSessionControl(string host, string username, string password, string windowTitle)
    {
        _host = host;
        _username = username;
        _password = password;
        _windowTitle = windowTitle;
        BuildUI();
        Loaded += (_, _) =>
        {
            if (_hasAutoConnected)
                return;

            _hasAutoConnected = true;
            Connect();
        };
    }

    private void BuildUI()
    {
        var connectBtn = new Button
        {
            Content = "Připojit znovu",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        };
        connectBtn.Click += (_, _) => Connect();

        var disconnectBtn = new Button
        {
            Content = "Odpojit",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };
        disconnectBtn.Click += (_, _) => Disconnect();

        var bringToFrontBtn = new Button
        {
            Content = "Přenést dopředu",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };
        bringToFrontBtn.Click += (_, _) => BringToFront();

        _statusText = new TextBlock
        {
            Text = "Spouštím připojení…",
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };

        Content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = $"Host: {_host}",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontWeight = FontWeight.Bold
                },
                new TextBlock
                {
                    Text = $"Uživatel: {_username}",
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                _statusText,
                connectBtn,
                disconnectBtn,
                bringToFrontBtn
            }
        };
    }

    private void Connect()
    {
        if (_rdpProcess is { HasExited: false })
        {
            Disconnect();
        }

        _isDisconnecting = false;

        string sdlBin;
        try
        {
            sdlBin = FindSdlFreeRdp();
        }
        catch (Exception ex)
        {
            SetStatus($"❌ {ex.Message}");
            return;
        }

        SetStatus($"Připojuji se k {_host}…");

        var processDisplayName = GetProcessDisplayName(_windowTitle);
        var appBundle = GetConnectionAppBundle(sdlBin, processDisplayName, _instanceId);
        _processDisplayName = appBundle.ProcessName;
        _appPath = appBundle.AppPath;
        _appExecutablePath = appBundle.ExecutablePath;

        var psi = CreateRdpProcessStartInfo(appBundle, appBundle.ProcessName);
        psi.Environment["SDL_APP_NAME"] = appBundle.ProcessName;

        psi.ArgumentList.Add($"/v:{_host}");
        psi.ArgumentList.Add($"/u:{_username}");
        psi.ArgumentList.Add($"/p:{_password}");
        psi.ArgumentList.Add("/cert:ignore");
        psi.ArgumentList.Add($"/t:{_windowTitle}");
        psi.ArgumentList.Add($"/w:{InitialWidth}");
        psi.ArgumentList.Add($"/h:{InitialHeight}");

        if (TryGetCenteredWindowPosition() is { } windowPosition)
            psi.ArgumentList.Add(windowPosition);

        psi.ArgumentList.Add("+dynamic-resolution");
        psi.ArgumentList.Add($"/scale:{RemoteScalePercent}");
        // psi.ArgumentList.Add($"/scale-desktop:{RemoteScalePercent}");
        // psi.ArgumentList.Add($"/scale-device:{RemoteScalePercent}");
        psi.ArgumentList.Add($"/wm-class:{MakeWindowClass(_processDisplayName)}");

        _rdpProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

        _rdpProcess.Exited += (_, _) =>
        {
            if (_isDisconnecting)
                return;

            var code = _rdpProcess?.ExitCode ?? -1;
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                SetStatus(code == 0 ? "✅ Odpojeno." : $"❌ Chyba (kód {code}). Klikněte pro opakování."));
        };

        _rdpProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                Console.WriteLine($"[sdl-freerdp] {e.Data}");
        };

        try
        {
            _rdpProcess.Start();
            _rdpProcess.BeginErrorReadLine();
            SetStatus($"✅ RDP okno spuštěno (PID {_rdpProcess.Id})");
        }
        catch (Exception ex)
        {
            SetStatus($"❌ Nepodařilo se spustit: {ex.Message}");
        }
    }

    private void SetStatus(string text)
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_statusText != null)
                _statusText.Text = text;
        });
    }

    public void Disconnect()
    {
        try
        {
            _isDisconnecting = true;

            if (!string.IsNullOrWhiteSpace(_appExecutablePath) ||
                !string.IsNullOrWhiteSpace(_appPath) ||
                !string.IsNullOrWhiteSpace(_processDisplayName))
            {
                KillConnectionApp(_appExecutablePath, _appPath, _processDisplayName);
            }

            if (_rdpProcess is { HasExited: false })
                _rdpProcess.Kill(entireProcessTree: true);

            _rdpProcess = null;
            SetStatus("✅ Odpojeno.");
        }
        catch (Exception ex)
        {
            SetStatus($"❌ Nepodařilo se odpojit: {ex.Message}");
        }
    }

    public void BringToFront()
    {
        try
        {
            if (!OperatingSystem.IsMacOS())
                return;

            if (!string.IsNullOrWhiteSpace(_processDisplayName))
            {
                using var activateProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    ArgumentList =
                    {
                        "-e",
                        $"tell application \"{EscapeAppleScriptString(_processDisplayName)}\" to activate"
                    }
                });

                if (activateProcess != null && activateProcess.WaitForExit(1000) && activateProcess.ExitCode == 0)
                    return;
            }

            if (!string.IsNullOrWhiteSpace(_appPath))
            {
                using var openProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "/usr/bin/open",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    ArgumentList =
                    {
                        _appPath
                    }
                });
                openProcess?.WaitForExit(1000);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"❌ Nepodařilo se přenést dopředu: {ex.Message}");
        }
    }

    private static string FindSdlFreeRdp()
    {
        string[] candidates =
        [
            "/opt/homebrew/bin/sdl-freerdp",
            "/opt/homebrew/bin/sdl-freerdp3",
            "/usr/local/bin/sdl-freerdp",
            "/usr/local/bin/sdl-freerdp3",
        ];

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        throw new InvalidOperationException(
            "sdl-freerdp nebylo nalezeno.\nNainstalujte: brew install freerdp");
    }

    private static string GetProcessDisplayName(string title) =>
        string.IsNullOrWhiteSpace(title) ? "RDP" : title.Trim();

    private string? TryGetCenteredWindowPosition()
    {
        var screens = TopLevel.GetTopLevel(this)?.Screens;
        var screen = screens?.ScreenFromVisual(this) ?? screens?.Primary;
        if (screen is null)
            return null;

        var workingArea = screen.WorkingArea;
        var x = workingArea.X + Math.Max(0, (workingArea.Width - InitialWidth) / 2);
        var y = workingArea.Y + Math.Max(0, (workingArea.Height - InitialHeight) / 2);
        return $"/window-position:{x}x{y}";
    }

    private static ConnectionAppBundle GetConnectionAppBundle(string sdlBin, string displayName, string instanceId)
    {
        try
        {
            var appsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ZasLauncher",
                "rdp-apps");

            var appName = MakeSafeFileName($"{displayName}-{instanceId}");
            var appDir = Path.Combine(appsDir, $"{appName}.app");
            var contentsDir = Path.Combine(appDir, "Contents");
            var macOsDir = Path.Combine(contentsDir, "MacOS");
            Directory.CreateDirectory(macOsDir);

            var executablePath = Path.Combine(macOsDir, appName);
            if (!File.Exists(executablePath))
                File.CreateSymbolicLink(executablePath, sdlBin);

            File.WriteAllText(Path.Combine(contentsDir, "Info.plist"), CreateInfoPlist(appName, appName));
            return new ConnectionAppBundle(appDir, executablePath, appName);
        }
        catch
        {
            return new ConnectionAppBundle(
                sdlBin,
                sdlBin,
                Path.GetFileNameWithoutExtension(sdlBin),
                false);
        }
    }

    private static ProcessStartInfo CreateRdpProcessStartInfo(ConnectionAppBundle appBundle, string processDisplayName)
    {
        var psi = new ProcessStartInfo
        {
            FileName = appBundle.UseLaunchServices ? "/usr/bin/open" : appBundle.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardError = true,
        };

        if (appBundle.UseLaunchServices)
        {
            psi.ArgumentList.Add("-n");
            psi.ArgumentList.Add("-W");
            psi.ArgumentList.Add("--env");
            psi.ArgumentList.Add($"SDL_APP_NAME={processDisplayName}");
            psi.ArgumentList.Add(appBundle.AppPath);
            psi.ArgumentList.Add("--args");
        }

        return psi;
    }

    private static void KillConnectionApp(string? executablePath, string? appPath, string? processName)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(processName))
            {
                RunAndWait("/usr/bin/osascript", new[]
                {
                    "-e",
                    $"tell application \"{EscapeAppleScriptString(processName)}\" to quit"
                });

                RunAndWait("/usr/bin/killall", new[] { processName });

                RunAndWait("/usr/bin/pkill", new[]
                {
                    "-x",
                    processName
                });
            }

            var patterns = new List<string>();

            if (!string.IsNullOrWhiteSpace(executablePath))
                patterns.Add(executablePath);

            if (!string.IsNullOrWhiteSpace(appPath))
                patterns.Add(appPath);

            if (!string.IsNullOrWhiteSpace(processName))
                patterns.Add(processName);

            foreach (var pattern in patterns.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
            {
                RunAndWait("/usr/bin/pkill", new[]
                {
                    "-f",
                    pattern
                });
            }
        }
        catch
        {
            // Best effort only.
        }
    }

    private static void RunAndWait(string fileName, IEnumerable<string> arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            foreach (var argument in arguments)
                psi.ArgumentList.Add(argument);

            using var process = Process.Start(psi);
            process?.WaitForExit(1500);
        }
        catch
        {
            // Best effort only.
        }
    }

    private static string EscapeAppleScriptString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var c in value.Trim())
        {
            builder.Append(Array.IndexOf(invalid, c) >= 0 || c == ':' || char.IsControl(c) ? '_' : c);
        }

        var result = builder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(result))
            return "RDP";

        return result.Length <= 80 ? result : result[..80].Trim();
    }

    private static string CreateInfoPlist(string executableName, string displayName) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>CFBundleDevelopmentRegion</key>
            <string>en</string>
            <key>CFBundleDisplayName</key>
            <string>{EscapePlistString(displayName)}</string>
            <key>CFBundleExecutable</key>
            <string>{EscapePlistString(executableName)}</string>
            <key>CFBundleIdentifier</key>
            <string>cz.zasgroup.zaslauncher.rdp.{CreateBundleIdentifierSuffix(executableName)}</string>
            <key>CFBundleName</key>
            <string>{EscapePlistString(displayName)}</string>
            <key>CFBundlePackageType</key>
            <string>APPL</string>
            <key>CFBundleShortVersionString</key>
            <string>1.0</string>
            <key>CFBundleVersion</key>
            <string>1</string>
            <key>LSUIElement</key>
            <true/>
            <key>NSHighResolutionCapable</key>
            <true/>
        </dict>
        </plist>
        """;

    private static string EscapePlistString(string value) =>
        value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");

    private static string CreateBundleIdentifierSuffix(string value)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hashBytes, 0, 8).ToLowerInvariant();
    }

    private static string MakeWindowClass(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var c in value)
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '-');
        }

        var result = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "rdp" : result.ToLowerInvariant();
    }

    private sealed record ConnectionAppBundle(
        string AppPath,
        string ExecutablePath,
        string ProcessName,
        bool UseLaunchServices = true);
}
