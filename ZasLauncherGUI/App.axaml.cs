using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ZasLauncherGUI.Class;

namespace ZasLauncherGUI;

public partial class App : Application
{
    private TrayIcon? _trayIcon;
    public override void Initialize()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _ = CreateTrayIconAsync();
        base.OnFrameworkInitializationCompleted();
    }

    private async Task CreateTrayIconAsync()
    {
        var user = Environment.UserName;
        List<string> list = await Utility.Utils.GetXmlFromISK(
            "https://iserver.zasgroup.cz/zas-service/get-data-zas-launcher" +
            "?&token=BB0489CE-4C13-4E1E-897B-DEC4F706E5E4" +
            "&user=" + Uri.EscapeDataString(user)
        ); 
        var menu = new NativeMenu();
        var folder = new MyNativeMenuItem();

        foreach (string line in list)
        {
            if (line.StartsWith("#PARAMETERS_"))
                break;
            if (line != String.Empty)
            {
                MyNativeMenuItem pomItem = null;
                if (line.StartsWith("#") || String.IsNullOrEmpty(line))
                    continue;
                if (line == "--")
                {
                    if (folder!=null && folder.Menu!=null)
                        folder.Menu.Items.Add(new NativeMenuItemSeparator());
                    else
                        menu.Items.Add(new NativeMenuItemSeparator());
                }
                else if (line == "root")
                {
                    folder = null;
                }
                else if (!line.Contains("|")) //folder
                {
                    folder = CreateMenu(line, String.Empty, menu, null);
                }
                else
                {
                    string param = line.Split("|").Length == 3 ? line.Split("|")[2] : String.Empty;
                    pomItem = CreateMenu(line.Split("|")[0], line.Split("|")[1] + "|" + param, menu, folder);
                }
            }
        }
        
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(CreateMenuItem("Nastavení", () =>
        {
            Console.WriteLine("Nastavení");
        }, "settings.png", String.Empty));

        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(CreateMenuItem("Ukončit", Exit, "exit.png", String.Empty));
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(
                AssetLoader.Open(new Uri("avares://ZasLauncherGUI/Assets/zasgroup.ico"))
            ),
            ToolTipText = "ZasLauncher",
            Menu = menu,
            IsVisible = true
        };
    }

    private MyNativeMenuItem CreateMenu(string Name, string Path, NativeMenu menu, MyNativeMenuItem? folder = null)
    {
        var item = CreateMenuItem(Name, () =>
        {
            Console.WriteLine("Připojit");
        }, String.Empty, Path);
        
        if (folder != null)
        {
            if(folder.Menu == null)
                folder.Menu = new NativeMenu();
            folder.Menu.Items.Add(item);
        }
        else if (menu != null)
        {
            menu.Items.Add(item);
        }
        return item;
    }
    
    private static MyNativeMenuItem CreateMenuItem(string title, Action action, string icon, string path)
    {
        if (String.IsNullOrEmpty(icon))
        {
            string exe = path.Split("|")[0];
            string text = (path + title).ToLower();
            if (path.ToLowerInvariant().Contains("#rdp_id") || title.ToLowerInvariant().Contains("rdp"))
                icon = "rdp.ico";
            if (text.Contains("aliteo"))
                icon = "aliteo.ico";
            if (text.Contains("zshop") || text.Contains("i_web"))
                icon = "zasgroup.ico";
            if (title.ToLowerInvariant().Contains("http"))
                icon = "html.ico";
            if (title.ToLowerInvariant().Contains("configurator"))
                icon = "hammer.ico";
            if (title.ToLowerInvariant().Contains("isk"))
                icon = "karat.ico";
            if (text.Contains("teams"))
                icon = "teams.ico";
            if (text.Contains("dokumentace") || text.Contains("rest-api-docs"))
                icon = "help.ico";
            if (text.Contains("restapi") || text.Contains("rest-api"))
                icon = "api.ico";
            if (text.Contains("google keep"))
                icon = "google_keep.ico";
            if (text.Contains("http"))
                icon = "html.ico";
        }

        var item = new MyNativeMenuItem
        {
            Header = title,
            Tag = path,
            Icon = icon != String.Empty
                ? new Bitmap(
                    AssetLoader.Open(new Uri("avares://ZasLauncherGUI/Assets/" + icon))
                )
                : null,
        };
        item.Click += (_, _) => action();
        return item;
    }

    private void Exit()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}