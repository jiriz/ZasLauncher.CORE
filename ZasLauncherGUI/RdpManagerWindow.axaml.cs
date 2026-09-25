using System;
using System.Runtime.InteropServices;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ZasLauncherGUI.Class;
using ZasLauncherGUI.Rdp;
using ZasLauncherGUI.Utility;

namespace ZasLauncherGUI;

public partial class RdpManagerWindow : Window
{
    private const double DefaultWidth = 1200;
    private const double DefaultHeight = 800;
    private const double MinimumWidth = 900;
    private const double MinimumHeight = 600;

    private bool _closing, _canClose;

    public RdpManagerWindow()
    {
        InitializeComponent();
        var settings = WindowSettingsStore.Load();
        Width = GetUsableSize(settings.Width, DefaultWidth, MinimumWidth);
        Height = GetUsableSize(settings.Height, DefaultHeight, MinimumHeight);
        if (settings.X.HasValue && settings.Y.HasValue)
        {
            Position = new PixelPoint(
                (int)settings.X.Value,
                (int)settings.Y.Value);
        }

        // Aktualizuj titulek při přepnutí záložky
        Tabs.SelectionChanged += (_, _) =>
        {
            if (Tabs.SelectedItem is TabItem { Tag: string tabTitle })
                Title = tabTitle;
        };

        Closing += async (_, e) =>
        {
            if (_canClose) return;
            e.Cancel = true;
            if (_closing) return;
            await CloseSessionsAsync();
            Close();
        };
    }

    private Task? _shutdownTask;
    public Task CloseSessionsAsync() => _shutdownTask ??= CloseSessionsCoreAsync();
    private async Task CloseSessionsCoreAsync()
    {
        _closing = true;
        IsEnabled = false;
        var sessions = Tabs.Items.OfType<TabItem>()
            .Select(tab => FindRdpSessionControl(tab.Content as Control)).OfType<RdpSessionControl>();
        await Task.WhenAll(sessions.Select(session => session.CloseAsync()));
        WindowSettingsStore.Save(new WindowSettings
        {
            Width = Math.Max(Bounds.Width, MinimumWidth), Height = Math.Max(Bounds.Height, MinimumHeight),
            X = Position.X, Y = Position.Y
        });
        _canClose = true;
    }

    public void AddRdpTab(string name, string host, int port, string username, string password)
    {
        if (_closing) return;
        name = name.Trim();
        var displayEndpoint = RdpEndpoint.Format(host, port);
        var tabTitle = $"{name} – {displayEndpoint} ({username.Trim()})";
        Control content;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            content = new RdpSessionControl(host, port, username, password, tabTitle);
        }
        else
        {
            content = MakeErrorPanel(displayEndpoint, username,
                "Embedded RDP je momentálně podporováno pouze na macOS.");
        }

        var tab = RdpTabHeader.Create(name, tabTitle, content, async closedTab =>
        {
            if (FindRdpSessionControl(closedTab.Content as Control) is { } ctrl)
                await ctrl.CloseAsync();
            Tabs.Items.Remove(closedTab);
            if (Tabs.Items.Count == 0) Close();
        });

        Tabs.Items.Add(tab);
        Tabs.SelectedItem = tab;
        Title = tabTitle;           // okamžitě nastavíme titulek
    }

    private static RdpSessionControl? FindRdpSessionControl(Control? control)
    {
        if (control is RdpSessionControl rdp)
            return rdp;

        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is Control childControl && FindRdpSessionControl(childControl) is { } found)
                    return found;
            }
        }

        if (control is ContentControl contentControl)
            return FindRdpSessionControl(contentControl.Content as Control);

        return null;
    }

    private static double GetUsableSize(double value, double defaultValue, double minimum) =>
        double.IsFinite(value) && value >= minimum ? value : defaultValue;

    private static StackPanel MakeErrorPanel(string host, string username, string message) =>
        new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = $"Host: {host}", HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = $"Uživatel: {username}", HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock
                {
                    Text = message,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = Avalonia.Media.TextAlignment.Center
                }
            }
        };
}
