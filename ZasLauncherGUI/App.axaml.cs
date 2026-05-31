using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.Collections.Generic;
using System.Management;
using System.Threading.Tasks;
using System.Xml;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using ZasLauncherGUI.Class;
using ZasLauncherGUI.Utility;
using ZASutility.Standard;

namespace ZasLauncherGUI;

public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private RdpManagerWindow? _rdpWindow;

    public override void Initialize()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _ = CreateTrayIconAsync();

        base.OnFrameworkInitializationCompleted();
    }

    private async Task CreateTrayIconAsync()
    {
        var menu = await BuildMenuAsync();

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

    private async Task ReloadMenuAsync()
    {
        try
        {
            var menu = await BuildMenuAsync();

            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

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
        catch (Exception ex)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(
                "Chyba",
                "Nepodařilo se znovu načíst data.\n\n" + ex,
                ButtonEnum.Ok,
                Icon.Error);

            await box.ShowAsync();
        }
    }
    private async Task<NativeMenu> BuildMenuAsync()
    {
        var user = Environment.UserName;

        List<string> list = await Utility.Utils.GetXmlFromISK(
            "https://iserver.zasgroup.cz/zas-service/get-data-zas-launcher" +
            "?&token=BB0489CE-4C13-4E1E-897B-DEC4F706E5E4" +
            "&user=" + Uri.EscapeDataString(user)
        );

        var menu = new NativeMenu();
        MyNativeMenuItem? folder = null;

        foreach (string line in list)
        {
            if (line.StartsWith("#PARAMETERS_"))
                break;

            if (string.IsNullOrEmpty(line))
                continue;

            if (line.StartsWith("#"))
                continue;

            if (line == "--")
            {
                if (folder?.Menu != null)
                    folder.Menu.Items.Add(new NativeMenuItemSeparator());
                else
                    menu.Items.Add(new NativeMenuItemSeparator());

                continue;
            }

            if (line == "root")
            {
                folder = null;
                continue;
            }

            if (!line.Contains("|"))
            {
                folder = CreateMenu(line, string.Empty, menu, null);
                continue;
            }

            var parts = line.Split("|");

            string name = parts[0];
            string path = parts.Length > 1 ? parts[1] : string.Empty;
            string param = parts.Length > 2 ? parts[2] : string.Empty;

            CreateMenu(name, path + "|" + param, menu, folder);
        }

        menu.Items.Add(new NativeMenuItemSeparator());

        menu.Items.Add(CreateMenuItem(null, "Ukončit všechny IS Karat", async () =>
        {
            await Utils.KillProcessesAsync("ISKarat.Loader.Win");
            await Utils.KillProcessInAllWindowsVMs("ISKarat.Loader.Win");
        }, "exit.png", string.Empty));

        menu.Items.Add(new NativeMenuItemSeparator());

        menu.Items.Add(CreateMenuItem(null, "PIN pro docházkový terminál", async () =>
        {
            try
            {
                DateTime od = MyUtility.StringToDateTime(DateTime.Now.ToString("dd.MM.yyyy HH:00"));
                DateTime doCas = od.AddHours(1).AddMinutes(-1);
                string pin = PinHelper.MakePin();

                var box = MessageBoxManager.GetMessageBoxStandard(
                    "PIN pro docházkový terminál",
                    "PIN: " + pin + Environment.NewLine +
                    "Platnost: " + od.ToString("dd.MM.yyyy") +
                    ", od: " + od.ToString("HH:mm") +
                    " do: " + doCas.ToString("HH:mm"),
                    ButtonEnum.Ok,
                    Icon.Info);

                await box.ShowAsync();
            }
            catch (ManagementException ex)
            {
                var box = MessageBoxManager.GetMessageBoxStandard(
                    "PIN pro docházkový terminál",
                    ex.ToString(),
                    ButtonEnum.Ok,
                    Icon.Error);

                await box.ShowAsync();
            }
        }, "hammer.ico", string.Empty));

        menu.Items.Add(new NativeMenuItemSeparator());

        menu.Items.Add(CreateMenuItem(null, "Nastavení", async () =>
        {
            Console.WriteLine("Nastavení");
        }, "settings.png", string.Empty));

        menu.Items.Add(CreateMenuItem(null, "Načti data", async () =>
        {
            await ReloadMenuAsync();
        }, "reload.ico", string.Empty));

        menu.Items.Add(CreateMenuItem(null, "O programu", async () =>
        {
            var box = MessageBoxManager.GetMessageBoxStandard(
                "O programu",
                "HwID: " + Utils.GetDeviceId(),
                ButtonEnum.Ok,
                Icon.Info);

            await box.ShowAsync();
        }, "info.png", string.Empty));

        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(CreateMenuItem(null, "Ukončit", Exit, "exit.png", string.Empty));

        return menu;
    }

    private MyNativeMenuItem CreateMenu(
        string name,
        string path,
        NativeMenu menu,
        MyNativeMenuItem? folder = null)
    {
        var item = CreateMenuItem(folder, name, async () =>
        {
            if (!string.IsNullOrEmpty(path))
                await MyNativeMenuItemClick(name, path, KeyboardUtils.IsOnlyShiftPressed());
        }, string.Empty, path);

        if (folder != null)
        {
            folder.Menu ??= new NativeMenu();
            folder.Menu.Items.Add(item);
        }
        else
        {
            menu.Items.Add(item);
        }

        return item;
    }

    private static MyNativeMenuItem CreateMenuItem(
        MyNativeMenuItem? parent,
        string title,
        Func<Task> action,
        string icon,
        string path)
    {
        if (string.IsNullOrEmpty(icon))
        {
            string text = (path + title).ToLowerInvariant();

            if (path.ToLowerInvariant().Contains("#rdp_id") || title.ToLowerInvariant().Contains("rdp"))
                icon = "rdp.ico";
            else if (text.Contains("aliteo"))
                icon = "aliteo.ico";
            else if (title.ToLowerInvariant().Contains("isk") || path.ToLowerInvariant().Contains("ISKarat.Loader.Win.exe".ToLowerInvariant()))
                icon = "karat.ico";
            else if (text.Contains("zshop") || text.Contains("i_web"))
                icon = "zasgroup-web.ico";
            else if (title.ToLowerInvariant().Contains("configurator"))
                icon = "hammer.ico";
            else if (text.Contains("teams"))
                icon = "teams.ico";
            else if (text.Contains("dokumentace") || text.Contains("rest-api-docs"))
                icon = "help.ico";
            else if (text.Contains("restapi") || text.Contains("rest-api"))
                icon = "api.ico";
            else if (text.Contains("google keep"))
                icon = "google_keep.ico";
            else if (text.Contains("http"))
                icon = "html.ico";
        }

        Bitmap? bitmap = null;

        if (icon == "none")
        {
            bitmap = null;
        }
        else if (parent?.Icon is Bitmap parentBitmap)
        {
            bitmap = parentBitmap;
        }
        else if (!string.IsNullOrEmpty(icon))
        {
            bitmap = new Bitmap(
                AssetLoader.Open(new Uri("avares://ZasLauncherGUI/Assets/" + icon))
            );
        }
        else
        {
            bitmap = new Bitmap(
                AssetLoader.Open(new Uri("avares://ZasLauncherGUI/Assets/zasgroup.ico"))
            );
        }

        var item = new MyNativeMenuItem
        {
            Header = title,
            Tag = path,
            Icon = bitmap
        };

        item.Click += async (_, _) =>
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await action();
            });
        };

        return item;
    }

    private Task Exit()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();

        return Task.CompletedTask;
    }

    private async Task MyNativeMenuItemClick(string name, string path, bool isOnlyShiftPressed)
    {
        try
        {
            path = path.Replace("#SYS;", "");
            string args = string.Empty;
            if (path.Contains("|"))
                args = path.MySplit("|")[1];
            if (isOnlyShiftPressed)
            {
                string mess = path;
                if (mess.Contains("#ID=") && !string.IsNullOrEmpty(args))
                {
                    string id = MyStringExtensions.GetDirective(mess, "ID");

                    mess += Environment.NewLine + Environment.NewLine +
                            "  #ID_" + id + "_BEGIN;" + Environment.NewLine +
                            "    #ID_" + id + "_PARAMS_BEGIN;" + Environment.NewLine +
                            "      " + args.Replace("#ID=" + id + ";", "") + Environment.NewLine +
                            "    #ID_" + id + "_PARAMS_END;" + Environment.NewLine +
                            "  #ID_" + id + "_END;";
                }

                var box = MessageBoxManager.GetMessageBoxStandard(
                    "Parametry připojení",
                    mess,
                    ButtonEnum.Ok,
                    Icon.Info);

                await box.ShowAsync();
            }
            else
            {
                if (path.Contains("#RDP_ID"))
                {
                    string rdpId = MyStringExtensions.GetDirective(path, "RDP_ID");

                    if (string.IsNullOrEmpty(rdpId))
                    {
                        var box = MessageBoxManager.GetMessageBoxStandard(
                            "Chyba", "Neplatné RDP ID!", ButtonEnum.Ok, Icon.Error);
                        await box.ShowAsync();
                        return;
                    }

                    string? rdpParams = null;
                    try
                    {
                        rdpParams = await Utils.GetRdpParamsAsync(rdpId);
                    }
                    catch (Exception ex)
                    {
                        var box = MessageBoxManager.GetMessageBoxStandard(
                            "Chyba komunikace", ex.Message, ButtonEnum.Ok, Icon.Error);
                        await box.ShowAsync();
                        return;
                    }

                    if (string.IsNullOrEmpty(rdpParams) || !rdpParams.Contains("RDP_NAME"))
                    {
                        var box = MessageBoxManager.GetMessageBoxStandard(
                            "Chyba",
                            "Nepodařilo se načíst data pro vybrané připojení!",
                            ButtonEnum.Ok, Icon.Error);
                        await box.ShowAsync();
                        return;
                    }

                    string rdpName   = MyStringExtensions.GetDirective(rdpParams, "RDP_NAME");
                    string rdpServer = MyStringExtensions.GetDirective(rdpParams, "RDP_SERVER");
                    string rdpUser   = MyStringExtensions.GetDirective(rdpParams, "RDP_USER_NAME");
                    string rdpPass   = MyStringExtensions.GetDirective(rdpParams, "RDP_PASSWORD");

                    if (rdpPass.StartsWith("!enc:!") && rdpPass.Length > 6)
                        rdpPass = MyCryptography.EncodeZasPassword(rdpPass);

                    int rdpPort = MyUtility.StringToInt(MyStringExtensions.GetDirective(rdpParams, "RDP_PORT"));
                    var rdpEndpoint = RdpEndpoint.Normalize(rdpServer, rdpPort);

                    var tabName = string.IsNullOrEmpty(rdpName) ? name : rdpName;

                    OpenRdpTab(tabName, rdpEndpoint.Host, rdpEndpoint.Port, rdpUser, rdpPass);
                }
                else
                {
                    await ProcessLauncher.StartAsync(
                        path,
                        args,
                        string.Empty,
                        isOnlyShiftPressed);
                }
            }
        }
        catch (Exception ex)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(
                "Chyba",
                ex.ToString(),
                ButtonEnum.Ok,
                Icon.Error);

            await box.ShowAsync();
        }
    }
    
    private void OpenRdpTab(string name, string host, int port, string username, string password)
    {
        if (_rdpWindow == null)
        {
            MacDockIcon.SetVisible(true);
            _rdpWindow = new RdpManagerWindow();
            _rdpWindow.Closed += (_, _) =>
            {
                _rdpWindow = null;
                MacDockIcon.SetVisible(false);
            };
        }

        _rdpWindow.AddRdpTab(name, host, port, username, password);
        MacDockIcon.SetVisible(true);
        _rdpWindow.Show();
        _rdpWindow.Activate();
    }
}
