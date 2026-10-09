using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Miche.Mac.Models;
using Miche.Mac.Services;

namespace Miche.Mac.Controls;

public sealed partial class VisionView
{
    private readonly HashSet<Guid> _selection=new();
    public IReadOnlyCollection<Guid> SelectedIds=>_selection.Count>0?_selection.ToArray():_selected is { } id?new[]{id}:Array.Empty<Guid>();
    private bool IsSelected(Guid id)=>SelectedIds.Contains(id);
    private bool HasCanvasGesture=>_gesture is not null||_marquee is not null||_group is not null;
    private sealed record Marquee(Point Start,IPointer Pointer,Guid[] Before,bool Additive)
    {public bool Started {get;set;} public Border Box {get;}=new(){Background=Brush.Parse("#224D91A8"),BorderBrush=Brush.Parse("#DFA6AD"),BorderThickness=new Thickness(1),IsHitTestVisible=false};}
    private sealed record GroupGesture(VisionItem[] Before,Point Start,IPointer Pointer)
    {public VisionItem[] Preview {get;set;}=Before;public bool Started {get;set;}}
    private Marquee? _marquee;
    private GroupGesture? _group;
    public void SelectObject(Guid id,bool additive=false)
    {
        if(!Items().Any(i=>i.Id==id&&i.DeletedAt is null))return;
        _selectedConnection=null;
        var current=SelectedIds.ToArray();
        if(additive){_selection.UnionWith(current);if(!_selection.Add(id))_selection.Remove(id);_selected=_selection.FirstOrDefault() is var first&&first!=Guid.Empty?first:null;}
        else if(!current.Contains(id)){_selection.Clear();_selection.Add(id);_selected=id;}
        UpdateSelection();
    }
    private void BeginMarquee(PointerPressedEventArgs e)
    {
        CancelGesture();_marquee=new(e.GetPosition(_canvas),e.Pointer,SelectedIds.ToArray(),e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        _canvas.Focus();e.Pointer.Capture(_canvas);
    }
    private void BeginGroupGesture(PointerPressedEventArgs e)
    {
        var before=Items().Where(i=>i.DeletedAt is null&&SelectedIds.Contains(i.Id)).Select(WorkspaceStore.CopyVision).ToArray();
        CancelGesture();_group=new(before,e.GetPosition(_canvas),e.Pointer);_canvas.Focus();e.Pointer.Capture(_canvas);
    }
    private bool MoveSelectionPointer(PointerEventArgs e)
    {
        if(_marquee is { } m)
        {
            var now=e.GetPosition(_canvas);var delta=now-m.Start;
            if(!m.Started&&Math.Abs(delta.X)<4&&Math.Abs(delta.Y)<4)return true;
            m.Started=true;var area=new Rect(m.Start,now).Normalize();
            Canvas.SetLeft(m.Box,area.Left);Canvas.SetTop(m.Box,area.Top);m.Box.Width=area.Width;m.Box.Height=area.Height;
            if(!_canvas.Children.Contains(m.Box))_canvas.Children.Add(m.Box);
            _selection.Clear();if(m.Additive)_selection.UnionWith(m.Before);
            foreach(var item in Items().Where(i=>i.DeletedAt is null))
            {
                // Transformed corners include rotation, and coordinates stay in board space at any zoom.
                if(!_objects.TryGetValue(item.Id,out var control))continue;
                var corners=new[]{new Point(0,0),new Point(item.Width,0),new Point(0,item.Height),new Point(item.Width,item.Height)}.Select(p=>control.TranslatePoint(p,_canvas)??default).ToArray();
                var bounds=new Rect(corners.Min(p=>p.X),corners.Min(p=>p.Y),corners.Max(p=>p.X)-corners.Min(p=>p.X),corners.Max(p=>p.Y)-corners.Min(p=>p.Y));
                if(area.Intersects(bounds))_selection.Add(item.Id);
            }
            _selected=_selection.Count>0?_selection.First():null;UpdateSelection();e.Handled=true;return true;
        }
        if(_group is not { } g)return false;
        var movement=e.GetPosition(_canvas)-g.Start;
        if(!g.Started&&Math.Abs(movement.X)<4&&Math.Abs(movement.Y)<4)return true;
        g.Started=true;
        var dx=Math.Clamp(movement.X,-g.Before.Min(i=>i.Left),Math.Max(-g.Before.Min(i=>i.Left),_canvas.Width-g.Before.Max(i=>i.Left+i.Width)));
        var dy=Math.Clamp(movement.Y,-g.Before.Min(i=>i.Top),10000-g.Before.Max(i=>i.Top));
        g.Preview=g.Before.Select(i=>{var copy=WorkspaceStore.CopyVision(i);copy.Left+=dx;copy.Top+=dy;return copy;}).ToArray();
        foreach(var item in g.Preview)Position(_objects[item.Id],item);
        RefreshConnections();
        _canvas.Height=Math.Max(_canvas.Height,g.Preview.Max(i=>i.Top+i.Height+160));e.Handled=true;return true;
    }
    private bool ReleaseSelectionPointer(PointerReleasedEventArgs e)
    {
        if(_marquee is { } m)
        {
            MoveSelectionPointer(e);_marquee=null;m.Pointer.Capture(null);_canvas.Children.Remove(m.Box);
            if(!m.Started&&!m.Additive)BeginText(m.Start);else RefreshBoard();e.Handled=true;return true;
        }
        if(_group is not { } g)return false;
        MoveSelectionPointer(e);_group=null;g.Pointer.Capture(null);
        if(g.Started&&!(CalendarMode&&CalendarDropRequested?.Invoke(g.Before.Select(i=>i.Id).ToArray(),e)==true))SaveCanvasItems(g.Preview);
        RefreshBoard();e.Handled=true;return true;
    }
    private bool CancelSelectionGesture()
    {
        if(_marquee is { } m){_marquee=null;m.Pointer.Capture(null);_canvas.Children.Remove(m.Box);_selection.Clear();_selection.UnionWith(m.Before);_selected=m.Before.FirstOrDefault() is var id&&id!=Guid.Empty?id:null;RefreshBoard();return true;}
        if(_group is { } g){_group=null;g.Pointer.Capture(null);RefreshBoard();return true;}
        return false;
    }
    public bool ResizeSelection(double factor)
    {
        if(_dialogBorder.IsVisible||_editor is not null||!double.IsFinite(factor)||factor<=0||SelectedIds.Count==0)return false;
        CancelGesture();
        var changed=Items().Where(i=>i.DeletedAt is null&&SelectedIds.Contains(i.Id)).Select(WorkspaceStore.CopyVision).ToArray();
        foreach(var item in changed)
        {
            var width=item.Kind=="vertical-line"?item.Width:item.Width*factor;
            var height=item.Kind=="horizontal-line"?item.Height:item.Height*factor;
            var font=item.Kind=="text"?item.FontSize*factor:item.FontSize;
            if(width<8||height<8||width>4000||height>4000||font<8||font>200)return false;
            item.Width=width;item.Height=height;item.FontSize=font;if(item.Kind=="table")ScaleTable(item,factor);
        }
        var saved=SaveCanvasItems(changed);RefreshBoard();return saved;
    }
    private bool HandleSelectionSizeKey(KeyEventArgs e)
    {
        if(_editor is not null||SelectedIds.Count==0||!e.KeyModifiers.HasFlag(KeyModifiers.Shift)||
           (e.KeyModifiers&(KeyModifiers.Meta|KeyModifiers.Control))==0||e.KeyModifiers.HasFlag(KeyModifiers.Alt))return false;
        if(e.Key==Key.OemPeriod)ResizeSelection(1.1);
        else if(e.Key==Key.OemComma)ResizeSelection(1/1.1);
        else return false;
        e.Handled=true;return true;
    }

}
