namespace ZasLauncherGUI.Utility;
// Headless tests must not read or overwrite the user's window settings.
public static class WindowSettingsStore
{
    public static WindowSettings Load() => new();
    public static void Save(WindowSettings settings) { }
}
