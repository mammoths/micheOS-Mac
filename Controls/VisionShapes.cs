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
        var note=Items().SingleOrDefault(i=>i.Id==_selected&&i.Kind=="text"&&i.DeletedAt is null);
        var choices=new StackPanel {Spacing=4};
        choices.Children.Add(new TextBlock {Text=note is null?"draw on the page":"around this note",Foreground=CanvasMuted,Margin=new Thickness(6)});
        foreach(var shape in Shapes.Where(s=>s.Label!="circle"))
        {
            var row=new StackPanel {Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=10};
            row.Children.Add(new VisionPrimitive(shape.Kind,CanvasInk){Width=36,Height=24});
            row.Children.Add(new TextBlock {Text=note is null?shape.Label:shape.Kind switch {"ellipse"=>"circle the thought","rounded-rectangle"=>"frame the thought","horizontal-line"=>"underline","vertical-line"=>"mark the side",_=>shape.Label},Foreground=CanvasInk,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center});
            var button=new Button {Content=row,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch};
            button.Click+=(_,_)=>{_shapePicker.IsOpen=false;if(note is null)AddShape(shape.Kind,point,shape.Width,shape.Height);else SetNoteShape(note.Id,shape.Kind);};choices.Children.Add(button);
        }
        if(note?.NoteShape is not null)AddButton(choices,"remove the mark",()=>{_shapePicker.IsOpen=false;SetNoteShape(note.Id,null);});
        _shapePicker.PlacementTarget=_shapeButton;_shapePicker.Child=new Border {Background=Brush.Parse(CalendarMode?"#F7F1E5":"#2C2033"),BorderBrush=CanvasMuted,BorderThickness=new Thickness(1),Padding=new Thickness(8),Child=choices};
        _shapePicker.IsOpen=true;
    }
    public bool SetNoteShape(Guid id,string? shape)
    {
        if(shape is not null&&!WorkspaceStore.IsVisionShape(shape)||!PrepareToLeave())return false;
        var note=Items().SingleOrDefault(i=>i.Id==id&&i.Kind=="text"&&i.DeletedAt is null);if(note is null)return false;
        note=WorkspaceStore.CopyVision(note);note.NoteShape=shape;NotePresentation.Fit(note);
        if(!SaveCanvasItems(new[]{note}))return false;
        _selected=id;_selection.Clear();RefreshBoard();return true;
    }
    public bool AttachShapeToNote(Guid shapeId,Guid noteId)
    {
        if(!PrepareToLeave())return false;
        var items=Items();var shape=items.SingleOrDefault(i=>i.Id==shapeId&&WorkspaceStore.IsVisionShape(i.Kind)&&i.DeletedAt is null);
        var note=items.SingleOrDefault(i=>i.Id==noteId&&i.Kind=="text"&&i.DeletedAt is null);if(shape is null||note is null)return false;
        shape=WorkspaceStore.CopyVision(shape);note=WorkspaceStore.CopyVision(note);
        note.NoteShape=shape.Kind;NotePresentation.Fit(note);shape.DeletedAt=DateTimeOffset.UtcNow;
        if(!SaveCanvasItems(new[]{note,shape}))return false;
        _selected=note.Id;_selection.Clear();RefreshBoard();return true;
    }
    private bool TryAttachShape(VisionItem shape,Point drop)
    {
        var center=new Point(shape.Left+shape.Width/2,shape.Top+shape.Height/2);
        var note=Items().LastOrDefault(i=>i.Kind=="text"&&i.DeletedAt is null&&(new Rect(i.Left,i.Top,i.Width,i.Height).Contains(drop)||new Rect(i.Left,i.Top,i.Width,i.Height).Contains(center)));
        if(note is null)return false;
        AttachShapeToNote(shape.Id,note.Id);return true;
    }
    public bool AddShape(string kind,Point point,double width,double height)
    {
        if(_dialogBorder.IsVisible||!WorkspaceStore.IsVisionShape(kind)||!PrepareToLeave())return false;
        var item=new VisionItem {Kind=kind,Left=Math.Clamp(point.X,0,10000),Top=Math.Clamp(point.Y,0,10000),Width=width,Height=height};
        if(!SaveCanvasItems(new[]{item}))return false;
        _selection.Clear();_selected=item.Id;RefreshBoard();_canvas.Focus();return true;
    }
}

// Code-native vector objects: no bitmap assets or file-picker dependency.
internal sealed class VisionPrimitive(string kind, IBrush? ink=null) : Control
{
    private readonly Pen Stroke=new(ink??Brush.Parse("#F4E5D1"),2);
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
