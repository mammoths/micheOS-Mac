// Timing and planning semantics adapted from current Windows Flow.
using Miche.Mac.Models;

namespace Miche.Mac.Services;

public sealed partial class FlowRepository
{
    public Guid AddToStack(string title, DateOnly? date = null, TimeOnly? plannedTime = null)
    {
        var item = new OnDeckItem { Title = title.Trim(), NotBefore = date, PlannedTime = plannedTime };
        var snapshot=Snapshot();
        var localDate=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.Now,TimeZoneInfo.FindSystemTimeZoneById(snapshot.Preferences.TimeZoneId)).DateTime);
        var prefs=DailyRhythm.ForDate(snapshot,date??localDate);
        item.PlannedBand=plannedTime==prefs.MorningStart?"morning":plannedTime==new TimeOnly(12,0)?"afternoon":plannedTime==prefs.EveningStart?"evening":null;
        Change(d => { d.StackItems.Add(item); if (date.HasValue) d.StackDayDrafts.Remove(date.Value.ToString("yyyy-MM-dd")); else d.StackDraft = ""; });
        return item.Id;
    }

    public void SaveStackDraft(string text, DateOnly? date = null)
    {
        if (date is { } day)
        {
            var key = day.ToString("yyyy-MM-dd");
            if (Snapshot().StackDayDrafts.GetValueOrDefault(key, "") == text) return;
            Commit(d => { if (text.Length == 0) d.StackDayDrafts.Remove(key); else d.StackDayDrafts[key] = text; }, false, false);
            return;
        }
        if (Snapshot().StackDraft == text) return;
        Commit(d => d.StackDraft = text, false, false);
    }

    public void StartStackItem(Guid id, DateTimeOffset now) => Change(d =>
    {
        var item = d.StackItems.Single(i => i.Id == id && !i.CouldDo && i.FinishedAt == null && i.ReviewAt == null);
        foreach (var running in d.StackItems.Where(i => i.StartedAt != null)) Pause(running, now);
        item.StartedAt = now; item.NotBefore = null;
    }, now);
    public void PauseStackItem(Guid id, DateTimeOffset now) => Change(d => Pause(d.StackItems.Single(i => i.Id == id), now), now);
    public void FinishStackItem(Guid id, DateTimeOffset now) => Change(d =>
    {
        var item = d.StackItems.Single(i => i.Id == id);
        Pause(item, now); item.FinishedAt = now;
    }, now);
    public Guid? FinishStackItemAndStartNext(Guid id, DateTimeOffset now)
    {
        Guid? nextId = null;
        // Finish and hand off in one durable, undoable transaction. The planner
        // supplies the visible order and excludes explicitly deferred tasks.
        Change(d =>
        {
            var item = d.StackItems.Single(i => i.Id == id && i.FinishedAt == null);
            Pause(item, now); item.FinishedAt = now;
            var next = OnDeckPlanner.Plan(d, now).Tonight.FirstOrDefault()?.Item;
            if (next == null) return;
            nextId = next.Id;
            next.StartedAt ??= now;
            next.NotBefore = null;
        }, now);
        return nextId;
    }
    private static void Pause(OnDeckItem item, DateTimeOffset now)
    { item.ElapsedSeconds = item.Elapsed(now); item.StartedAt = null; }

    public void ReorderStack(IReadOnlyList<Guid> visibleOrder) => Change(d =>
    {
        // Only reorder the visible subset; hidden/completed records retain their slots.
        var ids = visibleOrder.ToHashSet();
        Require(ids.Count == visibleOrder.Count && ids.All(id => d.StackItems.Any(i => i.Id == id)), "The stack changed. Try dragging again.");
        var sorted = visibleOrder.Select(id => d.StackItems.Single(i => i.Id == id)).ToArray();
        var n = 0;
        for (var i = 0; i < d.StackItems.Count; i++) if (ids.Contains(d.StackItems[i].Id)) d.StackItems[i] = sorted[n++];
    });

    public void SetStackDuration(Guid id, int minutes) => Change(d => d.StackItems.Single(i => i.Id == id).Minutes = minutes);

    public void ReorderFlowStack(IReadOnlyList<Guid> order, Guid moved, DateOnly? date = null) => Change(d =>
    {
        var ids=order.ToHashSet();
        Require(ids.Count==order.Count && ids.All(id=>d.StackItems.Any(i=>i.Id==id&&!i.CouldDo&&i.FinishedAt==null&&i.ReviewAt==null)),"The flow changed. Try dragging again.");
        var index=order.ToList().IndexOf(moved);
        Require(index>=0,"That task is no longer in the flow.");
        var item=d.StackItems.Single(i=>i.Id==moved);
        Require(!OnDeckPlanner.HasTiming(item),"Change this task’s time to move its reserved slot.");
        if(date.HasValue)
        {
            Require(order.All(id=>d.StackItems.Single(i=>i.Id==id).NotBefore==date),"The day changed. Try dragging again.");
            var neighbor=index>0?order[index-1]:order.Skip(1).FirstOrDefault();
            if(neighbor!=Guid.Empty){var other=d.StackItems.Single(i=>i.Id==neighbor);item.PlannedTime=other.PlannedTime;item.PlannedBand=other.PlannedBand;}
        }
        item.AfterTimedItemId=order.Take(index).Select(id=>d.StackItems.Single(i=>i.Id==id))
            .LastOrDefault(OnDeckPlanner.HasTiming)?.Id;
        var sorted=order.Select(id=>d.StackItems.Single(i=>i.Id==id)).ToArray();var n=0;
        for(var i=0;i<d.StackItems.Count;i++)if(ids.Contains(d.StackItems[i].Id))d.StackItems[i]=sorted[n++];
    });

    public void ReorderPlannedStack(IReadOnlyList<Guid> order, Guid moved, DateOnly date) => Change(d =>
    {
        Require(order.Distinct().Count() == order.Count && order.All(id => d.StackItems.Any(i => i.Id == id && i.NotBefore == date && i.FinishedAt == null)), "The day changed. Try dragging again.");
        var item = d.StackItems.Single(i => i.Id == moved);
        var index = order.ToList().IndexOf(moved);
        Require(index >= 0, "That task is no longer on this day.");
        var neighbor = index > 0 ? order[index - 1] : order.Skip(1).FirstOrDefault();
        if (neighbor != Guid.Empty) { var next=d.StackItems.Single(i => i.Id == neighbor); item.PlannedTime=next.PlannedTime; item.PlannedBand=next.PlannedBand; }
        var ids = order.ToHashSet(); var sorted = order.Select(id => d.StackItems.Single(i => i.Id == id)).ToArray(); var n = 0;
        for (var i = 0; i < d.StackItems.Count; i++) if (ids.Contains(d.StackItems[i].Id)) d.StackItems[i] = sorted[n++];
    });

    private static void ValidateStack(TodayDocument d)
    {
        Require(d.StackItems != null && d.StackItems.All(i => i != null), "Unreadable stack. Nothing was replaced.");
        Unique(d.StackItems!.Select(i => i.Id), "Stack");
        Require(d.StackItems.Count(i => i.StartedAt != null) <= 1, "Only one stack timer can run at a time.");
        foreach (var item in d.StackItems)
        {
            Require(!item.CouldDo || (item.StartedAt == null && item.FinishedAt == null && item.ReviewAt == null), "A Could do item cannot run or be completed.");
            Require(item.BeforeBedtimeMinutes==null || item.BeforeBedtimeMinutes is >=1 and <=1440,"Choose 1–1440 minutes before bedtime.");
            Require((item.FixedTime.HasValue?1:0)+(item.BeforeBedtimeMinutes.HasValue?1:0)+(item.FinishAtBedtime?1:0)<=1,"Choose one timing rule for a task.");
            Text(item.Title, 1000, "Stack item", true);
            Require(item.Minutes is >= 1 and <= 1440, "Choose between 1 and 1440 minutes.");
            Require(double.IsFinite(item.ElapsedSeconds) && item.ElapsedSeconds >= 0, "Invalid timer duration.");
            Require(item.StartedAt == null || item.FinishedAt == null, "A finished item cannot have a running timer.");
            Require(item.ReviewAt==null||(item.StartedAt==null&&item.FinishedAt==null),"A review item cannot run or be finished.");
            Require(item.TimeBlocks!=null&&item.TimeBlocks.All(b=>b!=null&&double.IsFinite(b.Seconds)&&b.Seconds>=0),"Invalid time block history.");
        }
        Text(d.StackDraft, 16000, "Stack draft"); Text(d.Treat, 1000, "Treat");
        Text(d.CouldDoDraft, 16000, "Could do draft");
        Require(d.StackDayDrafts != null, "Unreadable day drafts.");
        foreach (var draft in d.StackDayDrafts!) { Require(DateOnly.TryParseExact(draft.Key,"yyyy-MM-dd",out _), "Invalid draft date."); Text(draft.Value,16000,"Day draft"); }
    }
}

public sealed record OnDeckSlot(OnDeckItem Item, DateTimeOffset Start, DateTimeOffset End, bool Tight);
public sealed record OnDeckPlan(List<OnDeckSlot> Tonight, List<OnDeckItem> Tomorrow);

public static class OnDeckPlanner
{
    public static DateTimeOffset? AfterTimedStart(TodayDocument doc,OnDeckItem item,DateOnly date)
    {
        if(item.AfterTimedItemId is not {} id)return null;
        var anchor=doc.StackItems.SingleOrDefault(i=>i.Id==id&&!i.CouldDo&&HasTiming(i)&&!(i.NotBefore>date));
        if(anchor==null)return null; // Deleting or untiming the landmark releases the constraint.
        return TimedStart(anchor,DailyRhythm.ForDate(doc,date),date).AddMinutes(anchor.Minutes);
    }
    public static bool HasTiming(OnDeckItem item)=>item.FixedTime.HasValue||item.BeforeBedtimeMinutes.HasValue||item.FinishAtBedtime;
    public static DateTimeOffset TimedStart(OnDeckItem item,TodayPreferences prefs,DateOnly date)
    {
        if(item.FixedTime is {} clock)return At(prefs,date,clock);
        var bedDate=prefs.WindDownTime<prefs.MorningStart?date.AddDays(1):date;
        return At(prefs,bedDate,prefs.WindDownTime).AddMinutes(-(item.FinishAtBedtime?item.Minutes:item.BeforeBedtimeMinutes??item.Minutes));
    }
    public static DateTimeOffset At(TodayPreferences prefs, DateOnly date, TimeOnly time)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(prefs.TimeZoneId);
        var wall = date.ToDateTime(time, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(wall)) wall = wall.AddMinutes(1);
        return new DateTimeOffset(wall, zone.GetUtcOffset(wall));
    }
    public static TimeOnly PlannedStart(OnDeckItem item, TodayPreferences prefs) => item.PlannedBand switch {
        "morning" => prefs.MorningStart, "afternoon" => new TimeOnly(12,0), "evening" => prefs.EveningStart,
        // Old planning UI stored the default morning band as a literal 7am.
        _ => item.PlannedTime == new TimeOnly(7,0) ? prefs.MorningStart : item.PlannedTime ?? prefs.MorningStart
    };
    public static OnDeckPlan ForDate(TodayDocument doc, DateOnly date) => Plan(new TodayDocument {
        Preferences = doc.Preferences, DayRhythms=doc.DayRhythms, Commitments = doc.Commitments,
        StackItems = doc.StackItems.Where(i => i.NotBefore == date && i.FinishedAt == null && i.StartedAt == null).ToList()
    }, At(doc.Preferences,date,DailyRhythm.ForDate(doc,date).MorningStart));

    public static OnDeckPlan Plan(TodayDocument doc, DateTimeOffset now, ISet<Guid>? keepVisible = null)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(doc.Preferences.TimeZoneId);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var date = DateOnly.FromDateTime(local.DateTime);
        var prefs=DailyRhythm.ForDate(doc,date);
        var night = (prefs.WindDownTime<prefs.MorningStart?date.AddDays(1):date).ToDateTime(prefs.WindDownTime, DateTimeKind.Unspecified);
        var end = new DateTimeOffset(night, zone.GetUtcOffset(night));
        var cursor = now;
        var tonight = new List<OnDeckSlot>(); var tomorrow = new List<OnDeckItem>();
        var occupied = doc.Commitments.Select(c => (Start: c.Start.AddMinutes(-(c.TravelMinutes ?? 0)), c.End)).OrderBy(c => c.Start).ToList();
        // Timed routines reserve their own slot. Flexible tasks fill around them;
        // finishing the flexible list early never pulls a bedtime routine forward.
        var timed=doc.StackItems.Where(i=>!i.CouldDo&&i.FinishedAt==null&&i.ReviewAt==null&&(i.StartedAt==null||i.StartedAt>now)&&!(i.NotBefore>date)&&HasTiming(i)).ToList();
        foreach(var item in timed)
        {
            var requested=TimedStart(item,prefs,date);
            var start=requested>now?requested:now;
            var finish=start.AddMinutes(Math.Max(1,item.Minutes-item.Elapsed(now)/60));
            bool overlap=occupied.Any(b=>start<b.End&&finish>b.Start);
            tonight.Add(new(item,start,finish,overlap||finish>end));
            occupied.Add((start,finish));
        }
        occupied=occupied.OrderBy(b=>b.Start).ToList();
        // An active task remains first; starting a second task pauses it explicitly.
        foreach (var item in doc.StackItems.Where(i => !i.CouldDo && i.FinishedAt == null && i.ReviewAt == null).OrderByDescending(i => i.StartedAt != null)
            .ThenBy(i => {
                var planned=i.NotBefore==date?At(prefs,date,PlannedStart(i,prefs)):now;
                var after=AfterTimedStart(doc,i,date);
                return after>planned?after.Value:planned;
            }))
        {
            if (item.NotBefore > date && item.StartedAt == null) { tomorrow.Add(item); continue; }
            if(timed.Contains(item))continue;
            var minutes = Math.Max(1, item.Minutes - item.Elapsed(now) / 60);
            var start = cursor;
            if(item.StartedAt is {} reserved&&reserved>start)start=reserved;
            if (item.StartedAt == null && item.NotBefore == date)
            { var earliest = At(prefs,date,PlannedStart(item,prefs)); if (earliest > start) start = earliest; }
            if (item.StartedAt == null)
            {
                var earliest=AfterTimedStart(doc,item,date);
                if(item.AfterTimedItemId is {} anchorId && tonight.FirstOrDefault(s=>s.Item.Id==anchorId) is {} anchorSlot && anchorSlot.End>earliest)
                    earliest=anchorSlot.End;
                if(earliest>start)start=earliest.Value;
                foreach (var busy in occupied)
                    if (start < busy.End && start.AddMinutes(minutes) > busy.Start) start = busy.End;
            }
            var finish = start.AddMinutes(minutes);
            var tight = finish > end || (item.StartedAt != null && occupied.Any(b => start < b.End && finish > b.Start));
            // Bedtime is a reference, not a capacity limit. Only explicit deferrals
            // go into Tomorrow; show the time tradeoff rather than hiding work.
            tonight.Add(new(item, start, finish, tight)); cursor = finish;
        }
        return new(tonight.OrderByDescending(s=>s.Item.StartedAt is {} running&&running<=now).ThenBy(s=>s.Start).ToList(), tomorrow);
    }
}
