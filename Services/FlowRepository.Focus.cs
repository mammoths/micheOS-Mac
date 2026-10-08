// Timing and planning semantics adapted from current Windows Flow.
using Miche.Mac.Models;
namespace Miche.Mac.Services;
public sealed partial class FlowRepository
{
    // Undo an accidental start without completing, parking elsewhere, or
    // handing off to another task. Keep the schedule and prior block history.
    public void ResetStackTimer(Guid id,DateTimeOffset now)=>Change(d=>{
        var item=d.StackItems.Single(i=>i.Id==id&&!i.CouldDo&&i.FinishedAt==null);
        item.StartedAt=null;
        item.ElapsedSeconds=0;
        item.ReviewAt=null;
    },now);

    public void FocusStackItem(Guid id,DateTimeOffset now)
    {
        var current=Snapshot().StackItems.Single(i=>i.Id==id);
        if(current.StartedAt is {} start&&start<=now)return;
        Change(d=>{
            var item=d.StackItems.Single(i=>i.Id==id&&!i.CouldDo&&i.FinishedAt==null);
            var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById(d.Preferences.TimeZoneId)).DateTime);
            Require(!(item.NotBefore>day),"This activity belongs to a future day.");
            Require(!OnDeckPlanner.HasTiming(item)||OnDeckPlanner.TimedStart(item,DailyRhythm.ForDate(d,day),day)<=now,"This activity has a reserved time. Double-click to change it.");
            foreach(var running in d.StackItems.Where(i=>i.StartedAt!=null))Pause(running,now);
            if(item.ReviewAt!=null){item.ReviewAt=null;item.ElapsedSeconds=0;}
            item.StartedAt=now;item.NotBefore=null;
            d.StackItems.Remove(item);d.StackItems.Insert(0,item);
        },now);
    }
}
