using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ZasLauncherGUI.Class;
using ZasLauncherGUI.Utility;

namespace ZasLauncherGUI;

public partial class RdpManagerWindow : Window
{
    private const double DefaultWidth = 1200;
    private const double DefaultHeight = 800;
    private const double MinimumWidth = 900;
    private const double MinimumHeight = 600;

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

        Closing += (_, _) =>
        {
            foreach (var item in Tabs.Items)
            {
                if (item is TabItem tab && FindRdpSessionControl(tab.Content as Control) is { } ctrl)
                    ctrl.Disconnect();
            }

            WindowSettingsStore.Save(new WindowSettings
            {
                Width = Math.Max(Bounds.Width, MinimumWidth),
                Height = Math.Max(Bounds.Height, MinimumHeight),
                X = Position.X,
                Y = Position.Y
            });
        };
    }

    public void AddRdpTab(string name, string host, string username, string password)
    {
        var tabTitle = $"{name} – {host} ({username})";
        Control content;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            content = new RdpSessionControl(host, username, password, tabTitle);
        }
        else
        {
            content = MakeErrorPanel(host, username,
                "Embedded RDP je momentálně podporováno pouze na macOS.");
        }

        var closeButton = new Button
        {
            Content = "×",
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = name,
                    VerticalAlignment = VerticalAlignment.Center
                },
                closeButton
            }
        };

        var tab = new TabItem
        {
            Header = headerPanel,
            Content = content,
            Tag = tabTitle          // uložíme plný titulek do Tag
        };

        closeButton.Click += (_, e) =>
        {
            e.Handled = true;

            if (FindRdpSessionControl(tab.Content as Control) is { } ctrl)
                ctrl.Disconnect();

            Tabs.Items.Remove(tab);

            if (Tabs.Items.Count == 0)
                Title = "RDP Manager";
        };

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
