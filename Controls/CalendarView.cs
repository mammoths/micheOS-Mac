using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Miche.Mac.Models;
using Miche.Mac.Services;
using CalendarItem = Miche.Mac.Models.CalendarItem;

namespace Miche.Mac.Controls;

public sealed partial class CalendarView : UserControl, IDisposable
{
    private readonly WorkspaceSession _session;
    private DateOnly _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private string? _day;
    private string _view;
    private readonly Grid _root = new() { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
    private readonly TextBlock _title = CalendarPaper.Label("", 28, "#453E38", "Georgia");
    private readonly TextBlock _subtitle = CalendarPaper.Label("", 11);
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ContentControl _calendar = new();
    private readonly TextBlock _editorTitle = CalendarPaper.Label("month margin", 22, "#453E38", "Georgia");
    private readonly TextBlock _editorHint = CalendarPaper.Label("loose thoughts → a day when you’re ready", 11);
    private readonly Border _marginButton = new();
    private readonly Button _monthButton, _verticalButton;
    private readonly Dictionary<string, Border> _days = new();
    private readonly Dictionary<string, Bitmap> _images = new();
    private readonly TextBlock _status = CalendarPaper.Label("click a day and keep writing · drag a shape onto a note to wrap it", 11);
    private PreviewGesture? _drag;
    private sealed record PreviewGesture(Guid Id, Point Start, IPointer Pointer, int Clicks) { public bool Started { get; set; } }
    public VisionView Editor { get; } = new();
    public IReadOnlyDictionary<string, Border> DayCards => _days;
    public string ViewMode => _view;
    public string Month => _month.ToString("yyyy-MM");
    public string? SelectedDate => _day;
    public ScrollViewer MonthScroll => _scroll;
    public event Action? CloseRequested;
    public CalendarView(WorkspaceSession session)
    {
        _session = session; _view = session.Store.Snapshot.Calendar.View;
        Background = Brush.Parse("#F3ECDD"); Foreground = Brush.Parse("#453E38"); Content = _root; CalendarPaper.Style(this);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(26,20,20,16) };
        var heading = new StackPanel { Spacing = 5 }; heading.Children.Add(_subtitle); heading.Children.Add(_title); header.Children.Add(heading);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(CalendarPaper.Button("‹", () => ChangeMonth(-1)));
        actions.Children.Add(CalendarPaper.Button("today", () => ShowDate(DateTime.Today.ToString("yyyy-MM-dd"))));
        actions.Children.Add(CalendarPaper.Button("›", () => ChangeMonth(1)));
        _monthButton = CalendarPaper.Button("month", () => SetView("month"));
        _verticalButton = CalendarPaper.Button("vertical", () => SetView("vertical"));
        actions.Children.Add(_monthButton); actions.Children.Add(_verticalButton);
        Grid.SetColumn(actions, 1); header.Children.Add(actions); _root.Children.Add(header);
        var spread = new Grid { ColumnDefinitions = new ColumnDefinitions("*,340"), Margin = new Thickness(20,0,20,0) };
        _scroll.Content = _calendar; spread.Children.Add(_scroll);
        var margin = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), Margin = new Thickness(18,0,0,0) };
        _marginButton.Child = CalendarPaper.Button("↖ month margin", () => ShowMargin());
        margin.Children.Add(_marginButton);
        var editorHeading = new StackPanel { Spacing = 6, Margin = new Thickness(10,12,8,12) };
        editorHeading.Children.Add(_editorTitle); editorHeading.Children.Add(_editorHint); Grid.SetRow(editorHeading,1); margin.Children.Add(editorHeading);
        Editor.SetCalendarTarget(Month); Editor.Connect(session); Editor.CalendarDropRequested = DropObjects;
        Editor.CloseRequested+=()=>CloseRequested?.Invoke();
        var editorFrame = new Border { Background = Brush.Parse("#F7F1E5"), BorderBrush = Brush.Parse("#D6CAB8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = Editor };
        Grid.SetRow(editorFrame,2); margin.Children.Add(editorFrame); Grid.SetColumn(margin,1); spread.Children.Add(margin);
        Grid.SetRow(spread,1); _root.Children.Add(spread);
        _status.Margin = new Thickness(26,12); Grid.SetRow(_status,2); _root.Children.Add(_status);
        _session.Changed += Refresh; _session.ErrorOccurred += Error;
        _scroll.SizeChanged+=(_,_)=>Refresh();
        AddHandler(PointerMovedEvent, MovePreview, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, ReleasePreview, RoutingStrategies.Tunnel);
        PointerCaptureLost += (_, _) => CancelPreview();
        InitializeInlineWriting();
        Refresh();
    }
    private void Error(string message) => _status.Text = message;
    public bool PrepareToLeave() { CancelPreview(); return CommitInline() && Editor.PrepareToLeave(); }
    public bool ShowDate(string date)
    {
        if (!WorkspaceStore.CalendarDateValid(date) || !CommitInline()) return false;
        if (!Editor.SetCalendarTarget(date[..7], date)) return false;
        _day = date; var parsed = DateOnly.ParseExact(date, "yyyy-MM-dd"); _month = new(parsed.Year,parsed.Month,1); Refresh(); return true;
    }
    public bool ShowMargin()
    {
        if(!CommitInline())return false;
        if (!Editor.SetCalendarTarget(Month)) return false;
        _day = null; Refresh(); return true;
    }
    public bool ChangeMonth(int direction)
    {
        if (_month.Year == 1 && _month.Month == 1 && direction < 0 || _month.Year == 9999 && _month.Month == 12 && direction > 0) return false;
        if (!PrepareToLeave()) return false;
        _month = _month.AddMonths(direction); _day = null; Editor.SetCalendarTarget(Month); _scroll.Offset = default; Refresh(); return true;
    }
    public bool SetView(string mode)
    {
        if (mode is not ("month" or "vertical") || !PrepareToLeave()) return false;
        if (!_session.Act(() => _session.Store.SaveCalendarView(mode))) return false;
        _view = mode; _scroll.Offset = default; Refresh(); return true;
    }
    public void Refresh()
    {
        _title.Text = _month.ToString("MMMM yyyy").ToLowerInvariant(); _subtitle.Text = "日历 / MICHE CALENDAR";
        _monthButton.Background = Brush.Parse(_view == "month" ? "#E5DACA" : "#F3ECDD");
        _verticalButton.Background = Brush.Parse(_view == "vertical" ? "#E5DACA" : "#F3ECDD");
        _editorTitle.Text = _day is null ? "month margin" : DateOnly.Parse(_day).ToString("dddd, d").ToLowerInvariant();
        _editorHint.Text = _day is null ? "loose thoughts → a day when you’re ready" : "a little room for everything you had in mind";
        StyleInlineMention();
        if(_inlineEditor is not null)return; // Keep the caret and unsaved writing through data/layout notifications.
        _days.Clear(); _noteHosts.Clear(); _previews.Clear();
        var items = _session.Store.Snapshot.Calendar.Items.Where(i => i.Month == Month && i.Date is not null && i.Content.DeletedAt is null).ToArray();
        if (_view == "month") BuildMonth(items); else BuildVertical(items);
    }
    private void BuildMonth(CalendarItem[] items)
    {
        var page = new StackPanel { Width = Math.Max(630,_scroll.Bounds.Width-16) };
        var headings = new UniformGrid { Columns = 7, Margin = new Thickness(0,0,0,8) };
        foreach (var day in new[] { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" })
            headings.Children.Add(CalendarPaper.Label(day, 11, day == "SUN" ? "#A74D43" : "#887C6F"));
        page.Children.Add(headings);
        var grid = new UniformGrid { Columns = 7 };
        for (var i = 0; i < (int)_month.DayOfWeek; i++) grid.Children.Add(new Border { MinHeight = 146, Background = Brush.Parse("#ECE4D5"), BorderBrush = Brush.Parse("#D6CAB8"), BorderThickness = new Thickness(.5) });
        for (var day = 1; day <= DateTime.DaysInMonth(_month.Year,_month.Month); day++)
        {
            var date = _month.AddDays(day-1); var key = date.ToString("yyyy-MM-dd");
            var notes = items.Where(i => i.Date == key).OrderBy(i => i.Content.Top).ToArray();
            var content = new StackPanel { Spacing = 6, Margin = new Thickness(7,7,7,9) };
            var number = CalendarPaper.Label(day.ToString("00"), 19, date.DayOfWeek == DayOfWeek.Sunday || key == DateTime.Today.ToString("yyyy-MM-dd") ? "#A74D43" : "#776B5D", "Georgia");
            content.Children.Add(number);
            var writing=new StackPanel {Spacing=6}; _noteHosts[key]=writing;
            foreach (var note in notes) writing.Children.Add(Preview(note, compact:true));
            content.Children.Add(writing);
            grid.Children.Add(DayCard(key,content,146));
        }
        while (grid.Children.Count % 7 != 0) grid.Children.Add(new Border { Background = Brush.Parse("#ECE4D5"), BorderBrush = Brush.Parse("#D6CAB8"), BorderThickness = new Thickness(.5) });
        page.Children.Add(grid); _calendar.Content = page;
    }
    private void BuildVertical(CalendarItem[] items)
    {
        var page = new StackPanel { Width = Math.Max(440,_scroll.Bounds.Width-16) };
        for (var day = 1; day <= DateTime.DaysInMonth(_month.Year,_month.Month); day++)
        {
            var date = _month.AddDays(day-1); var key = date.ToString("yyyy-MM-dd");
            var content = new Grid { ColumnDefinitions = new ColumnDefinitions("72,*"), Margin = new Thickness(14,14) };
            var label = new StackPanel { Spacing = 4 };
            label.Children.Add(CalendarPaper.Label(day.ToString("00"), 34, date.DayOfWeek == DayOfWeek.Sunday || key == DateTime.Today.ToString("yyyy-MM-dd") ? "#A74D43" : "#776B5D", "Georgia"));
            label.Children.Add(CalendarPaper.Label(date.ToString("ddd").ToUpperInvariant(),11)); content.Children.Add(label);
            var notes = new StackPanel { Spacing=8 }; _noteHosts[key]=notes;
            foreach (var note in items.Where(i => i.Date == key).OrderBy(i => i.Content.Top))
            { var preview = Preview(note,compact:false); notes.Children.Add(preview); }
            Grid.SetColumn(notes,1); content.Children.Add(notes); page.Children.Add(DayCard(key,content,156));
        }
        _calendar.Content = page;
    }
    private Border DayCard(string key, Control child, double height)
    {
        var isToday = key == DateTime.Today.ToString("yyyy-MM-dd");
        var paper=new Grid();paper.Children.Add(new CalendarPaper{IsHitTestVisible=false});paper.Children.Add(child);
        var card = new Border { MinHeight = height, Background = Brush.Parse(key == _day ? "#EFE2D1" : "#F7F1E5"), BorderBrush = Brush.Parse(isToday ? "#A74D43" : "#D6CAB8"), BorderThickness = new Thickness(1), Child = paper, ClipToBounds = true };
        AutomationProperties.SetName(card, "Calendar day " + key); _days.Add(key,card);
        card.PointerPressed += (_,e) => {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed || _drag is not null || e.Source is Control source && (source is Button or TextBox || source.GetVisualAncestors().Any(v => v is Button or TextBox))) return;
            BeginInline(key); e.Handled = true;
        };
        DragDrop.SetAllowDrop(card,true);
        DragDrop.AddDragOverHandler(card, (_, e) => { e.DragEffects = e.DataTransfer.TryGetFiles()?.Any() == true ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; });
        DragDrop.AddDropHandler(card, (_,e) => {
            if(!PrepareToLeave())return; e.Handled=true;
            foreach(var file in e.DataTransfer.TryGetFiles()??Array.Empty<IStorageItem>())
            {var path=file.TryGetLocalPath();if(path is not null)_session.Act(()=>VisionImages.Import(_session.Store,_session.Store.Snapshot.Index.RootMicheId,path,24,24,null,Month,key));}
        });
        return card;
    }
    private Control Preview(CalendarItem entry, bool compact)
    {
        var preview = CalendarPreview.Make(_session,entry,_images,compact); _previews[entry.Id]=preview;
        preview.PointerPressed += (_,e) => {
            if (!e.GetCurrentPoint(preview).Properties.IsLeftButtonPressed || e.Source is Control source && (source is Button || source.GetVisualAncestors().Any(v => v is Button))) return;
            if(!CommitInline()||!Editor.PrepareToLeave())return;
            _drag = new(entry.Id,e.GetPosition(this),e.Pointer,e.ClickCount); e.Pointer.Capture(this); e.Handled=true;
        };
        return preview;
    }
    private void MovePreview(object? sender, PointerEventArgs e)
    {
        if(_drag is not { } g)return;
        var delta=e.GetPosition(this)-g.Start;if(Math.Abs(delta.X)>=4||Math.Abs(delta.Y)>=4)g.Started=true;
        if(g.Started){var target=FindDay(e);_status.Text=target is null?"drop onto a day, or onto month margin":"move to "+DateOnly.Parse(target).ToString("MMMM d");}
        e.Handled=true;
    }
    private void ReleasePreview(object? sender, PointerReleasedEventArgs e)
    {
        if(_drag is not { } g)return;
        MovePreview(sender,e);_drag=null;g.Pointer.Capture(null);
        if(g.Started)DropObjects(new[]{g.Id},e);
        else if(_session.Store.Snapshot.Calendar.Items.Single(i=>i.Id==g.Id) is {Date: { } date} entry)
        { if(entry.Content.Kind=="text")BeginInline(date,g.Clicks>1?entry:null);else BeginInline(date); }
        e.Handled=true;
    }
    private void CancelPreview(){if(_drag is { } g){_drag=null;g.Pointer.Capture(null);}}
    private string? FindDay(PointerEventArgs e)
    {
        if(!new Rect(_scroll.Bounds.Size).Contains(e.GetPosition(_scroll)))return null;
        return _days.FirstOrDefault(pair => {var point=e.GetPosition(pair.Value);return new Rect(pair.Value.Bounds.Size).Contains(point);}).Key;
    }
    private bool DropObjects(Guid[] ids, PointerReleasedEventArgs e)
    {
        var day=FindDay(e);var marginPoint=e.GetPosition(_marginButton);
        if(day is null&&!new Rect(_marginButton.Bounds.Size).Contains(marginPoint))return false;
        if(ids.Length==1&&day is not null)
        {
            var shape=_session.Store.Snapshot.Calendar.Items.SingleOrDefault(i=>i.Id==ids[0]);
            if(shape is not null&&WorkspaceStore.IsVisionShape(shape.Content.Kind))
            {
                var note=_session.Store.Snapshot.Calendar.Items.LastOrDefault(i=>i.Date==day&&i.Id!=shape.Id&&i.Content.Kind=="text"&&i.Content.DeletedAt is null&&_previews.TryGetValue(i.Id,out var preview)&&new Rect(preview.Bounds.Size).Contains(e.GetPosition(preview)));
                if(note is not null){var wrapped=WorkspaceStore.CopyVision(note.Content);wrapped.NoteShape=shape.Content.Kind;NotePresentation.Fit(wrapped);_session.Act(()=>_session.Store.AttachCalendarShape(shape.Id,note.Id,wrapped.Width,wrapped.Height));return true;}
            }
        }
        _session.Act(()=>_session.Store.ScheduleCalendarItems(ids,day,Month),day is null?"Back in the month margin.":"Planned for "+DateOnly.Parse(day).ToString("MMMM d")+".");
        _status.Text="click a day and keep writing · drag a shape onto a note to wrap it";return true;
    }
    public void Dispose(){ClearInline();CancelPreview();Editor.Dispose();_session.Changed-=Refresh;_session.ErrorOccurred-=Error;foreach(var bitmap in _images.Values)bitmap.Dispose();_images.Clear();}
}

internal static class CalendarPreview
{
    public static Control Make(WorkspaceSession session, CalendarItem entry, Dictionary<string,Bitmap> images, bool compact)
    {
        var item=entry.Content;
        if(item.Kind=="image")
        {
            try{
                if(!images.TryGetValue(item.FileName!,out var bitmap)){bitmap=new Bitmap(session.Store.VisionAssetPath(entry.OwnerMicheId,item.FileName!));images.Add(item.FileName!,bitmap);}
                return new Border{Padding=new Thickness(4),Background=Brush.Parse("#EDE3D3"),CornerRadius=new CornerRadius(2),Child=new Image{Source=bitmap,Height=compact?60:120,Stretch=Stretch.Uniform}};
            }catch(Exception ex)when(ex is IOException or ArgumentException or UnauthorizedAccessException){return CalendarPaper.Label("photo unavailable",11);}
        }
        if(WorkspaceStore.IsVisionShape(item.Kind))return new VisionPrimitive(item.Kind,Brush.Parse("#887C6F")){Height=compact?32:64,MaxWidth=230,HorizontalAlignment=HorizontalAlignment.Stretch};
        if(item.Kind=="table")return new TableView(item.Table!,_=>false);
        if(item.Kind!="text")return new Border();
        var panel=new StackPanel{Spacing=3};var label=CalendarPaper.Label(item.Text,compact?12:16,"#453E38","Georgia");

        if(item.LinkedMicheId is { } linked)
        {
            var state=session.Store.Snapshot;var miche=state.Index.Miches.Concat(state.Trash.Select(t=>t.Miche)).Single(m=>m.Id==linked);
            var link=new Button{Content="↗ "+miche.Name,FontSize=11,Padding=new Thickness(0),Foreground=Brush.Parse("#4B4742"),HorizontalAlignment=HorizontalAlignment.Left};
            link.Click+=(_,e)=>{session.OpenLinkedMiche(linked);e.Handled=true;};panel.Children.Add(link);panel.Children.Add(label);
            return NotePresentation.Decorate(new Border{Background=Brush.Parse(WorkspaceStore.MicheNoteColor(miche)),CornerRadius=new CornerRadius(compact?8:12),Padding=new Thickness(compact?6:10),Child=panel},item.NoteShape,Brush.Parse("#887C6F"),compact);
        }
        panel.Children.Add(label);return NotePresentation.Decorate(new Border{Padding=new Thickness(3),Child=panel},item.NoteShape,Brush.Parse("#887C6F"),compact);
    }
}
