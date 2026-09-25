using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Styling;
using Avalonia.Layout;
using Avalonia.Media;
using ZasLauncherGUI.Class;

namespace ZasLauncherGUI.Rdp;

internal static class RdpTabHeader
{
    public const double TabWidth = 240;

    private static ControlTheme CloseButtonTheme()
    {
        var theme = new ControlTheme(typeof(Button));
        theme.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent));
        theme.Setters.Add(new Setter(TemplatedControl.TemplateProperty,
            new FuncControlTemplate<Button>((button, _) =>
            {
                var background = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Child = new Avalonia.Controls.Shapes.Path
                    {
                        Data = Geometry.Parse("M 1,1 L 9,9 M 9,1 L 1,9"),
                        Stroke = Brushes.Gray, StrokeThickness = 1.5,
                        Width = 10, Height = 10,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                background.Bind(Border.BackgroundProperty, button.GetObservable(TemplatedControl.BackgroundProperty));
                return background;
            })));
        theme.Children.Add(new Style(x => x.Nesting().Class(":pointerover"))
        { Setters = { new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.FromArgb(28,128,128,128))) } });
        theme.Children.Add(new Style(x => x.Nesting().Class(":pressed"))
        { Setters = { new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.FromArgb(50,128,128,128))) } });
        theme.Children.Add(new Style(x => x.Nesting().Class(":focus-visible"))
        { Setters = { new Setter(TemplatedControl.BackgroundProperty, new SolidColorBrush(Color.FromArgb(40,80,140,200))) } });
        return theme;
    }

    public static TabItem Create(string name, string title, Control content, Func<TabItem, Task> close)
    {
        var label = new TextBlock
        {
            Text = name, TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center
        };
        var closeButton = new Button
        {
            Width = 24, Height = 24, Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Theme = CloseButtonTheme()
        };
        ToolTip.SetTip(closeButton, "Zavřít připojení");
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,24"), ColumnSpacing = 8,
            Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        header.Children.Add(label); Grid.SetColumn(closeButton, 1); header.Children.Add(closeButton);
        var tab = new TabItem
        {
            Header = header, Content = content, Tag = title,
            Width = TabWidth, MinWidth = TabWidth, MaxWidth = TabWidth,
            Padding = new Thickness(12, 8), HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        ToolTip.SetTip(header, title);
        if (content is RdpSessionControl session) header.ContextMenu = session.CreateContextMenu(header);
        closeButton.Click += async (_, e) =>
        {
            e.Handled = true;
            closeButton.IsEnabled = false;
            await close(tab);
        };
        return tab;
    }
}
