using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Miche.Mac.Models;
using Miche.Mac.Services;

namespace Miche.Mac.Controls;

public sealed partial class VisionView
{
    private Button? _shapeButton;
    private readonly Avalonia.Controls.Primitives.Popup _shapePicker=new(){Placement=Avalonia.Controls.PlacementMode.Bottom,IsLightDismissEnabled=true};
    private static readonly (string Label,string Kind,double Width,double Height)[] Shapes = {
        ("horizontal line","horizontal-line",240,16), ("vertical line","vertical-line",16,200),
        ("rounded rectangle","rounded-rectangle",240,160), ("oval","ellipse",240,140), ("circle","ellipse",180,180)
    };
    private void AddShapeMenuItems(ContextMenu menu,Point point)
    {
        foreach(var shape in Shapes)
        {
            var entry=new MenuItem {Header=shape.Label};
            entry.Click+=(_,_)=>AddShape(shape.Kind,point,shape.Width,shape.Height);menu.Items.Add(entry);
        }
    }
    private void ShowShapes(Point point)
    {
        if(!PrepareToLeave())return;
        var choices=new StackPanel();
        foreach(var shape in Shapes)AddButton(choices,shape.Label,()=>{_shapePicker.IsOpen=false;AddShape(shape.Kind,point,shape.Width,shape.Height);});
        _shapePicker.PlacementTarget=_shapeButton;_shapePicker.Child=new Border {Background=Brush.Parse("#2C2033"),BorderBrush=Brush.Parse("#5D435C"),BorderThickness=new Thickness(1),Padding=new Thickness(8),Child=choices};
        _shapePicker.IsOpen=true;
    }
    public bool AddShape(string kind,Point point,double width,double height)
    {
        if(_dialogBorder.IsVisible||!WorkspaceStore.IsVisionShape(kind)||!PrepareToLeave())return false;
        var item=new VisionItem {Kind=kind,Left=Math.Clamp(point.X,0,10000),Top=Math.Clamp(point.Y,0,10000),Width=width,Height=height};
        if(!_session!.Act(()=>_session.Store.SaveVisionItem(_micheId,item,_artifactId),"Shape added to this Vision."))return false;
        _selection.Clear();_selected=item.Id;RefreshBoard();_canvas.Focus();return true;
    }
}

// Code-native vector objects: no bitmap assets or file-picker dependency.
internal sealed class VisionPrimitive(string kind) : Control
{
    private static readonly Pen Stroke=new(Brush.Parse("#F4E5D1"),2);
    public override void Render(DrawingContext context)
    {
        var w=Bounds.Width;var h=Bounds.Height;
        if(kind=="horizontal-line")context.DrawLine(Stroke,new Point(2,h/2),new Point(Math.Max(2,w-2),h/2));
        else if(kind=="vertical-line")context.DrawLine(Stroke,new Point(w/2,2),new Point(w/2,Math.Max(2,h-2)));
        else
        {
            var rect=new Rect(2,2,Math.Max(0,w-4),Math.Max(0,h-4));
            if(kind=="ellipse")context.DrawEllipse(null,Stroke,rect);
            else context.DrawRectangle(null,Stroke,rect,18,18);
        }
    }
}
