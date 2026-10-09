using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Miche.Mac.Models;
using Miche.Mac.Services;

namespace Miche.Mac.Controls;

public sealed partial class VisionView
{
    private Popup? _connectionMenu;
    public Popup? ConnectionMenu=>_connectionMenu;
    private Guid? _connectingFrom;
    private IPointer? _connectionPointer;
    private Point _connectionStart;
    private Point? _connectionCursor;
    private readonly List<VisionConnectionStroke> _connectionStrokes=new();
    private (Guid Source,Guid Target)? _selectedConnection;
    public Guid? ConnectionSourceId=>_connectingFrom;
    public IReadOnlyList<VisionConnectionStroke> ConnectionControls=>_connectionStrokes;
    public bool BeginConnection(Guid source)
    {
        if(CalendarMode||!PrepareToLeave()||!Items().Any(i=>i.Id==source&&i.DeletedAt is null))return false;
        _selectedConnection=null;_connectingFrom=source;SelectObject(source);RefreshBoard();
        _message.Text="Click a downstream thought · esc cancels";_canvas.Focus();return true;
    }
    public bool ConnectObjects(Guid source,Guid target)
    {
        if(CalendarMode||_session is null)return false;
        var ok=_session.Act(()=>_session.Store.ConnectVisionItems(_micheId,source,target,_artifactId));
        if(ok){CancelConnection();_message.Text="";RefreshBoard();}return ok;
    }
    public bool DisconnectObjects(Guid source,Guid target)
    {
        if(CalendarMode||_session is null)return false;
        var ok=_session.Act(()=>_session.Store.DisconnectVisionItems(_micheId,source,target,_artifactId));
        if(ok){_selectedConnection=null;RefreshBoard();}return ok;
    }
    private bool CancelConnection()
    {
        if(_connectionMenu is not null)_connectionMenu.IsOpen=false;
        if(_connectingFrom is null&&_selectedConnection is null)return false;
        _connectingFrom=null;_connectionCursor=null;var pointer=_connectionPointer;_connectionPointer=null;pointer?.Capture(null);
        _selectedConnection=null;_message.Text="";RefreshBoard();return true;
    }
    private void AddConnectionMenu(VisionItem item,Grid layer,Border border)
    {
        if(CalendarMode)return;
        var connect=new MenuItem{Header="connect to…"};connect.Click+=(_,_)=>BeginConnection(item.Id);border.ContextMenu!.Items.Add(connect);
        if(!IsSelected(item.Id)||item.Kind is "horizontal-line" or "vertical-line")return;
        var port=new Border{Name="VisionConnectionPort",Width=12,Height=12,CornerRadius=new CornerRadius(6),Background=CanvasInk,
            BorderBrush=Background,BorderThickness=new Thickness(2),HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center,Margin=new Thickness(0,0,-10,0),Cursor=new Cursor(StandardCursorType.Cross)};
        ToolTip.SetTip(port,"Drag to connect to another thought");Avalonia.Automation.AutomationProperties.SetName(port,"Connect this thought");
        port.PointerPressed+=(_,e)=>{if(!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed)return;
            if(!BeginConnection(item.Id))return;_connectionStart=e.GetPosition(_canvas);_connectionCursor=_connectionStart;
            _connectionPointer=e.Pointer;e.Pointer.Capture(_canvas);e.Handled=true;};layer.Children.Add(port);
    }
    private bool TryConnectionTarget(Guid id)
    {
        if(_connectingFrom is not { } source)return false;
        if(id!=source)ConnectObjects(source,id);else _message.Text="Choose a different downstream thought · esc cancels";
        return true;
    }
    private bool MoveConnectionPointer(PointerEventArgs e)
    {
        if(_connectingFrom is null)return false;
        _connectionCursor=e.GetPosition(_canvas);RefreshConnections();e.Handled=true;return true;
    }
    private bool ReleaseConnectionPointer(PointerReleasedEventArgs e)
    {
        if(_connectionPointer is null||_connectingFrom is not { } source)return false;
        var point=e.GetPosition(_canvas);var distance=point-_connectionStart;var pointer=_connectionPointer;_connectionPointer=null;pointer.Capture(null);
        if(distance.X*distance.X+distance.Y*distance.Y>=16)
        {
            var target=_objects.LastOrDefault(pair=>pair.Key!=source&&
                _canvas.TranslatePoint(point,pair.Value) is { } local&&new Rect(pair.Value.Bounds.Size).Contains(local)).Key;
            if(target!=Guid.Empty)ConnectObjects(source,target);else CancelConnection();
        }
        e.Handled=true;return true;
    }
    private bool SelectConnectionAt(PointerPressedEventArgs e)
    {
        if(CalendarMode||_connectingFrom is not null)return false;
        var point=e.GetPosition(_canvas);var line=_connectionStrokes.Where(l=>l.TargetId!=Guid.Empty)
            .OrderBy(l=>l.Curve.DistanceTo(point)).FirstOrDefault();
        if(line is null||line.Curve.DistanceTo(point)>6/_zoom)return false;
        _selectedConnection=(line.SourceId,line.TargetId);_selected=null;_selection.Clear();UpdateSelection();
        foreach(var stroke in _connectionStrokes){stroke.Emphasized=ReferenceEquals(stroke,line);stroke.InvalidateVisual();}
        _canvas.Focus();
        if(e.GetCurrentPoint(_canvas).Properties.IsRightButtonPressed)
        {
            if(_connectionMenu is not null){_connectionMenu.IsOpen=false;_root.Children.Remove(_connectionMenu);}
            var remove=new Button{Content="Remove connection",FontSize=12,Padding=new Thickness(14,9)};
            var menu=new Popup{PlacementTarget=_canvas,Placement=PlacementMode.Pointer,ShouldUseOverlayLayer=true,IsLightDismissEnabled=true,
                Child=new Border{Background=Brush.Parse("#2C2033"),BorderBrush=CanvasMuted,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Child=remove}};
            remove.Click+=(_,_)=>{menu.IsOpen=false;DisconnectObjects(line.SourceId,line.TargetId);};
            menu.Opened+=(_,_)=>remove.Focus();_canvas.ContextMenu=null;_connectionMenu=menu;_root.Children.Add(menu);menu.IsOpen=true;
        }
        e.Handled=true;return true;
    }
    private void RefreshConnections()
    {
        foreach(var line in _connectionStrokes)_canvas.Children.Remove(line);_connectionStrokes.Clear();
        if(CalendarMode)return;
        var visible=Items().Where(i=>i.DeletedAt is null).ToDictionary(i=>i.Id);
        if(_selectedConnection is { } edge&&(!visible.ContainsKey(edge.Source)||!visible.ContainsKey(edge.Target)))_selectedConnection=null;
        if(_connectingFrom is { } armed&&!visible.ContainsKey(armed)){_connectingFrom=null;_connectionCursor=null;var pointer=_connectionPointer;_connectionPointer=null;pointer?.Capture(null);_message.Text="";}
        if(_gesture is { } g)visible[g.Preview.Id]=g.Preview;
        if(_group is { } group)foreach(var p in group.Preview)visible[p.Id]=p;
        if(_editing is not null&&_editor is not null){var draft=WorkspaceStore.CopyVision(_editing);draft.Width=_editor.Width;draft.Height=_editor.MinHeight;visible[draft.Id]=draft;}
        foreach(var source in visible.Values)
        foreach(var id in source.DownstreamIds)
        if(visible.TryGetValue(id,out var target))AddStroke(source,target,false);
        if(_connectingFrom is { } from&&visible.TryGetValue(from,out var start)&&_connectionCursor is { } cursor)
            AddStroke(start,new VisionItem{Id=Guid.Empty,Left=cursor.X,Top=cursor.Y,Width=1,Height=1},true);
    }
    private void AddStroke(VisionItem source,VisionItem target,bool preview)
    {
        var curve=ConnectionGeometry.Create(new Rect(source.Left,source.Top,source.Width,source.Height),new Rect(target.Left,target.Top,target.Width,target.Height),
            source.Kind=="ellipse"||source.NoteShape=="ellipse",target.Kind=="ellipse"||target.NoteShape=="ellipse");
        Point Rotate(Point p,VisionItem item){var c=new Point(item.Left+item.Width/2,item.Top+item.Height/2);var d=p-c;var a=item.Rotation*Math.PI/180;return c+new Vector(d.X*Math.Cos(a)-d.Y*Math.Sin(a),d.X*Math.Sin(a)+d.Y*Math.Cos(a));}
        curve=curve with{Start=Rotate(curve.Start,source),Control1=Rotate(curve.Control1,source),Control2=Rotate(curve.Control2,target),End=Rotate(curve.End,target),ArrowLeft=Rotate(curve.ArrowLeft,target),ArrowRight=Rotate(curve.ArrowRight,target)};
        var stroke=new VisionConnectionStroke(source.Id,target.Id,curve,CanvasInk){Width=_canvas.Width,Height=_canvas.Height,Opacity=preview?.5:.8,IsHitTestVisible=false,ZIndex=-1};
        if(_selectedConnection==(source.Id,target.Id))stroke.Emphasized=true;
        _connectionStrokes.Add(stroke);_canvas.Children.Add(stroke);
    }
}

public sealed class VisionConnectionStroke:Control
{
    public Guid SourceId{get;}public Guid TargetId{get;}public ConnectionCurve Curve{get;}public bool Emphasized{get;set;}
    private readonly StreamGeometry _curve=new();private readonly StreamGeometry _arrow=new();private readonly IBrush _ink;
    public VisionConnectionStroke(Guid source,Guid target,ConnectionCurve curve,IBrush ink)
    {
        SourceId=source;TargetId=target;Curve=curve;_ink=ink;Cursor=new Cursor(StandardCursorType.Hand);
        using(var c=_curve.Open()){c.BeginFigure(curve.Start,false);c.CubicBezierTo(curve.Control1,curve.Control2,curve.End);c.EndFigure(false);}
        using(var c=_arrow.Open()){c.BeginFigure(curve.ArrowLeft,false);c.LineTo(curve.End);c.LineTo(curve.ArrowRight);c.EndFigure(false);}
        Avalonia.Automation.AutomationProperties.SetName(this,"Thought connection");
    }
    public override void Render(DrawingContext context){var pen=new Pen(_ink,Emphasized?3:1.8,lineCap:PenLineCap.Round,lineJoin:PenLineJoin.Round);context.DrawGeometry(null,pen,_curve);context.DrawGeometry(null,pen,_arrow);}
}
