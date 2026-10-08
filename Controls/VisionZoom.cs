using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Miche.Mac.Controls;

public sealed partial class VisionView
{
    private double _zoom=1;
    private readonly LayoutTransformControl _zoomHost=new();
    private readonly Button _zoomLabel=new();
    public double ZoomScale=>_zoom;
    private Point VisibleInsertionPoint=>new(48+_scroll.Offset.X/_zoom,48+_scroll.Offset.Y/_zoom);
    private void InitializeZoom()
    {
        _zoomHost.Child=_canvas;_zoomHost.LayoutTransform=new ScaleTransform(1,1);_scroll.Content=_zoomHost;
        var footer=new StackPanel {Orientation=Orientation.Horizontal,Spacing=4,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(12,6)};
        void Button(string caption,string name,Action action,Button? existing=null)
        {
            var button=existing??new Button();button.Content=caption;button.FontSize=12;button.MinWidth=36;
            AutomationProperties.SetName(button,name);button.Click+=(_,_)=>action();footer.Children.Add(button);
        }
        Button("−","Zoom out",()=>SetZoom(_zoom/1.25));
        Button("100%","Reset Vision zoom",()=>SetZoom(1),_zoomLabel);
        Button("+","Zoom in",()=>SetZoom(_zoom*1.25));
        Button("fit","Fit Vision objects",()=>FitBoard());
        Grid.SetRow(footer,2);_root.Children.Add(footer);
    }
    public bool SetZoom(double scale)
    {
        if(_dialogBorder.IsVisible||!double.IsFinite(scale)||!PrepareToLeave())return false;
        var viewport=_scroll.Viewport;
        var center=new Point((_scroll.Offset.X+viewport.Width/2)/_zoom,(_scroll.Offset.Y+viewport.Height/2)/_zoom);
        ApplyZoom(scale,center);return true;
    }
    private void ApplyZoom(double scale,Point center)
    {
        _zoom=Math.Clamp(scale,.25,2);
        _zoomHost.LayoutTransform=new ScaleTransform(_zoom,_zoom);
        _zoomLabel.Content=$"{_zoom:P0}";
        UpdateExtent();TopLevel.GetTopLevel(this)?.UpdateLayout();
        _scroll.Offset=new Vector(Math.Max(0,center.X*_zoom-_scroll.Viewport.Width/2),Math.Max(0,center.Y*_zoom-_scroll.Viewport.Height/2));
    }
    public bool FitBoard()
    {
        if(_dialogBorder.IsVisible||!PrepareToLeave())return false;
        var items=Items().Where(i=>i.DeletedAt is null).ToArray();
        if(items.Length==0)return SetZoom(1);
        var bounds=items.Select(i=> {
            var radians=i.Rotation*Math.PI/180;
            var width=Math.Abs(Math.Cos(radians))*i.Width+Math.Abs(Math.Sin(radians))*i.Height;
            var height=Math.Abs(Math.Sin(radians))*i.Width+Math.Abs(Math.Cos(radians))*i.Height;
            return new Rect(i.Left+i.Width/2-width/2,i.Top+i.Height/2-height/2,width,height);
        }).Aggregate((a,b)=>a.Union(b));
        var scale=Math.Min(Math.Max(1,_scroll.Viewport.Width-80)/Math.Max(1,bounds.Width),Math.Max(1,_scroll.Viewport.Height-80)/Math.Max(1,bounds.Height));
        ApplyZoom(scale,bounds.Center);return true;
    }
    private bool HandleZoomKey(KeyEventArgs e)
    {
        if((e.KeyModifiers&(KeyModifiers.Meta|KeyModifiers.Control))==0)return false;
        if(e.Key is Key.OemPlus or Key.Add)SetZoom(_zoom*1.25);
        else if(e.Key is Key.OemMinus or Key.Subtract)SetZoom(_zoom/1.25);
        else if(e.Key==Key.D0)SetZoom(1);
        else return false;
        e.Handled=true;return true;
    }
}
