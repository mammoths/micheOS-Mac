using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Miche.Mac.Models;

namespace Miche.Mac.Services;

// One local Flow journal. The coordinator never opens a mailbox or a network connection.
public sealed partial class FlowRepository : IDisposable
{
    private readonly object _sync = new();
    private readonly FileStream _writer;
    private readonly List<TodayDocument> _undo = new();
    private TodayDocument _document;
    private LocalFlowEnvelope _envelope;
    private string? _fingerprint;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public string FilePath { get; }
    public Guid OriginMicheId => _envelope.OriginMicheId;
    public WindowGeometry? Window => Clone(_envelope.Window);
    public string? LastNotificationWarning { get; private set; }
    public bool CanUndo { get { lock (_sync) return _undo.Count > 0; } }
    public event Action? Changed;

    public FlowRepository(string path, Guid originMicheId)
    {
        FilePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        _writer = new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var bytes = ReadDisk();
            _envelope = bytes is null ? new LocalFlowEnvelope { OriginMicheId = originMicheId } :
                JsonSerializer.Deserialize<LocalFlowEnvelope>(bytes,Json) ?? throw new InvalidDataException("Flow is empty. Nothing was replaced.");
            Require(_envelope.Version == 1 && _envelope.OriginMicheId == originMicheId && originMicheId != Guid.Empty, "Flow belongs to a different workspace or unsupported version. Nothing was replaced.");
            _document = _envelope.Document ?? throw new InvalidDataException("Flow has no document. Nothing was replaced.");
            Validate(_document); ValidateWindow(_envelope.Window); _fingerprint = Fingerprint(bytes);
        }
        catch { _writer.Dispose(); throw; }
    }
    private byte[]? ReadDisk()
    {
        if (!File.Exists(FilePath)) return null;
        Require((File.GetAttributes(FilePath) & FileAttributes.ReparsePoint) == 0, "A linked Flow document cannot be overwritten.");
        Require(new FileInfo(FilePath).Length <= 32 * 1024 * 1024, "Flow is too large to open safely. Nothing was replaced.");
        return File.ReadAllBytes(FilePath);
    }
    public TodayDocument Snapshot() { lock (_sync) return Clone(_document); }
    public void Change(Action<TodayDocument> update) => Commit(update,true,true);
    public void Change(Action<TodayDocument> update,DateTimeOffset now) => Commit(update,true,true,now);
    private void Commit(Action<TodayDocument> update,bool contentChange,bool undoable,DateTimeOffset? now=null,WindowGeometry? geometry=null)
    {
        lock(_sync)
        {
            var next=Clone(_document); update(next);
            next.Revision=checked(_document.Revision+1);
            next.ContentRevision=contentChange?checked(_document.ContentRevision+1):_document.ContentRevision;
            Validate(next);
            if(WindDownInputs(next)!=WindDownInputs(_document)) SetStackWindDown(next,now??DateTimeOffset.UtcNow);
            var envelope=new LocalFlowEnvelope { OriginMicheId=OriginMicheId,Document=next,Window=Clone(geometry??_envelope.Window) };
            ValidateWindow(envelope.Window);
            var previous=ReadDisk();
            if(Fingerprint(previous)!=_fingerprint) throw new IOException("Flow changed outside this session. Nothing was overwritten; reopen Miche to load it.");
            var bytes=JsonSerializer.SerializeToUtf8Bytes(envelope,Json);
            var temp=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { file.Write(bytes);file.Flush(true); }
                if(previous is not null) File.Copy(FilePath,FilePath+".bak",true);
                File.Move(temp,FilePath,true);
            }
            finally { if(File.Exists(temp))File.Delete(temp); }
            if(undoable) { _undo.Add(Clone(_document));if(_undo.Count>60)_undo.RemoveAt(0); }
            _envelope=envelope;_document=next;_fingerprint=Fingerprint(bytes);
        }
        NotifyChanged();
    }
    private void NotifyChanged()
    {
        LastNotificationWarning=null;
        if(Changed is not null) foreach(Action callback in Changed.GetInvocationList())
            try { callback(); } catch(Exception) { LastNotificationWarning="Flow saved. Reopen its window if a view did not refresh."; }
    }
    public void SaveWindow(WindowGeometry geometry)
    {
        if(JsonSerializer.Serialize(geometry)==JsonSerializer.Serialize(Window))return;
        Commit(_=>{},false,false,geometry:geometry);
    }
    public bool Undo()
    {
        lock(_sync)
        {
            if(_undo.Count==0)return false;
            var previous=Clone(_undo[^1]);
            // Undo changes intent and timer state, retaining canonical record identities.
            Commit(d=> { d.StackItems=previous.StackItems;
                // Draft saves are independent, non-undoable editor state. Task undo preserves newer input.
                d.Preferences=previous.Preferences;d.DayRhythms=previous.DayRhythms;d.Treat=previous.Treat; },true,false);
            _undo.RemoveAt(_undo.Count-1);NotifyChanged();return true;
        }
    }
    private static void ValidateWindow(WindowGeometry? value)
    {
        if(value is null)return;
        Require(double.IsFinite(value.Width)&&double.IsFinite(value.Height)&&value.Width is >=320 and <=10000&&value.Height is >=240 and <=10000,"Invalid Flow window size.");
    }
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static string? Fingerprint(byte[]? value) => value is null?null:Convert.ToHexString(SHA256.HashData(value));
    private static void Require([DoesNotReturnIf(false)] bool condition,string message) {if(!condition)throw new InvalidDataException(message);}
    private static void Text(string? value,int maximum,string label,bool required=false) => Require(value is not null&&value.Length<=maximum&&(!required||!string.IsNullOrWhiteSpace(value)),label+" is missing or too long.");
    private static void Unique(IEnumerable<Guid> values,string label) {var ids=values.ToArray();Require(ids.All(id=>id!=Guid.Empty)&&ids.Distinct().Count()==ids.Length,label+" has invalid or duplicate IDs.");}
    private static void Nutrient(decimal? value) => Require(value is null or >=0 and <=10000,"Nutrition values must be non-negative or unknown.");
    private static void Zone(string zone)
    {
        Text(zone,200,"Time zone",true);
        try {TimeZoneInfo.FindSystemTimeZoneById(zone);}catch(Exception ex) when(ex is TimeZoneNotFoundException or InvalidTimeZoneException) {throw new InvalidDataException("Choose a recognized time zone.",ex);}
    }
    public void Dispose() => _writer.Dispose();
    private static void Validate(TodayDocument d)
    {
        ValidateStack(d);
        Require(d.Version == 1, "This version of Miche cannot read this Today document. Nothing was replaced.");
        Require(d.Revision >= 0 && d.ContentRevision >= 0, "Invalid Today revision.");
        Require(d.Preferences != null && d.Days != null && d.Meals != null && d.Pantry != null && d.Commitments != null && d.Messages != null && d.Proposals != null && d.Receipts != null && d.Snapshots != null, "Today contains an unreadable collection.");
        var p = d.Preferences!;
        ValidateDayRhythms(d);
        Zone(p.TimeZoneId);
        Require(p.MorningStart < p.WorkStart && p.WorkStart < p.EveningStart, "Chapter times must run morning, afternoon, then evening.");
        Require(p.WindDownTime != p.MorningStart, "Wind-down and morning need different start times.");
        Require(p.WindDownActivities != null && p.QuietMicheIds != null && p.WeeklyMovement != null, "Today preferences are incomplete.");
        Require(p.WindDownActivities!.Count <= 100 && p.QuietMicheIds!.Count <= 1000 && p.WeeklyMovement!.Count <= 7, "Too many preferences.");
        foreach (var a in p.WindDownActivities) Text(a, 500, "Wind-down activity");
        foreach (var a in p.WeeklyMovement!) { Require(Enum.IsDefined(a.Key), "Invalid weekday."); Text(a.Value, 2000, "Movement rhythm"); }
        Text(p.AudiobookTitle, 500, "Audiobook");
        Nutrient(p.ProteinTargetGrams); Nutrient(p.FiberTargetGrams);
        Require(p.ProteinTargetGrams != 0 && p.FiberTargetGrams != 0, "Use a positive nutrition target, or leave it unset.");
        Text(d.ReplyDraft, 16000, "Reply draft");
        Require(d.Days!.All(x => x != null) && d.Meals!.All(x => x != null) && d.Commitments!.All(x => x != null) && d.Messages!.All(x => x != null) && d.Proposals!.All(x => x != null) && d.Snapshots!.All(x => x != null), "Today contains an unreadable entry.");
        Require(d.Days!.Select(day => day.Date).Distinct().Count() == d.Days.Count, "Duplicate day plans.");
        foreach (var day in d.Days) ValidateDay(day);
        Unique(d.Meals!.Select(m => m.Id), "Meals");
        foreach (var meal in d.Meals) { Text(meal.Name, 500, "Meal", true); Nutrient(meal.ProteinGrams); Nutrient(meal.FiberGrams); }
        foreach (var food in d.Pantry!) Text(food, 500, "Pantry item", true);
        Unique(d.Commitments!.Select(c => c.Id), "Commitments");
        foreach (var c in d.Commitments)
        {
            Text(c.Title, 1000, "Commitment", true); Text(c.Location, 1000, "Location"); Zone(c.TimeZoneId);
            Require(c.End > c.Start, "A commitment must end after it starts.");
            Require(c.TravelMinutes == null || c.TravelMinutes is >= 0 and <= 1440, "Travel buffer must be between 0 and 1440 minutes.");
        }
        Unique(d.Messages!.Select(m => m.Id), "Messages");
        foreach (var m in d.Messages)
        {
            Require(m.Role is "user" or "bud" or "system", "Invalid message role.");
            Text(m.Text, 16000, "Message", true); ValidateChoices(m.Choices);
        }
        Unique(d.Proposals!.Select(p => p.Id), "Proposals");
        foreach (var proposal in d.Proposals) ValidateProposal(proposal, false);
        Unique(d.Receipts!, "Proposal receipts");
        Unique(d.Snapshots!.Select(s => s.Id), "Snapshot receipts");
        Require(d.Snapshots.Count <= 256 && d.Snapshots.All(s => s.ContentRevision >= 0), "Invalid snapshot receipts.");
    }

    private static void ValidateDay(TodayDay day)
    {
        Require(day != null && day.Date.Year >= 2000 && day.Date.Year <= 2200, "Invalid plan date.");
        Require(day!.State is "prepared" or "accepted", "Invalid plan state.");
        Require(day.Origin is "local" or "bud" or "user", "Invalid plan origin.");
        Text(day.WindDownSuggestion, 2000, "Wind-down suggestion");
        Require(day.Chapters != null && day.PlannedMeals != null && day.MealLogs != null, "Incomplete day plan.");
        Require(day.Chapters!.All(x => x != null) && day.PlannedMeals!.All(x => x != null) && day.MealLogs!.All(x => x != null), "Day plan contains an unreadable entry.");
        Require(day.Chapters!.Count == 3 && day.Chapters.Select(c => c.Key).OrderBy(k => k).SequenceEqual(new[] { "life", "morning", "work" }), "A day needs one body, work, and life chapter.");
        foreach (var c in day.Chapters)
        {
            Text(c.Title, 100, "Chapter title", true); Text(c.NextAction, 4000, "Next action"); Text(c.GentlerAction, 2000, "Gentler option"); Text(c.Note, 4000, "Chapter note");
            Require(c.State is "ready" or "done" or "skipped", "Invalid chapter state.");
            Require(c.Minutes == null || c.Minutes is >= 0 and <= 1440, "Movement duration must be between 0 and 1440 minutes.");
        }
        Unique(day.PlannedMeals!.Select(m => m.Id), "Planned meals");
        Unique(day.MealLogs!.Select(m => m.Id), "Meal logs");
        foreach (var m in day.PlannedMeals) ValidateMeal(m.Name, m.Portions, m.ProteinGrams, m.FiberGrams);
        foreach (var m in day.MealLogs) ValidateMeal(m.Name, m.Portions, m.ProteinGrams, m.FiberGrams);
    }

    private static void ValidateMeal(string name, decimal portions, decimal? protein, decimal? fiber)
    { Text(name, 500, "Meal name", true); Require(portions is > 0 and <= 100, "Portions must be greater than zero and at most 100."); Nutrient(protein); Nutrient(fiber); }

    private static void ValidateChoices(List<string>? choices)
    { Require(choices != null && choices.Count <= 6, "Messages support at most six choices."); foreach (var choice in choices!) Text(choice, 200, "Reply choice", true); }

    private static void ValidateProposal(TodayProposal p, bool external)
    {
        Require(p.Version == 1 && p.Id != Guid.Empty && p.SnapshotId != Guid.Empty && p.BaseRevision >= 0, "Invalid proposal envelope.");
        if (external) Require(p.CreatedAt != default && p.CreatedAt <= DateTimeOffset.UtcNow.AddMinutes(10), "Proposal has an invalid creation time.");
        Text(p.Message, 16000, "Bud message"); ValidateChoices(p.Choices);
        Require(!string.IsNullOrWhiteSpace(p.Message) || p.Plan != null, "Proposal needs a message or a plan.");
        Require(p.Status is "pending" or "applied" or "stale" or "dismissed", "Invalid proposal state.");
        Text(p.StatusReason, 2000, "Proposal status");
        if (p.Plan != null)
        {
            ValidateDay(p.Plan);
            Require(p.Plan.MealLogs.Count == 0, "Bud may propose meals but cannot claim you ate them.");
            if (external)
            {
                Require(p.Plan.Chapters.All(c => c.State == "ready" && c.Minutes == null), "Bud cannot mark movement or work completed.");
                Require(p.Plan.PlannedMeals.Count <= 20, "Too many proposed meals.");
            }
        }
    }
}
