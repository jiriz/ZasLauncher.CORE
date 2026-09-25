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

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
await using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
await session.Dispatch(async () =>
{
    int port = int.Parse(Environment.GetEnvironmentVariable("ZAS_RDP_TEST_PORT")!);
    var a = new RdpSessionControl("127.0.0.1", port, "test", "", "Local A");
    var b = new RdpSessionControl("127.0.0.1", port, "test", "", "Local B");
    var tabs = new TabControl();
    var tabA = new TabItem { Header = "Test A", Content = a };
    var tabB = new TabItem { Header = "Test B", Content = b };
    tabs.Items.Add(tabA); tabs.Items.Add(tabB);
    var window = new Window { Width = 1200, Height = 800, Content = tabs };
    try
    {
        window.Show(); tabs.SelectedItem = tabA;
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
        using var screenshot = window.CaptureRenderedFrame();
        Require(screenshot != null, "No rendered UI frame");
        screenshot!.Save(Path.Combine(Path.GetTempPath(), "zas-rdp-ui.png"));
        await a.CloseAsync(); tabs.Items.Remove(tabA);
        await Task.Delay(150);
        Require(b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Připojeno"), "B disconnected when A closed");
        var reconnect = b.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Připojit znovu"));
        reconnect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(250); await WaitImage(b);
        Require(RdpKeyboard.ScanCode(PhysicalKey.ControlRight) == 0x11d, "Extended scancode");
        Require(RdpKeyboard.ScanCode(PhysicalKey.Delete) == 0x153, "Delete scancode");
        await Task.WhenAll(b.CloseAsync(), b.CloseAsync());
        Console.WriteLine("PASS: Avalonia tab rendering, switch, mouse/keyboard dispatch, independent close, reconnect, idempotent disposal");
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
