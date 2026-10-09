using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Miche.Mac.Models;

namespace Miche.Mac.Controls;

internal static class NotePresentation
{
    private static Thickness ShapePadding(string? shape) => shape switch {
        "ellipse" => new Thickness(24,16), "rounded-rectangle" => new Thickness(12,8),
        "horizontal-line" => new Thickness(2,0,2,7), "vertical-line" => new Thickness(9,2,0,2), _ => default
    };
    public static Control Decorate(Control body, string? shape, IBrush ink, bool compact=false)
    {
        if (shape is null) return body;
        var frame = new Grid {HorizontalAlignment=HorizontalAlignment.Left};
        var stroke = new VisionPrimitive(shape,ink) { IsHitTestVisible=false };
        if(shape=="horizontal-line"){stroke.Height=4;stroke.VerticalAlignment=VerticalAlignment.Bottom;}
        if(shape=="vertical-line"){stroke.Width=4;stroke.HorizontalAlignment=HorizontalAlignment.Left;}
        frame.Children.Add(stroke);
        var padding=ShapePadding(shape);
        if(compact)padding=new Thickness(padding.Left*.35,padding.Top*.35,padding.Right*.35,padding.Bottom*.35);
        frame.Children.Add(new Border { Padding=padding, Child=body });
        return frame;
    }
    public static void Fit(VisionItem item)
    {
        var shape=ShapePadding(item.NoteShape);
        var horizontal=(item.LinkedMicheId is not null?24:item.RoundedFrame?20:2)+shape.Left+shape.Right;
        var vertical=(item.LinkedMicheId is not null?42:item.RoundedFrame?14:2)+shape.Top+shape.Bottom;
        var text=new TextBlock{Text=item.Text,FontFamily=new FontFamily("Georgia"),FontSize=item.FontSize,TextWrapping=TextWrapping.Wrap};
        text.Measure(new Size(Math.Max(280,item.Width)-horizontal,double.PositiveInfinity));
        item.Width=Math.Clamp(Math.Max(item.LinkedMicheId is null?8:200,text.DesiredSize.Width+horizontal+4),8,4000);
        text.InvalidateMeasure();text.Measure(new Size(Math.Max(1,item.Width-horizontal-2),double.PositiveInfinity));
        item.Height=Math.Clamp(text.DesiredSize.Height+vertical,8,4000);
    }
}
