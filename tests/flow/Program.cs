using System.Text.Json;
using Miche.Mac.Models;
using Miche.Mac.Services;

var root=Path.Combine(Path.GetTempPath(),"miche-flow-checks-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var origin=Guid.NewGuid();var now=new DateTimeOffset(2026,10,6,10,0,0,TimeSpan.Zero);
int checks=0;
void Check(bool pass,string why){if(!pass)throw new Exception(why);checks++;}
void Reject(Action action,string why){bool rejected=false;try{action();}catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException){rejected=true;}Check(rejected,why);}
FlowRepository New(string name){var r=new FlowRepository(Path.Combine(root,name+".json"),origin);r.Change(d=>d.Preferences.TimeZoneId="UTC",now);return r;}
OnDeckItem Item(FlowRepository r,Guid id)=>r.Snapshot().StackItems.Single(i=>i.Id==id);
try
{
    using(var r=New("clock"))
    {
        var a=r.AddToStack("first");var b=r.AddToStack("second");var c=r.AddToStack("third");var tomorrow=r.AddToStack("tomorrow",new DateOnly(2026,10,7));
        Check(r.Snapshot().StackItems.All(i=>i.StartedAt==null),"Adding tasks started a timer");
        var rev=r.Snapshot().Revision;Check(!r.AdvanceStackFlow(now)&&r.Snapshot().Revision==rev,"Idle clock wrote or started flow");
        r.FocusStackItem(a,now);r.CompleteSelectedStackItem(c,now.AddMinutes(1));
        Check(Item(r,c).FinishedAt!=null&&Item(r,a).StartedAt==now&&Item(r,b).StartedAt==null,"Selected completion touched a different live task");
        Check(r.AdvanceStackFlow(now.AddMinutes(25)),"Sleep catchup did not run");
        Check(Item(r,a).ReviewAt==now.AddMinutes(10)&&Item(r,b).ReviewAt==now.AddMinutes(20),"Catchup lost absolute boundaries");
        Check(Item(r,a).TimeBlocks.Single().Seconds==600&&!Item(r,a).TimeBlocks.Single().EndedEarly,"Expiry block was inaccurate");
        Check(Item(r,a).FinishedAt==null&&Item(r,b).FinishedAt==null,"Expiry claimed completion");
        Check(Item(r,tomorrow).StartedAt==null&&r.Snapshot().StackItems.All(i=>i.StartedAt==null),"Catchup started a future date or exhausted queue");
        rev=r.Snapshot().Revision;Check(!r.AdvanceStackFlow(now.AddMinutes(25))&&rev==r.Snapshot().Revision,"Catchup was not idempotent");
        r.RequeueReviewedStackItem(a,now.AddMinutes(26));Check(Item(r,a).StartedAt==now.AddMinutes(26)&&Item(r,a).TimeBlocks.Count==1,"Review requeue lost history or failed explicit start");
        r.ResetStackTimer(a,now.AddMinutes(27));Check(Item(r,a).StartedAt==null&&Item(r,a).ElapsedSeconds==0&&Item(r,a).TimeBlocks.Count==1,"Reset lost prior history");
        r.FocusStackItem(a,now.AddMinutes(28));r.CompleteSelectedStackItem(a,now.AddMinutes(29));
        Check(Item(r,a).TimeBlocks.Last().EndedEarly&&Item(r,a).TimeBlocks.Last().Seconds==60,"Early completion journal inaccurate");
        Reject(()=>r.FocusStackItem(tomorrow,now),"Future task could start early");
    }
    using(var r=New("pause"))
    {
        var a=r.AddToStack("pause me");var b=r.AddToStack("live");r.FocusStackItem(a,now);r.PauseStackItem(a,now.AddSeconds(90));
        Check(Item(r,a).ElapsedSeconds==90&&Item(r,a).StartedAt==null,"Pause used ticks rather than elapsed time");
        r.FocusStackItem(a,now.AddMinutes(3));r.FocusStackItem(b,now.AddMinutes(4));
        Check(Item(r,a).ElapsedSeconds==150&&Item(r,b).StartedAt==now.AddMinutes(4),"Explicit switch lost accumulated seconds");
        r.Change(d=>d.StackItems.RemoveAll(i=>i.Id==b),now.AddMinutes(5));
        Check(r.Snapshot().StackItems.All(i=>i.StartedAt==null),"Delete handed off flow");
        Check(r.Undo()&&Item(r,b).StartedAt==now.AddMinutes(4),"Delete undo lost canonical task/timer");
    }
    using(var r=New("timing"))
    {
        var flex=r.AddToStack("flex");var timed=r.AddToStack("fixed");r.Change(d=>d.StackItems.Single(i=>i.Id==timed).FixedTime=new TimeOnly(11,0),now);
        var start=OnDeckPlanner.Plan(r.Snapshot(),now).Tonight.Single(s=>s.Item.Id==timed).Start;
        r.ReorderFlowStack(new[]{timed,flex},flex);
        Check(Item(r,flex).AfterTimedItemId==timed&&OnDeckPlanner.Plan(r.Snapshot(),now).Tonight.Single(s=>s.Item.Id==flex).Start>=start.AddMinutes(10),"Drag crossed anchor without storing relationship");
        Check(Item(r,timed).FixedTime==new TimeOnly(11,0)&&r.Snapshot().StackItems.All(i=>i.StartedAt==null),"Reorder changed reservation or started timer");
        Reject(()=>r.ReorderFlowStack(new[]{flex,timed},timed),"Timed task could reorder");
        r.SetStackDuration(flex,25);Check(Item(r,flex).Minutes==25&&Item(r,timed).FixedTime==new TimeOnly(11,0),"Resize changed fixed reservation");
        r.Change(d=>d.StackItems.RemoveAll(i=>i.Id==timed),now);
        Check(OnDeckPlanner.AfterTimedStart(r.Snapshot(),Item(r,flex),new DateOnly(2026,10,6))==null,"Deleted anchor left invisible constraint");
        var bed=r.AddToStack("before bed");r.Change(d=>d.StackItems.Single(i=>i.Id==bed).BeforeBedtimeMinutes=30,now);
        r.SetDayRhythm(new DateOnly(2026,10,7),false,new TimeOnly(1,0));r.SetDayRhythm(new DateOnly(2026,10,8),true,new TimeOnly(8,0));
        var prefs=DailyRhythm.ForDate(r.Snapshot(),new DateOnly(2026,10,8));
        Check(prefs.MorningStart==new TimeOnly(8,0)&&prefs.WindDownTime==new TimeOnly(1,0),"Wake/bed failed independent inheritance");
        Check(OnDeckPlanner.TimedStart(Item(r,bed),prefs,new DateOnly(2026,10,8))==new DateTimeOffset(2026,10,9,0,30,0,TimeSpan.Zero),"After-midnight bed used wrong day");
        r.SetDayRhythm(new DateOnly(2026,10,6),true,new TimeOnly(6,0));Check(DailyRhythm.ForDate(r.Snapshot(),new DateOnly(2026,10,8)).MorningStart==new TimeOnly(8,0),"Earlier rhythm edit erased later explicit wake");
        prefs.TimeZoneId="America/Los_Angeles";Check(OnDeckPlanner.At(prefs,new DateOnly(2026,3,8),new TimeOnly(2,30)).Hour==3,"DST missing wall time did not move forward");
    }
    var draftUndo=Path.Combine(root,"draft-undo.json");
    using(var r=new FlowRepository(draftUndo,origin))
    {
        var id=r.AddToStack("draft undo task");r.SetStackDuration(id,25);
        r.SaveStackDraft("newer today");r.SaveStackDraft("newer tomorrow",new DateOnly(2026,10,7));r.SaveCouldDoDraft("newer possibility");
        Check(r.Undo()&&Item(r,id).Minutes==10,"Task undo did not reverse task change");
        Check(r.Snapshot().StackDraft=="newer today"&&r.Snapshot().StackDayDrafts["2026-10-07"]=="newer tomorrow"&&r.Snapshot().CouldDoDraft=="newer possibility","Task undo clobbered unrelated newer drafts");
    }
    using(var r=new FlowRepository(draftUndo,origin))Check(r.Snapshot().StackDraft=="newer today"&&r.Snapshot().StackDayDrafts["2026-10-07"]=="newer tomorrow"&&r.Snapshot().CouldDoDraft=="newer possibility","Undo/restart lost newer scoped drafts");
    var resumed=Path.Combine(root,"resumed.json");Guid live,next;
    using(var r=new FlowRepository(resumed,origin))
    {
        r.Change(d=>d.Preferences.TimeZoneId="UTC",now);live=r.AddToStack("already running");next=r.AddToStack("next block");r.FocusStackItem(live,now);
    }
    using(var r=new FlowRepository(resumed,origin))
    {
        Check(Item(r,live).StartedAt==now&&Item(r,next).StartedAt==null,"Loading reset prior timer or started another one");
        Check(r.AdvanceStackFlow(now.AddMinutes(12))&&Item(r,live).ReviewAt==now.AddMinutes(10)&&Item(r,next).StartedAt==now.AddMinutes(10),"Restart catchup used reopen time instead of prior boundary");
        var revision=r.Snapshot().Revision;Check(!r.AdvanceStackFlow(now.AddMinutes(12))&&r.Snapshot().Revision==revision,"Repeated restart reconciliation wrote twice");
        r.CompleteSelectedStackItem(live,now.AddMinutes(13));Check(Item(r,live).FinishedAt!=null&&Item(r,next).StartedAt==now.AddMinutes(10),"Completing reviewed work interrupted current block");
    }
    using(var r=New("waiting"))
    {
        var future=r.AddToStack("reserved future");var eligible=r.AddToStack("eligible now");
        r.Change(d=>{var t=d.StackItems.Single(i=>i.Id==future);t.FixedTime=new TimeOnly(11,0);t.StartedAt=now.AddHours(1);},now);
        r.CompleteSelectedStackItem(future,now.AddMinutes(1));Check(Item(r,eligible).StartedAt==null&&Item(r,future).TimeBlocks.Count==0,"Completing waiting task handed off or invented performed time");
        r.FocusStackItem(eligible,now.AddMinutes(2));r.SetStackDuration(eligible,20);Check(Item(r,eligible).StartedAt==now.AddMinutes(2),"Resize restarted active timer");
    }
    var persistence=Path.Combine(root,"persist.json");Guid task;
    using(var r=new FlowRepository(persistence,origin))
    {
        task=r.AddToStack("restart task");r.SaveStackDraft("today draft");r.SaveStackDraft("tomorrow draft",new DateOnly(2026,10,7));r.SaveCouldDoDraft("possibility draft");r.SaveWindow(new WindowGeometry {Width=620,Height=760,X=12,Y=23});
        Check(r.Snapshot().ContentRevision==1,"Draft/geometry writes changed content revision");
        Reject(()=>{using var other=new FlowRepository(persistence,origin);},"Second writer opened Flow");
        r.Changed+=()=>throw new Exception("view fixture");r.SetStackDuration(task,15);Check(Item(r,task).Minutes==15&&r.LastNotificationWarning!=null,"Observer made durable commit look failed");
        Directory.CreateDirectory(persistence+".bak.blocked");File.Delete(persistence+".bak");Directory.Move(persistence+".bak.blocked",persistence+".bak");
        var bytes=File.ReadAllBytes(persistence);var before=r.Snapshot().Revision;
        Reject(()=>r.SetStackDuration(task,20),"Blocked backup did not fail");Check(File.ReadAllBytes(persistence).SequenceEqual(bytes)&&r.Snapshot().Revision==before&&Item(r,task).Minutes==15,"Failed save published memory or changed disk");
        Directory.Delete(persistence+".bak");
    }
    using(var r=new FlowRepository(persistence,origin))
    {
        Check(r.OriginMicheId==origin&&Item(r,task).StartedAt==null&&Item(r,task).Minutes==15,"Restart lost identity or started idle timer");
        Check(r.Snapshot().StackDraft=="today draft"&&r.Snapshot().StackDayDrafts["2026-10-07"]=="tomorrow draft"&&r.Snapshot().CouldDoDraft=="possibility draft"&&r.Window!.Width==620,"Restart lost drafts/geometry");
        var bytes=File.ReadAllBytes(persistence);File.AppendAllText(persistence," ");Reject(()=>r.AddToStack("external overwrite"),"External edit was overwritten");Check(r.Snapshot().StackItems.Count==1,"Failed external edit published memory");File.WriteAllBytes(persistence,bytes);
    }
    Reject(()=>{using var r=new FlowRepository(persistence,Guid.NewGuid());},"Wrong workspace opened Flow");
    var invalid=Path.Combine(root,"invalid.json");var malformed="{\"Version\":1,\"OriginMicheId\":\""+origin+"\"}";File.WriteAllText(invalid,malformed);
    Reject(()=>{using var r=new FlowRepository(invalid,origin);},"Missing document silently became empty Flow");Check(File.ReadAllText(invalid)==malformed,"Bad envelope was replaced");
    using(var r=New("undo")){var id=r.AddCouldDo("possibility");r.ScheduleCouldDo(id,now);Check(Item(r,id).Id==id&&!Item(r,id).CouldDo&&Item(r,id).StartedAt==null,"Could do scheduling copied identity or auto-started");r.Undo();Check(Item(r,id).CouldDo,"Scheduling undo lost bucket");while(r.CanUndo)r.Undo();bool observed=false;r.Changed+=()=>observed=r.CanUndo;var fresh=r.AddToStack("last undo");r.Undo();Check(!r.CanUndo&&!observed,"Last undo left stale view state");}
    Console.WriteLine($"PASS: {checks} Flow persistence/planning/clock checks. Synthetic data only.");
}
finally{Directory.Delete(root,true);}
