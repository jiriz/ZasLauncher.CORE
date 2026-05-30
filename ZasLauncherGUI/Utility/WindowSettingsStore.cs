using System;
using System.IO;
using System.Text.Json;

namespace ZasLauncherGUI.Utility;

public static class WindowSettingsStore
{
    private static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ZasLauncher",
            "window.json");

    public static WindowSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new WindowSettings();

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<WindowSettings>(json) ?? new WindowSettings();
        }
        catch
        {
            return new WindowSettings();
        }
    }

    public static void Save(WindowSettings settings)
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(FilePath, json);
    }
}