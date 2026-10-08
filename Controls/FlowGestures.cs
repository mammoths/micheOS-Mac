using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Miche.Mac.Services;

namespace Miche.Mac.Controls;

public sealed partial class FlowView
{
    private bool _preparingGesture;
    private FlowGesture? _gesture;
    private sealed class FlowGesture(Guid id,Border row,IPointer pointer,Point point,int minutes,bool resize,bool canReorder)
    {
        public Guid Id=id;public Border Row=row;public IPointer Pointer=pointer;public Point Start=point;
        public int BeforeMinutes=minutes,Minutes=minutes;public bool Resize=resize,CanReorder=canReorder,Started;
        public double DeltaY;
    }
    private void TaskPressed(Guid id,Border row,PointerPressedEventArgs e)
    {
        if(_dialog.IsVisible||!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed||Editing(e.Source))return;
        if(e.Source is Control child&&child.GetVisualAncestors().Any(c=>c is Border b&&b.Focusable))return;
        if(e.ClickCount==2){_selected=id;Refresh();ShowTiming(id);e.Handled=true;return;}
        BeginGesture(id,row,e,false);e.Handled=true;
    }
    private void BeginGesture(Guid id,Border row,PointerPressedEventArgs e,bool resize)
    {
        CancelGesture();_preparingGesture=true;
        try {if(!SaveDraft())return;} finally {_preparingGesture=false;}
        var item=_repo.Snapshot().StackItems.SingleOrDefault(i=>i.Id==id);
        if(item is null)return;
        if(resize&&(item.FinishedAt is not null||item.ReviewAt is not null))return;
        var canReorder=!item.CouldDo&&item.FinishedAt is null&&item.ReviewAt is null&&!OnDeckPlanner.HasTiming(item)&&QueueOrder().Contains(id);
        _gesture=new(id,row,e.Pointer,e.GetPosition(_rows),item.Minutes,resize,canReorder);
        e.Pointer.Capture(row);
    }
    private void MoveGesture(PointerEventArgs e)
    {
        if(_gesture is not {} g)return;
        var delta=e.GetPosition(_rows).Y-g.Start.Y;g.DeltaY=delta;
        if(!g.Resize&&!g.CanReorder)return;
        if(!g.Started&&Math.Abs(delta)<4)return;g.Started=true;
        if(g.Resize)
        {
            g.Minutes=Math.Clamp(g.BeforeMinutes+(int)Math.Round(delta/10)*5,5,1440);
            g.Row.MinHeight=DurationHeight(g.Minutes);
            if(_countdowns.TryGetValue(g.Id,out var text))text.Text="~"+g.Minutes+"m";
        }
        else g.Row.RenderTransform=new TranslateTransform(0,delta);
        e.Handled=true;
    }
    private void ReleaseGesture(PointerReleasedEventArgs e)
    {
        if(_gesture is not {} g)return;
        // Include final-release displacement: a fast flick may have no move event.
        MoveGesture(e);_gesture=null;g.Pointer.Capture(null);g.Row.RenderTransform=null;
        var current=_repo.Snapshot().StackItems.SingleOrDefault(i=>i.Id==g.Id);
        if(current is null){Refresh();return;}
        if(!g.Started){_selected=g.Id;Refresh();Focus();e.Handled=true;return;}
        if(g.Resize)
        {
            if(current.FinishedAt is null&&current.ReviewAt is null&&g.Minutes!=g.BeforeMinutes)Run(()=>_repo.SetStackDuration(g.Id,g.Minutes));
        }
        else if(!current.CouldDo&&current.FinishedAt is null&&current.ReviewAt is null&&!OnDeckPlanner.HasTiming(current))
        {
            var order=QueueOrder();
            if(order.Contains(g.Id))
            {
                var center=g.Row.Bounds.Center.Y+g.DeltaY;
                var before=order.Where(id=>id!=g.Id&&_rowControls.TryGetValue(id,out var row)&&row.Bounds.Center.Y<center).ToList();
                var target=order.Where(id=>id!=g.Id).ToList();target.Insert(before.Count,g.Id);
                if(!target.SequenceEqual(order))Run(()=>_repo.ReorderFlowStack(target,g.Id,_date));
            }
        }
        // Preserve existing selection only. Dropping does not focus or select the row.
        Refresh();e.Handled=true;
    }
    private Guid[] QueueOrder()
    {
        var d=_repo.Snapshot();return (_date is {} date?OnDeckPlanner.ForDate(d,date):OnDeckPlanner.Plan(d,DateTimeOffset.UtcNow)).Tonight.Select(s=>s.Item.Id).ToArray();
    }
    public void CancelGesture()
    {
        if(_gesture is not {} g)return;_gesture=null;g.Row.RenderTransform=null;g.Pointer.Capture(null);Refresh();
    }
    private void MoveSelected(int delta)
    {
        if(_couldDo||_selected is not {} id)return;
        var item=_repo.Snapshot().StackItems.SingleOrDefault(i=>i.Id==id);if(item is null||OnDeckPlanner.HasTiming(item))return;
        var order=QueueOrder().ToList();var index=order.IndexOf(id);var next=index+delta;if(index<0||next<0||next>=order.Count)return;
        order.RemoveAt(index);order.Insert(next,id);Run(()=>_repo.ReorderFlowStack(order,id,_date));
    }
}
