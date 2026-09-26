using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using ZasLauncherGUI.Class;
using ZasLauncherGUI.Rdp;

// Shared window XAML resolves its production icon from the app assembly.
System.Reflection.Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "ZasLauncherGUI.dll"));
AppContext.SetSwitch("ZasLauncher.DisableClipboardSync", true);
ClipboardContract.Run();
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
await using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
await session.Dispatch(async () =>
{
    int port = int.Parse(Environment.GetEnvironmentVariable("ZAS_RDP_TEST_PORT")!);
    if(Environment.GetEnvironmentVariable("ZAS_RDP_CLIPBOARD_PEER")=="1")
    {
        var clipboardWindow=new Window();clipboardWindow.Show();
        try {await ClipboardContract.RunWireAsync(clipboardWindow,port);return 0;}
        finally {clipboardWindow.Close();}
    }
    var a = new RdpSessionControl("127.0.0.1", port, "test", "", "Local A");
    var b = new RdpSessionControl("127.0.0.1", port, "test", "", "Local B");
    var tabs = new TabControl();
    var tabA = RdpTabHeader.Create("Axial", "Axial – localhost", a, async _ => await a.CloseAsync());
    var tabB = RdpTabHeader.Create("IPM - Lösungen aus Stahl s.r.o. - karat.server", "IPM - Lösungen aus Stahl s.r.o. - karat.server", b, async _ => await b.CloseAsync());
    tabs.Items.Add(tabA); tabs.Items.Add(tabB);
    var window = new Window { Width = 1200, Height = 800, Content = tabs };
    try
    {
        window.Show(); await ClipboardContract.RunSyncAsync(window); tabs.SelectedItem = tabA;
        await WaitImage(a);
        tabs.SelectedItem = tabB;
        await WaitImage(b);
        tabs.SelectedItem = tabA;
        await Task.Delay(150);
        Require(a.GetVisualDescendants().OfType<Image>().Any(i => i.Source is WriteableBitmap), "A lost its framebuffer on tab switch");
        window.MouseMove(new Point(150,200));
        window.MouseDown(new Point(150,200), MouseButton.Left);
        window.MouseUp(new Point(150,200), MouseButton.Left);
        window.KeyPressQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
        // Switch while Ctrl is down: LostFocus/Unloaded must release held keys.
        tabs.SelectedItem = tabB;
        window.KeyReleaseQwerty(PhysicalKey.ControlLeft, RawInputModifiers.None);
        await Task.Delay(150);
        Require(tabA.Bounds.Width == RdpTabHeader.TabWidth && tabB.Bounds.Width == RdpTabHeader.TabWidth, "Tab widths differ");
        Require(((Grid)tabB.Header!).Children.OfType<TextBlock>().Single().TextTrimming == Avalonia.Media.TextTrimming.CharacterEllipsis, "Missing ellipsis");
        Require(Equals(ToolTip.GetTip((Control)tabB.Header!), tabB.Tag), "Missing full-title tooltip");
        Require(!b.GetVisualDescendants().OfType<Button>().Any(), "Toolbar buttons still present");
        window.Width=970;window.Height=660;await Task.Delay(100);
        tabs.SelectedItem=tabA;await Task.Delay(80);tabs.SelectedItem=tabB;await Task.Delay(80);
        var image=b.GetVisualDescendants().OfType<Image>().Single();
        image.Margin=new Thickness(30,20,70,50);await Task.Delay(50);
        var pixelSize=((WriteableBitmap)image.Source!).PixelSize;
        var point=image.TranslatePoint(new Point(image.Bounds.Width*.25,image.Bounds.Height*.75),window)!.Value;
        window.MouseMove(point);
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        int x=(int)typeof(RdpSessionControl).GetField("_mouseX",flags)!.GetValue(b)!;
        int y=(int)typeof(RdpSessionControl).GetField("_mouseY",flags)!.GetValue(b)!;
        Require(Math.Abs(x-pixelSize.Width*.25)<=2 && Math.Abs(y-pixelSize.Height*.75)<=2,"pointer mapping after resize/tab return");
        // Invoke A's menu while B is selected: the action must stay bound to A.
        var disconnectA = ((Control)tabA.Header!).ContextMenu!.Items.OfType<MenuItem>().Single(x => Equals(x.Header, "Odpojit"));
        disconnectA.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Task.Delay(150);
        Require(b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Připojeno"), "Context menu targeted the selected tab instead");
        using var screenshot = window.CaptureRenderedFrame();
        Require(screenshot != null, "No rendered UI frame");
        screenshot!.Save(Path.Combine(Path.GetTempPath(), "zas-rdp-ui.png"));
        await a.CloseAsync(); tabs.Items.Remove(tabA);
        await Task.Delay(150);
        Require(b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Připojeno"), "B disconnected when A closed");
        var header = (Control)tabB.Header!;
        var reconnect = header.ContextMenu!.Items.OfType<MenuItem>().Single(x => Equals(x.Header, "Připojit znovu"));
        reconnect.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Task.Delay(250); await WaitImage(b);
        Require(RdpKeyboard.ScanCode(PhysicalKey.ControlRight) == 0x11d, "Extended scancode");
        Require(RdpKeyboard.ScanCode(PhysicalKey.Delete) == 0x153, "Delete scancode");
        await Task.WhenAll(b.CloseAsync(), b.CloseAsync());
        Console.WriteLine("PASS: Avalonia tab rendering, switch, mouse/keyboard dispatch, independent close, reconnect, idempotent disposal");
        var manager = new ZasLauncherGUI.RdpManagerWindow();
        bool managerClosed=false;manager.Closed+=(_,_)=>managerClosed=true;
        manager.Show(); manager.AddRdpTab("  Axial        ","127.0.0.1",port,"test","");
        Require(manager.Title=="Axial – 127.0.0.1:"+port+" (test)","title retains padding");
        var managerTabs=manager.FindControl<TabControl>("Tabs")!;
        var only=(TabItem)managerTabs.Items[0]!;
        ((Grid)only.Header!).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (int i=0;i<100 && !managerClosed;i++) await Task.Delay(20);
        Require(managerClosed,"empty RDP Manager remained open");
        Console.WriteLine("PASS: trimmed title and automatic last-tab manager close");
        return 0;
    }
    finally { await a.CloseAsync(); await b.CloseAsync(); window.Close(); }
}, timeout.Token);

static void Require(bool result, string message) { if (!result) throw new Exception(message); }
static async Task WaitImage(RdpSessionControl control)
{
    for (int i=0; i<100; i++)
    {
        if (control.GetVisualDescendants().OfType<Image>().Any(x => x.Source is WriteableBitmap)) return;
        await Task.Delay(50);
    }
    throw new Exception("No RDP image in UI: " + string.Join(";", control.GetVisualDescendants().OfType<TextBlock>().Select(x=>x.Text)));
}
public class TestApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
