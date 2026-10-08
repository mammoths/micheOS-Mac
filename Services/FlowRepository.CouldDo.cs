// Timing and planning semantics adapted from current Windows Flow.
using Miche.Mac.Models;

namespace Miche.Mac.Services;

public sealed partial class FlowRepository
{
    public Guid AddCouldDo(string title)
    {
        var item=new OnDeckItem {Title=title.Trim(),CouldDo=true};
        Change(d=>{d.StackItems.Add(item);d.CouldDoDraft="";});return item.Id;
    }
    public void SaveCouldDoDraft(string text)
    {
        if(Snapshot().CouldDoDraft!=text)Commit(d=>d.CouldDoDraft=text,false,false);
    }
    private static void ParkInCouldDo(OnDeckItem item,DateTimeOffset now)
    {
        if(item.ReviewAt==null && item.Elapsed(now)>0)
            item.TimeBlocks.Add(new(now,item.Elapsed(now),true));
        item.CouldDo=true;item.StartedAt=null;item.ElapsedSeconds=0;
        item.ReviewAt=null;item.FinishedAt=null;item.NotBefore=null;
        item.PlannedTime=null;item.PlannedBand=null;item.MustToday=false;
    }
    public void ReturnToCouldDo(Guid id,DateTimeOffset now)=>Change(d=>{
        var item=d.StackItems.Single(i=>i.Id==id&&!i.CouldDo&&i.FinishedAt==null);
        bool running=item.StartedAt!=null;
        ParkInCouldDo(item,now);
        if(running)
        {
            var next=OnDeckPlanner.Plan(d,now).Tonight.FirstOrDefault();
            if(next!=null)next.Item.StartedAt=next.Start;
        }
    },now);

    // Null replacement inserts next (or into the chosen planning day). A replacement
    // trades the canonical records in one undoable transaction, never copies them.
    public void ScheduleCouldDo(Guid id,DateTimeOffset now,Guid? replace=null,DateOnly? date=null)=>Change(d=>{
        var incoming=d.StackItems.Single(i=>i.Id==id&&i.CouldDo);
        OnDeckItem? outgoing=replace.HasValue?d.StackItems.Single(i=>i.Id==replace.Value&&!i.CouldDo&&i.FinishedAt==null&&i.ReviewAt==null):null;
        bool running=outgoing?.StartedAt!=null;
        var plannedDate=outgoing?.NotBefore??date;
        var plannedTime=outgoing?.PlannedTime;
        var plannedBand=outgoing?.PlannedBand;
        var priority=outgoing?.MustToday??false;
        if(outgoing!=null&&!OnDeckPlanner.HasTiming(incoming))
        {
            incoming.FixedTime=outgoing.FixedTime;incoming.BeforeBedtimeMinutes=outgoing.BeforeBedtimeMinutes;incoming.FinishAtBedtime=outgoing.FinishAtBedtime;
        }
        d.StackItems.Remove(incoming);
        int index=outgoing!=null?d.StackItems.IndexOf(outgoing):0;
        if(outgoing==null)
        {
            var active=d.StackItems.FindIndex(i=>i.StartedAt!=null);
            index=date.HasValue?d.StackItems.Count:active>=0?active+1:0;
        }
        if(outgoing!=null)ParkInCouldDo(outgoing,now);
        incoming.CouldDo=false;incoming.NotBefore=plannedDate;incoming.PlannedTime=plannedTime;
        incoming.PlannedBand=plannedBand;incoming.MustToday=priority;
        incoming.StartedAt=running?now:null;
        if(running&&OnDeckPlanner.HasTiming(incoming))
        {
            var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById(d.Preferences.TimeZoneId)).DateTime);
            var anchor=OnDeckPlanner.TimedStart(incoming,DailyRhythm.ForDate(d,plannedDate??day),plannedDate??day);
            incoming.StartedAt=anchor>now?anchor:now;
        }
        d.StackItems.Insert(index,incoming);
        ResumeBeforeWaitingSlot(d,now);
    },now);
}
