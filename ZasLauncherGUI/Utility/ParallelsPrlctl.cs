using System;
using System.IO;
using System.Linq;

namespace ZasLauncherGUI.Utility;

internal static class ParallelsPrlctl
{
    private static readonly string[] KnownExecutablePaths =
    [
        "/Applications/Parallels Desktop.app/Contents/MacOS/prlctl",
        "/usr/local/bin/prlctl",
        "/opt/homebrew/bin/prlctl"
    ];

    public static string ExecutablePath =>
        TryFindExecutablePath()
        ?? throw new FileNotFoundException(
            "Nepodařilo se najít Parallels nástroj prlctl. " +
            "Ověřte instalaci Parallels Desktop nebo cestu /Applications/Parallels Desktop.app/Contents/MacOS/prlctl.");

    private static string? TryFindExecutablePath()
    {
        foreach (var path in KnownExecutablePaths)
        {
            if (File.Exists(path))
                return path;
        }

        return Environment
            .GetEnvironmentVariable("PATH")
            ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, "prlctl"))
            .FirstOrDefault(File.Exists);
    }
}
