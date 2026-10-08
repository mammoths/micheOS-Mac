// Timing and planning semantics adapted from current Windows Flow.
using Miche.Mac.Models;

namespace Miche.Mac.Services;

public sealed partial class FlowRepository
{
    // A future reservation is not running work. Returned work can use the
    // available time before it, while the timed task keeps its own anchor.
    private static void ResumeBeforeWaitingSlot(TodayDocument d,DateTimeOffset now)
    {
        var waiting=d.StackItems.SingleOrDefault(i=>i.StartedAt>now&&OnDeckPlanner.HasTiming(i));
        if(waiting==null)return;
        var next=OnDeckPlanner.Plan(d,now).Tonight.FirstOrDefault();
        if(next==null||next.Item.Id==waiting.Id||next.Start>now)return;
        waiting.StartedAt=null;
        next.Item.StartedAt=now;
    }
    // Reconcile absolute timestamps, not tick counts. One transaction catches up
    // after sleep/restart; reviewing a past block never changes the live one.
    public bool AdvanceStackFlow(DateTimeOffset now)
    {
        lock (_sync)
        {
            var active=_document.StackItems.SingleOrDefault(i=>i.StartedAt!=null);
            if(active?.StartedAt>now&&OnDeckPlanner.HasTiming(active))
            {
                var day=active.NotBefore??DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById(_document.Preferences.TimeZoneId)).DateTime);
                var reserved=OnDeckPlanner.TimedStart(active,DailyRhythm.ForDate(_document,day),day);
                if(reserved<now)reserved=now;
                if(reserved!=active.StartedAt)
                {
                    var id=active.Id;
                    Commit(d=>d.StackItems.Single(i=>i.Id==id).StartedAt=reserved,true,false,now);
                    active=_document.StackItems.Single(i=>i.Id==id);
                }
            }
            if(active==null||active.Elapsed(now)<active.Minutes*60)return false;
            Commit(d=>CatchUpFlow(d,now),true,false,now);
            return true;
        }
    }

    private static void CatchUpFlow(TodayDocument d,DateTimeOffset now)
    {
        var active=d.StackItems.SingleOrDefault(i=>i.StartedAt!=null);
        if(active==null)return;
        var zone=TimeZoneInfo.FindSystemTimeZoneById(d.Preferences.TimeZoneId);
        var sessionDay=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(active.StartedAt!.Value,zone).DateTime);
        while(active!=null&&active.Elapsed(now)>=active.Minutes*60)
        {
            var boundary=active.StartedAt!.Value.AddSeconds(Math.Max(0,active.Minutes*60-active.ElapsedSeconds));
            EndBlock(active,boundary,false);
            var next=NextFlowSlot(d,boundary,sessionDay);
            active=next?.Item;
            if(active!=null)active.StartedAt=next!.Start;
        }
    }

    private static OnDeckSlot? NextFlowSlot(TodayDocument d,DateTimeOffset now,DateOnly day) =>
        OnDeckPlanner.Plan(d,now).Tonight.FirstOrDefault(s=>s.Item.NotBefore==null||s.Item.NotBefore<=day);

    private static void EndBlock(OnDeckItem item,DateTimeOffset now,bool early)
    {
        Pause(item,now);item.ReviewAt=now;
        item.TimeBlocks.Add(new(now,item.ElapsedSeconds,early));
    }

    public Guid? StepStackFlow(DateTimeOffset now,Guid? preferred=null)
    {
        // A reserved future slot is waiting, not work already performed.
        var waiting=Snapshot().StackItems.SingleOrDefault(i=>i.StartedAt>now);
        if(waiting!=null)return waiting.Id;
        if(AdvanceStackFlow(now))return Snapshot().StackItems.SingleOrDefault(i=>i.StartedAt!=null)?.Id;
        Guid? nextId=null;
        Change(d=>{
            var active=d.StackItems.SingleOrDefault(i=>i.StartedAt!=null);
            if(active!=null){EndBlock(active,now,true);active.ReviewAt=null;active.FinishedAt=now;}
            var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById(d.Preferences.TimeZoneId)).DateTime);
            var plan=OnDeckPlanner.Plan(d,now);
            var slot=active==null&&preferred.HasValue?plan.Tonight.FirstOrDefault(s=>s.Item.Id==preferred):NextFlowSlot(d,now,day);
            if(slot!=null){slot.Item.StartedAt=slot.Start;nextId=slot.Item.Id;}
        },now);
        return nextId;
    }

    public void CompleteReviewedStackItem(Guid id,DateTimeOffset now)=>Change(d=>{
        var item=d.StackItems.Single(i=>i.Id==id&&i.ReviewAt!=null&&i.StartedAt==null);
        item.FinishedAt=now;item.ReviewAt=null;
    },now);

    // Completion targets the selected record, never whichever timer happens to
    // be running. Only finishing the running task hands off the existing flow.
    public void CompleteSelectedStackItem(Guid id,DateTimeOffset now)
    {
        if(Snapshot().StackItems.Single(i=>i.Id==id).FinishedAt!=null)return;
        Change(d=>{
            var item=d.StackItems.Single(i=>i.Id==id&&!i.CouldDo&&i.FinishedAt==null);
            var wasRunning=item.StartedAt is {} start&&start<=now;
            if(wasRunning)EndBlock(item,now,true);
            else Pause(item,now);
            item.ReviewAt=null;item.FinishedAt=now;
            if(!wasRunning)return;
            var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById(d.Preferences.TimeZoneId)).DateTime);
            var next=NextFlowSlot(d,now,day);
            if(next!=null)next.Item.StartedAt=next.Start;
        },now);
    }

    public void RequeueReviewedStackItem(Guid id,DateTimeOffset now)=>Change(d=>{
        var item=d.StackItems.Single(i=>i.Id==id&&i.ReviewAt!=null&&i.FinishedAt==null);
        item.ReviewAt=null;item.ElapsedSeconds=0;item.StartedAt=null;item.NotBefore=null;item.PlannedTime=null;
        d.StackItems.Remove(item);d.StackItems.Add(item);
        ResumeBeforeWaitingSlot(d,now);
        if(!d.StackItems.Any(i=>i.StartedAt!=null))
        {var next=OnDeckPlanner.Plan(d,now).Tonight.FirstOrDefault();if(next!=null)next.Item.StartedAt=next.Start;}
    },now);
}
