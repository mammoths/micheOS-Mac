using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Miche.Mac.Controls;

internal sealed class CalendarPaper : Control
{
    public override void Render(DrawingContext context)
    {
        var pen = new Pen(Brush.Parse("#15908476"), .6);
        for (double x = 0; x < Bounds.Width; x += 20) context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));
        for (double y = 0; y < Bounds.Height; y += 20) context.DrawLine(pen, new Point(0, y), new Point(Bounds.Width, y));
    }
    public static void Style(StyledElement root)
    {
        root.Styles.Add(new Style(s => s.OfType<Button>()) { Setters = { new Setter(Avalonia.Controls.Button.ForegroundProperty, Brush.Parse("#726659")), new Setter(Avalonia.Controls.Button.FontSizeProperty, 11d) } });
        root.Styles.Add(new Style(s => s.OfType<Button>().Class(":pointerover")) { Setters = { new Setter(Avalonia.Controls.Button.ForegroundProperty, Brush.Parse("#A74D43")), new Setter(Avalonia.Controls.Button.BackgroundProperty, Brush.Parse("#EDE3D3")) } });
        root.Styles.Add(new Style(s => s.OfType<TextBox>()) { Setters = { new Setter(TextBox.ForegroundProperty, Brush.Parse("#453E38")), new Setter(TextBox.CaretBrushProperty, Brush.Parse("#A74D43")) } });
        root.Styles.Add(new Style(s => s.OfType<TextBox>().Class(":focus")) { Setters = { new Setter(TextBox.ForegroundProperty, Brush.Parse("#453E38")) } });
        root.Styles.Add(new Style(s=>s.OfType<TextBox>().Template().OfType<TextBlock>().Name("PART_Watermark")) {Setters={new Setter(TextBlock.ForegroundProperty,Brush.Parse("#887C6F"))}});
        root.Styles.Add(new Style(s=>s.OfType<TextBox>().Class(":focus").Template().OfType<TextBlock>().Name("PART_Watermark")) {Setters={new Setter(TextBlock.ForegroundProperty,Brush.Parse("#887C6F"))}});
        root.Styles.Add(new Style(s=>s.OfType<TextBox>().Class(":pointerover").Template().OfType<TextBlock>().Name("PART_Watermark")) {Setters={new Setter(TextBlock.ForegroundProperty,Brush.Parse("#887C6F"))}});
    }
    public static TextBlock Label(string text, double size = 12, string color = "#776B5D", string font = "Menlo") =>
        new() { Text = text, FontSize = size, FontFamily = new FontFamily(font), Foreground = Brush.Parse(color), TextWrapping = TextWrapping.Wrap };
    public static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(10,6), CornerRadius = new CornerRadius(6) };
        button.Click += (_, _) => action(); return button;
    }
}
