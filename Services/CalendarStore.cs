using System.Globalization;
using System.Text.RegularExpressions;
using Miche.Mac.Models;

namespace Miche.Mac.Services;

public sealed partial class WorkspaceStore
{
    public static readonly string[] NoteColors = { "#DABBB6", "#CED8BF", "#BCCEDC", "#D6C4DD", "#E8D5AA", "#BFD8D1" };
    public static string MicheNoteColor(Models.Miche miche) => miche.NoteColor ?? NoteColors[(int)(BitConverter.ToUInt32(miche.Id.ToByteArray(), 0) % NoteColors.Length)];
    public void SetMicheNoteColor(Guid id, string color)
    {
        if (!NoteColors.Contains(color)) throw new ArgumentException("Choose a Miche note color.");
        var next = Snapshot; next.Index.Miches.Single(m => m.Id == id).NoteColor = color; Commit(next);
    }
    public void ShowCalendarWidget(Guid micheId, bool visible = true)
    {
        var next = Snapshot;
        if (!next.Index.Miches.Any(m => m.Id == micheId)) throw new ArgumentException("Open this Miche first.");
        next.Calendar.VisibleIn.Remove(micheId); if (visible) next.Calendar.VisibleIn.Add(micheId); Commit(next);
    }
    public void SaveCalendarView(string view, WindowGeometry? geometry = null)
    {
        var next = Snapshot; next.Calendar.View = view;
        if (geometry is not null) next.Calendar.Window = geometry;
        Commit(next, false);
    }
    public static bool CalendarMonthValid(string? month) => month is not null && Regex.IsMatch(month, "^[0-9]{4}-[0-9]{2}$") &&
        DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    public static bool CalendarDateValid(string? date) => date is not null && Regex.IsMatch(date, "^[0-9]{4}-[0-9]{2}-[0-9]{2}$") &&
        DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public void SaveCalendarItems(string month, string? date, IEnumerable<VisionItem> changes, Guid? ownerMicheId = null)
    {
        CheckCalendarTarget(month, date);
        var next = Snapshot; var items = changes.ToArray();
        if (items.Select(i => i.Id).Distinct().Count() != items.Length) throw new ArgumentException("Change each calendar object once.");
        foreach (var content in items)
        {
            var entry = next.Calendar.Items.SingleOrDefault(i => i.Id == content.Id);
            if (entry is null)
            {
                if (content.Kind == "image") throw new ArgumentException("Import the image before placing it.");
                entry = new CalendarItem { Id = content.Id, Month = month, Date = date, OwnerMicheId = ownerMicheId ?? next.Index.RootMicheId };
                next.Calendar.Items.Add(entry);
            }
            else
            {
                if (entry.Month != month || entry.Date != date || entry.Content.Kind != content.Kind || entry.Content.FileName != content.FileName)
                    throw new ArgumentException("This object has moved. Reopen its day before editing it.");
                if(content.Kind=="text"&&content.Left==entry.Content.Left&&content.Top==entry.Content.Top)
                    MakeRoomForNote(next,entry,content.Height,items.Select(i=>i.Id).ToHashSet());
            }
            entry.Content = CopyVision(content); entry.Content.CalendarItemId = null;
            SyncCalendarProxies(next, entry);
        }
        Commit(next);
    }
    public void ScheduleCalendarItem(Guid id, string? date, string? month = null) => ScheduleCalendarItems(new[]{id},date,month);
    public void ScheduleCalendarItems(IEnumerable<Guid> ids, string? date, string? month = null)
    {
        var next = Snapshot;
        if (date is not null && !CalendarDateValid(date)) throw new ArgumentException("Use a date like 2026-10-09.");
        foreach(var id in ids.Distinct())
        {
            var entry = next.Calendar.Items.SingleOrDefault(i => i.Id == id && i.Content.DeletedAt is null) ?? throw new ArgumentException("Restore this note first.");
            var targetMonth = date is null ? month ?? entry.Month : date[..7]; CheckCalendarTarget(targetMonth, date);
            if (entry.Month == targetMonth && entry.Date == date) continue;
            var bottom = next.Calendar.Items.Where(i => i.Month == targetMonth && i.Date == date && i.Content.DeletedAt is null && i.Id != id).Select(i=>i.Content.Top+i.Content.Height+24).DefaultIfEmpty(24).Max();
            entry.Month = targetMonth; entry.Date = date;
            entry.Content.Left = 24; entry.Content.Top = Math.Min(10000,bottom);
        }
        Commit(next);
    }
    public void SetCalendarDeleted(Guid id, bool deleted)
    {
        var next = Snapshot; next.Calendar.Items.Single(i => i.Id == id).Content.DeletedAt = deleted ? DateTimeOffset.UtcNow : null; Commit(next);
    }
    public Guid[] PasteCalendarItems(string month, string? date, Guid workspaceId, IEnumerable<VisionItem> originals)
    {
        CheckCalendarTarget(month,date);var next=Snapshot;var copied=originals.ToArray();
        if(copied.Length is <1 or >1000||copied.Any(i=>i is null))throw new ArgumentException("Copy up to 1000 objects.");
        var ids=new List<Guid>();
        foreach(var source in copied)
        {
            var owner=next.Index.RootMicheId;
            if(source.Kind=="image")
            {
                if(workspaceId!=next.Index.RootMicheId||source.FileName is null||!AllVisionItems(next).Concat(next.Calendar.Items.Select(i=>i.Content)).Any(i=>i.Kind=="image"&&i.FileName==source.FileName))throw new ArgumentException("Copy images from this Mac workspace.");
                owner=Guid.ParseExact(source.FileName[..32],"N");VisionAssetPath(owner,source.FileName);
            }
            var item=CopyVision(source);item.Id=Guid.NewGuid();item.DownstreamIds.Clear();item.CalendarItemId=null;item.DeletedAt=null;item.Left=Math.Min(10000,item.Left+24);item.Top=Math.Min(10000,item.Top+24);
            next.Calendar.Items.Add(new CalendarItem{Id=item.Id,Month=month,Date=date,OwnerMicheId=owner,Content=item});ids.Add(item.Id);
        }
        Commit(next);return ids.ToArray();
    }
    public Guid PlanVisionItem(Guid micheId, Guid itemId, string? date, string month, Guid? artifactId = null)
    {
        CheckCalendarTarget(month, date);
        var next = Snapshot; var item = VisionItemsFor(next, micheId, artifactId).Single(i => i.Id == itemId && i.DeletedAt is null);
        LinkVisionCalendar(next, micheId, item, true);
        var entry = next.Calendar.Items.Single(i => i.Id == item.CalendarItemId);
        var count = next.Calendar.Items.Count(i => i.Month == month && i.Date == date && i.Content.DeletedAt is null && i.Id != entry.Id);
        entry.Month = month; entry.Date = date; entry.Content.DeletedAt = null;
        entry.Content.Left = 24; entry.Content.Top = Math.Min(10000,24 + count * 112);
        TouchArtifact(next, artifactId); Commit(next); return entry.Id;
    }
    private static void LinkVisionCalendar(Workspace next, Guid owner, VisionItem item, bool force = false)
    {
        if (item.CalendarItemId is null && !force && item.LinkedMicheId is null) return;
        CalendarItem entry;
        if (item.CalendarItemId is { } id)
            entry = next.Calendar.Items.SingleOrDefault(i => i.Id == id) ?? throw new ArgumentException("This calendar note is unavailable.");
        else
        {
            entry = new CalendarItem { OwnerMicheId = owner, Month = DateTime.Today.ToString("yyyy-MM"), Content = CopyVision(item) };
            entry.Content.Id = entry.Id; entry.Content.Left = 24;
            entry.Content.Top = Math.Min(10000,24 + next.Calendar.Items.Count(i => i.Month == entry.Month && i.Date is null && i.Content.DeletedAt is null) * 112);
            item.CalendarItemId = entry.Id; next.Calendar.Items.Add(entry);
        }
        CopySharedContent(item, entry.Content);
        entry.Content.CalendarItemId = null;entry.Content.DownstreamIds.Clear();
        SyncCalendarProxies(next, entry);
    }
    public void AttachCalendarShape(Guid shapeId,Guid noteId,double width,double height)
    {
        var next=Snapshot;
        var shape=next.Calendar.Items.SingleOrDefault(i=>i.Id==shapeId&&IsVisionShape(i.Content.Kind)&&i.Content.DeletedAt is null);
        var note=next.Calendar.Items.SingleOrDefault(i=>i.Id==noteId&&i.Content.Kind=="text"&&i.Content.DeletedAt is null);
        if(shape is null||note is null||shape.Month!=note.Month)throw new ArgumentException("Drop a shape onto a note in this month.");
        MakeRoomForNote(next,note,height,new HashSet<Guid>{shapeId,noteId});
        note.Content.NoteShape=shape.Content.Kind;note.Content.Width=width;note.Content.Height=height;
        shape.Content.DeletedAt=DateTimeOffset.UtcNow;SyncCalendarProxies(next,note);Commit(next);
    }
    private static void MakeRoomForNote(Workspace next,CalendarItem note,double height,HashSet<Guid> edited)
    {
        var growth=height-note.Content.Height;if(growth<=0||note.Date is null)return;
        foreach(var below in next.Calendar.Items.Where(i=>i.Month==note.Month&&i.Date==note.Date&&!edited.Contains(i.Id)&&i.Content.DeletedAt is null&&
            Math.Abs(i.Content.Left-note.Content.Left)<16&&i.Content.Top>=note.Content.Top+note.Content.Height))
            below.Content.Top=Math.Min(10000,below.Content.Top+growth);
    }
    private static void CopySharedContent(VisionItem source, VisionItem target)
    {
        target.Text = source.Text; target.LinkedMicheId = source.LinkedMicheId; target.RoundedFrame = source.RoundedFrame; target.NoteShape = source.NoteShape;
        target.FontSize=source.FontSize;target.Width=source.Width;target.Height=source.Height;target.Rotation=source.Rotation;
        target.Table = source.Table is null ? null : TableContent.Copy(source.Table);
        target.FileName = source.FileName;
    }
    private static void SyncCalendarProxies(Workspace next, CalendarItem entry)
    {
        foreach (var proxy in AllVisionItems(next).Where(i => i.CalendarItemId == entry.Id)) CopySharedContent(entry.Content, proxy);
    }
    private static void CheckCalendarTarget(string month, string? date)
    {
        if (!CalendarMonthValid(month) || date is not null && (!CalendarDateValid(date) || !date.StartsWith(month + "-", StringComparison.Ordinal)))
            throw new ArgumentException("Choose a valid calendar month and day.");
    }
    private static void ValidateCalendar(Workspace state, HashSet<Guid> micheIds)
    {
        if (state.Index.Miches.Concat(state.Trash.Select(t => t.Miche)).Any(m => m.NoteColor is not null && !NoteColors.Contains(m.NoteColor)))
            throw new InvalidDataException("Invalid Miche note color.");
        var calendar = state.Calendar;
        if (calendar is null || calendar.Items is null || calendar.VisibleIn is null || calendar.Items.Count > 10000 ||
            calendar.View is not ("month" or "vertical") || calendar.VisibleIn.Distinct().Count() != calendar.VisibleIn.Count || calendar.VisibleIn.Any(id => !micheIds.Contains(id)) ||
            state.Version < 9 && (calendar.Items.Count != 0 || calendar.VisibleIn.Count != 0) ||
            calendar.Items.Any(i => i is null) || calendar.Items.Select(i => i.Id).Distinct().Count() != calendar.Items.Count)
            throw new InvalidDataException("Invalid calendar. Your saved file is intact.");
        if (calendar.Window is { } g && (!double.IsFinite(g.Width) || !double.IsFinite(g.Height) || g.Width < 640 || g.Height < 440 || g.Width > 10000 || g.Height > 10000))
            throw new InvalidDataException("Invalid calendar window.");
        foreach (var entry in calendar.Items)
        {
            ValidateConnections(entry.Content,state.Version);
            var i = entry.Content;
            if (entry.Id == Guid.Empty || !micheIds.Contains(entry.OwnerMicheId) || !CalendarMonthValid(entry.Month) ||
                entry.Date is not null && (!CalendarDateValid(entry.Date) || !entry.Date.StartsWith(entry.Month + "-", StringComparison.Ordinal)) ||
                i is null || i.Id != entry.Id || i.CalendarItemId is not null || i.LinkedMicheId is { } linked && !micheIds.Contains(linked) ||
                i.Kind is not ("text" or "image" or "table") && !IsVisionShape(i.Kind) ||
                i.Text is null || i.Text.Length > 20000 || i.Kind == "text" && string.IsNullOrWhiteSpace(i.Text) ||
                i.Kind == "image" && (i.FileName is null || !Regex.IsMatch(i.FileName, "^" + entry.OwnerMicheId.ToString("N") + "/[a-f0-9]{32}\\.(png|jpg)$")) ||
                i.Kind != "image" && i.FileName is not null || i.NoteShape is { } noteShape && (state.Version < 10 || i.Kind != "text" || !IsVisionShape(noteShape)) ||
                i.Kind != "text" && (i.LinkedMicheId is not null || i.RoundedFrame) ||
                i.Kind == "table" && i.Table is null || i.Kind != "table" && i.Table is not null ||
                !double.IsFinite(i.Left) || i.Left < 0 || i.Left > 10000 || !double.IsFinite(i.Top) || i.Top < 0 || i.Top > 10000 ||
                !double.IsFinite(i.Width) || i.Width <= 0 || i.Width > 4000 || !double.IsFinite(i.Height) || i.Height <= 0 || i.Height > 4000 ||
                !double.IsFinite(i.FontSize) || i.FontSize < 8 || i.FontSize > 200 || !double.IsFinite(i.Rotation) || Math.Abs(i.Rotation) > 360)
                throw new InvalidDataException("Invalid calendar object. Your saved file is intact.");
            if (i.Table is not null) TableContent.Validate(i.Table);
        }
    }
}
