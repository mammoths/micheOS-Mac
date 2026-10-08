using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Miche.Mac.Models;
using Miche.Mac.Services;

namespace Miche.Mac.Controls;

public sealed partial class FlowView : UserControl, IDisposable
{
    private readonly FlowRepository _repo;
    private readonly Grid _root = new() { RowDefinitions=new("Auto,Auto,*") };
    private readonly StackPanel _rows = new() { Spacing=12, Margin=new Thickness(28,16,28,28) };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    private readonly TextBox _capture = new() { Watermark="+ a little thing to do", MaxLength=1000, Background=Brushes.Transparent, BorderThickness=new Thickness(0), FontFamily=new("Georgia"),FontSize=22 };
    private readonly TextBlock _dateLabel = new() { FontFamily=new("Georgia"),FontSize=24 };
    private readonly TextBlock _message = new() { TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=Brush.Parse("#DFA6AD"),Margin=new Thickness(12,4) };
    private readonly StackPanel _bandButtons = new() {Orientation=Orientation.Horizontal};
    private readonly Button _undo;
    private readonly Border _dialog = new() { IsVisible=false,Background=Brush.Parse("#2C2033"),BorderBrush=Brush.Parse("#735B70"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(22),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,MaxWidth=420 };
    private readonly Dictionary<Guid,Border> _rowControls = new();
    private readonly Dictionary<Guid,TextBlock> _countdowns = new();
    private readonly Dictionary<Guid,FlowWater> _water = new();
    private readonly DispatcherTimer _draftTimer = new() {Interval=TimeSpan.FromMilliseconds(500)};
    private readonly HashSet<Key> _heldActions = new();
    private Guid? _selected;
    private DateOnly? _date;
    private bool _couldDo, _restoring;
    private string _band="morning";
    private Func<bool>? _saveDialog;
    public FlowRepository Repository => _repo;
    public TextBox CaptureBox => _capture;
    public Guid? SelectedId => _selected;
    public DateOnly? PlanningDate => _date;
    public IReadOnlyDictionary<Guid,Border> RowControls => _rowControls;
    public StackPanel RowPanel => _rows;
    public event Action? HomeRequested;
    public FlowView(FlowRepository repository)
    {
        _repo=repository; Focusable=true;Background=Brush.Parse("#241B2D");Content=_root;
        var nav=new WrapPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(12,4)};
        Add(nav,"today",()=>SelectDay(null));Add(nav,"tomorrow",()=>SelectDay(Today().AddDays(1)));Add(nav,"could do",()=>SelectCouldDo());Add(nav,"←",()=>SelectDay((_date??Today()).AddDays(-1)));Add(nav,"→",()=>SelectDay((_date??Today()).AddDays(1)));Add(nav,"rhythm",ShowRhythm);
        _undo=Add(nav,"undo · ⌘Z",()=>Run(()=>_repo.Undo()));Add(nav,"home",()=>{if(PrepareToLeave())HomeRequested?.Invoke();});
        nav.Children.Add(_message);_root.Children.Add(nav);
        var heading=new StackPanel {Spacing=8,Margin=new Thickness(28,12,28,0)};heading.Children.Add(_dateLabel);heading.Children.Add(_bandButtons);
        var captureRow=new Grid {ColumnDefinitions=new("*,Auto")};captureRow.Children.Add(_capture);var add=Add(captureRow,"+",Submit);Grid.SetColumn(add,1);heading.Children.Add(captureRow);Grid.SetRow(heading,1);_root.Children.Add(heading);
        _scroll.Content=_rows;Grid.SetRow(_scroll,2);_root.Children.Add(_scroll);_root.Children.Add(_dialog);Grid.SetRowSpan(_dialog,3);_dialog.ZIndex=10;
        ScrollViewer.SetBringIntoViewOnFocusChange(_rows,false);ScrollViewer.SetBringIntoViewOnFocusChange(this,false);
        _draftTimer.Tick+=(_,_)=>{_draftTimer.Stop();SaveDraft();};_capture.TextChanged+=(_,_)=>{if(_restoring)return;_draftTimer.Stop();_draftTimer.Start();};
        _capture.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){e.Handled=true;Submit();}};
        AddHandler(KeyDownEvent,SelectionKeyDown,RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent,(_,e)=>_heldActions.Remove(e.Key),RoutingStrategies.Tunnel);
        LostFocus+=(_,_)=>_heldActions.Clear();
        _rows.PointerPressed+=(_,e)=>{if(ReferenceEquals(e.Source,_rows)&&!_dialog.IsVisible){_selected=null;Refresh();Focus();}};
        _repo.Changed+=DataChanged;LoadDraft();Refresh();
    }
    private DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,TimeZoneInfo.FindSystemTimeZoneById(_repo.Snapshot().Preferences.TimeZoneId)).DateTime);
    private static Button Add(Panel parent,string title,Action action)
    {var button=new Button {Content=title,FontSize=11,Margin=new Thickness(2),Padding=new Thickness(8,5)};AutomationProperties.SetName(button,title);button.Click+=(_,_)=>action();parent.Children.Add(button);return button;}
    private static TextBlock Label(string text,double size=12) => new() {Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Foreground=Brush.Parse("#F4E5D1"),FontFamily=new(size>=20?"Georgia":"Menlo")};
    private bool Run(Action action)
    {
        try {action();_message.Text=_repo.LastNotificationWarning??"saved on this Mac";return true;}
        catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
        {_message.Text=e.Message;return false;}
    }
    public void ShowError(string message) => _message.Text=message;
    public bool SaveDraft()
    {
        _draftTimer.Stop();
        var text=_capture.Text??"";var document=_repo.Snapshot();
        var saved=_couldDo?document.CouldDoDraft:_date is {} day?document.StackDayDrafts.GetValueOrDefault(day.ToString("yyyy-MM-dd"),""):document.StackDraft;
        // Scope changes/close must see the live editor even before a queued TextChanged event.
        if(text==saved){return true;}
        if(!Run(()=>{if(_couldDo)_repo.SaveCouldDoDraft(text);else _repo.SaveStackDraft(text,_date);}))return false;
        return true;
    }
    public bool PrepareToLeave() {CancelGesture();return SaveDraft()&&(_saveDialog?.Invoke()??true);}
    private void LoadDraft()
    {
        _draftTimer.Stop();var d=_repo.Snapshot();_restoring=true;
        _capture.Text=_couldDo?d.CouldDoDraft:_date is {} day?d.StackDayDrafts.GetValueOrDefault(day.ToString("yyyy-MM-dd"),""):d.StackDraft;
        _capture.CaretIndex=_capture.Text.Length;_restoring=false;
    }
    public bool SelectDay(DateOnly? date)
    {
        if(!PrepareToLeave())return false;_date=date;_couldDo=false;_selected=null;_band="morning";CloseDialog();LoadDraft();Refresh();return true;
    }
    public bool SelectCouldDo()
    {
        if(!PrepareToLeave())return false;_date=null;_couldDo=true;_selected=null;CloseDialog();LoadDraft();Refresh();return true;
    }
    public void Submit()
    {
        if(string.IsNullOrWhiteSpace(_capture.Text))return;
        var text=_capture.Text;Guid id=default;
        if(!Run(()=>id=_couldDo?_repo.AddCouldDo(text):_repo.AddToStack(text,_date,_date.HasValue?BandTime(_repo.Snapshot(),_date.Value):null)))return;
        _restoring=true;_capture.Text="";_restoring=false;_draftTimer.Stop();_selected=id;Refresh();Focus();
    }
    private TimeOnly BandTime(TodayDocument d,DateOnly date) {var p=DailyRhythm.ForDate(d,date);return _band=="morning"?p.MorningStart:_band=="afternoon"?new TimeOnly(12,0):p.EveningStart;}
    private void DataChanged() {if(_gesture is not null||_preparingGesture)return;Refresh();}
    public void Refresh()
    {
        if(_gesture is not null)return;
        var doc=_repo.Snapshot();if(_selected is {} selected&&!doc.StackItems.Any(i=>i.Id==selected))_selected=null;
        _undo.IsEnabled=_repo.CanUndo;_dateLabel.Text=_couldDo?"Could do":(_date??Today()).ToString("dddd, MMMM d");
        _bandButtons.Children.Clear();_bandButtons.IsVisible=_date.HasValue&&!_couldDo;
        if(_bandButtons.IsVisible)foreach(var band in new[]{"morning","afternoon","evening"}) {var value=band;var b=Add(_bandButtons,band,()=>{_band=value;Refresh();});b.Opacity=_band==band?1:.5;}
        _rows.Children.Clear();_rowControls.Clear();_countdowns.Clear();_water.Clear();
        if(_couldDo)
        {
            foreach(var task in doc.StackItems.Where(i=>i.CouldDo))AddTask(task,null);
            if(_rows.Children.Count==0)_rows.Children.Add(Label("a pocket for possibilities",18));
        }
        else
        {
            var plan=_date is {} date?OnDeckPlanner.ForDate(doc,date):OnDeckPlanner.Plan(doc,DateTimeOffset.UtcNow);
            foreach(var slot in plan.Tonight)AddTask(slot.Item,slot);
            if(plan.Tonight.Count==0)_rows.Children.Add(Label("a little space for what comes next",18));
            var prefs=DailyRhythm.ForDate(doc,_date??Today());_rows.Children.Add(Label("wake · "+prefs.MorningStart.ToString("h:mm tt")+"   /   bedtime · "+prefs.WindDownTime.ToString("h:mm tt")));
            var review=doc.StackItems.Where(i=>i.ReviewAt is not null&&i.FinishedAt is null).ToArray();
            if(review.Length>0){_rows.Children.Add(Label("review · time passed",18));foreach(var item in review)AddTask(item,null);}
            var done=doc.StackItems.Where(i=>i.FinishedAt is not null).OrderByDescending(i=>i.FinishedAt).ToArray();
            if(done.Length>0){_rows.Children.Add(Label("finished",18));foreach(var item in done)AddTask(item,null);}
            if(!string.IsNullOrWhiteSpace(doc.Treat))_rows.Children.Add(Label(doc.Treat,18));
        }
        UpdateClock();
    }
    private void AddTask(OnDeckItem item,OnDeckSlot? slot)
    {
        var grid=new Grid {RowDefinitions=new("*,Auto"),Margin=new Thickness(14,10)};
        var body=new StackPanel {Spacing=4};var title=Label(item.Title,21);if(item.FinishedAt is not null)title.TextDecorations=TextDecorations.Strikethrough;body.Children.Add(title);
        var countdown=Label("");body.Children.Add(countdown);_countdowns[item.Id]=countdown;grid.Children.Add(body);
        if(_selected==item.Id)
        {
            var actions=new WrapPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,10)};Grid.SetRow(actions,1);grid.Children.Add(actions);
            if(item.CouldDo)Add(actions,"into today",()=>Run(()=>_repo.ScheduleCouldDo(item.Id,DateTimeOffset.UtcNow)));
            else if(item.FinishedAt is not null)Add(actions,"return to flow",()=>Run(()=>_repo.Change(d=>{var t=d.StackItems.Single(i=>i.Id==item.Id);t.FinishedAt=null;t.ElapsedSeconds=0;t.ReviewAt=null;})));
            else
            {
                Add(actions,"done · enter",()=>CompleteSelected());
                if(!_date.HasValue)Add(actions,item.StartedAt is {} start&&start<=DateTimeOffset.UtcNow?"pause":"start timer · space",()=>{if(item.StartedAt is {} s&&s<=DateTimeOffset.UtcNow)Run(()=>_repo.PauseStackItem(item.Id,DateTimeOffset.UtcNow));else StartSelected();});
                if(item.ReviewAt is not null)Add(actions,"back into flow · tab",()=>Run(()=>_repo.RequeueReviewedStackItem(item.Id,DateTimeOffset.UtcNow)));
                Add(actions,"timing",()=>ShowTiming(item.Id));Add(actions,"could do",()=>Run(()=>_repo.ReturnToCouldDo(item.Id,DateTimeOffset.UtcNow)));
            }
            Add(actions,"rename",()=>ShowRename(item.Id));Add(actions,"delete",()=>DeleteSelected());
        }
        var layer=new Grid();var water=new FlowWater();layer.Children.Add(water);layer.Children.Add(grid);_water[item.Id]=water;
        var border=new Border {Child=layer,CornerRadius=new CornerRadius(16),BorderBrush=Brush.Parse(_selected==item.Id?"#DFA6AD":"#5D435C"),BorderThickness=new Thickness(_selected==item.Id?2:1),Background=Brush.Parse("#312439"),MinHeight=item.FinishedAt is null&&item.ReviewAt is null?DurationHeight(item.Minutes):54,Tag=item.Id};
        AutomationProperties.SetName(border,item.Title);_rowControls[item.Id]=border;_rows.Children.Add(border);
        border.PointerPressed+=(_,e)=>TaskPressed(item.Id,border,e);
        border.PointerMoved+=(_,e)=>MoveGesture(e);border.PointerReleased+=(_,e)=>ReleaseGesture(e);border.PointerCaptureLost+=(_,_)=>CancelGesture();
        if(_selected==item.Id&&item.FinishedAt is null&&item.ReviewAt is null)
        {
            var grip=new Border {Height=16,Background=Brushes.Transparent,Focusable=true,VerticalAlignment=VerticalAlignment.Bottom,Cursor=new(StandardCursorType.SizeNorthSouth),Child=new Border {Width=32,Height=2,CornerRadius=new CornerRadius(2),Background=Brush.Parse("#BEA8B9"),VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(4)}};
            AutomationProperties.SetName(grip,"Duration for "+item.Title);layer.Children.Add(grip);
            grip.PointerPressed+=(_,e)=>{if(e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed){BeginGesture(item.Id,border,e,true);e.Handled=true;}};
            grip.KeyDown+=(_,e)=>{if(e.Key is Key.Up or Key.Down){e.Handled=true;Run(()=>_repo.SetStackDuration(item.Id,Math.Clamp(item.Minutes+(e.Key==Key.Down?5:-5),5,1440)));}else if(e.Key==Key.Enter)e.Handled=true;};
        }
    }
    private static double DurationHeight(int minutes) => 44+Math.Max(0,minutes-10)*2;
    public void SelectTask(Guid id) {if(_dialog.IsVisible)return;_selected=id;Refresh();Focus();}
    public bool StartSelected()
    {
        if(_date.HasValue||_couldDo||_selected is not {} id)return false;
        var item=_repo.Snapshot().StackItems.SingleOrDefault(i=>i.Id==id);if(item is null||item.FinishedAt is not null)return false;
        return Run(()=>_repo.FocusStackItem(id,DateTimeOffset.UtcNow));
    }
    public bool CompleteSelected()
    {
        if(_couldDo||_selected is not {} id)return false;
        return Run(()=>_repo.CompleteSelectedStackItem(id,DateTimeOffset.UtcNow));
    }
    public bool DeleteSelected()
    {
        if(_selected is not {} id)return false;
        if(!Run(()=>_repo.Change(d=>d.StackItems.RemoveAll(i=>i.Id==id))))return false;_selected=null;Refresh();Focus();return true;
    }
    public void UpdateClock()
    {
        var now=DateTimeOffset.UtcNow;var doc=_repo.Snapshot();var plan=_date is {} date?OnDeckPlanner.ForDate(doc,date):OnDeckPlanner.Plan(doc,now);
        foreach(var item in doc.StackItems)
        {
            if(!_countdowns.TryGetValue(item.Id,out var text))continue;
            var slot=plan.Tonight.FirstOrDefault(s=>s.Item.Id==item.Id);
            var seconds=Math.Max(0,item.Minutes*60-item.Elapsed(now));
            text.Text=item.StartedAt is {} start?(start>now?"waiting · "+TimeZoneInfo.ConvertTime(start,TimeZoneInfo.FindSystemTimeZoneById(doc.Preferences.TimeZoneId)).ToString("h:mm tt"):TimeSpan.FromSeconds(seconds).ToString(item.Minutes>=60?@"hh\:mm\:ss":@"mm\:ss")+" left / "+item.Minutes+" min"):
                item.ReviewAt is not null?"review · "+item.Minutes+" min":item.FinishedAt is not null?"finished": "~"+item.Minutes+"m"+(slot is null?"":" · "+TimeZoneInfo.ConvertTime(slot.Start,TimeZoneInfo.FindSystemTimeZoneById(doc.Preferences.TimeZoneId)).ToString("h:mm tt"));
            if(_water.TryGetValue(item.Id,out var water)){water.Progress=item.FinishedAt is not null?1:Math.Clamp(item.Elapsed(now)/(item.Minutes*60),0,1);water.InvalidateVisual();}
        }
    }
    private static bool Editing(object? source) => source is Control c&&(c is TextBox or Button or ComboBox||c.GetVisualAncestors().Any(p=>p is TextBox or Button or ComboBox));
    private void SelectionKeyDown(object? sender,KeyEventArgs e)
    {
        if(e.Handled)return;
        if(e.Key==Key.Escape){if(_dialog.IsVisible)CloseDialog();else if(_gesture is not null)CancelGesture();else {_selected=null;Refresh();}e.Handled=true;return;}
        if(Editing(e.Source))return;
        if(_gesture is not null){if(e.Key is Key.Space or Key.Enter or Key.Back or Key.Tab)e.Handled=true;return;}
        if(e.Key==Key.Z&&e.KeyModifiers==KeyModifiers.Meta){e.Handled=true;Run(()=>_repo.Undo());return;}
        if(_dialog.IsVisible)return;
        if(e.KeyModifiers==KeyModifiers.Alt&&e.Key is Key.Up or Key.Down){e.Handled=true;MoveSelected(e.Key==Key.Down?1:-1);return;}
        if(e.KeyModifiers!=KeyModifiers.None)return;
        if(e.Key is Key.Space or Key.Enter or Key.Back or Key.Tab)
        {if(!_heldActions.Add(e.Key)){e.Handled=true;return;}}
        if(e.Key==Key.Space){e.Handled=true;if(_selected.HasValue)StartSelected();else _capture.Focus();}
        else if(e.Key==Key.Enter&&_selected.HasValue){e.Handled=true;CompleteSelected();}
        else if(e.Key==Key.Back&&_selected is {} id)
        {e.Handled=true;var item=_repo.Snapshot().StackItems.Single(i=>i.Id==id);if(item.FinishedAt is null&&(item.StartedAt is not null||item.ElapsedSeconds>0||item.ReviewAt is not null))Run(()=>_repo.ResetStackTimer(id,DateTimeOffset.UtcNow));else DeleteSelected();}
        else if(e.Key==Key.Tab&&_selected is {} reviewed&&_repo.Snapshot().StackItems.Single(i=>i.Id==reviewed).ReviewAt is not null)
        {e.Handled=true;Run(()=>_repo.RequeueReviewedStackItem(reviewed,DateTimeOffset.UtcNow));Focus();}
    }
    private void CloseDialog() {_dialog.IsVisible=false;_saveDialog=null;Focus();}
    private StackPanel Dialog(string title)
    {CancelGesture();var panel=new StackPanel {Spacing=12};panel.Children.Add(Label(title,25));_dialog.Child=panel;_dialog.IsVisible=true;return panel;}
    private static TextBox Field(Panel parent,string name,string value)
    {parent.Children.Add(Label(name));var field=new TextBox {Text=value,MinWidth=240,MaxLength=1000};AutomationProperties.SetName(field,name);parent.Children.Add(field);return field;}
    private void DialogActions(Panel parent,Func<bool> save)
    {_saveDialog=()=>{if(!save())return false;CloseDialog();return true;};var actions=new WrapPanel {Orientation=Orientation.Horizontal};Add(actions,"save",()=>_saveDialog?.Invoke());Add(actions,"cancel",CloseDialog);parent.Children.Add(actions);}
    private void ShowRename(Guid id)
    {var item=_repo.Snapshot().StackItems.Single(i=>i.Id==id);var panel=Dialog("a name for this task");var title=Field(panel,"Task name",item.Title);DialogActions(panel,()=>Run(()=>_repo.Change(d=>d.StackItems.Single(i=>i.Id==id).Title=title.Text?.Trim()??"")));title.Focus();title.SelectAll();}
    private void ShowRhythm()
    {
        if(!SaveDraft())return;var date=_date??Today();var doc=_repo.Snapshot();var prefs=DailyRhythm.ForDate(doc,date);var panel=Dialog("Your daily rhythm · "+date.ToString("MMM d"));
        var wake=Field(panel,"Wake time",prefs.MorningStart.ToString("h:mm tt"));var bed=Field(panel,"Bedtime",prefs.WindDownTime.ToString("h:mm tt"));var treat=Field(panel,"Optional treat",doc.Treat);
        DialogActions(panel,()=>Run(()=>{if(!TimeOnly.TryParse(wake.Text,out var w)||!TimeOnly.TryParse(bed.Text,out var b))throw new ArgumentException("Use a time like 7:30 AM or 11 PM.");_repo.Change(d=>{
            var key=date.ToString("yyyy-MM-dd");
            bool wakeChanged=w!=prefs.MorningStart,bedChanged=b!=prefs.WindDownTime;
            if(wakeChanged||bedChanged)
            {
                if(!d.DayRhythms.TryGetValue(key,out var rhythm))d.DayRhythms[key]=rhythm=new();
                if(wakeChanged)rhythm.Wake=w;if(bedChanged)rhythm.Bed=b;
            }
            d.Treat=treat.Text??"";
        });}));wake.Focus();
    }
    private void ShowTiming(Guid id)
    {
        var item=_repo.Snapshot().StackItems.Single(i=>i.Id==id);var panel=Dialog("Timing · "+item.Title);
        var choice=new ComboBox {ItemsSource=new[]{"flexible","at a time","minutes before bedtime","last thing before bed"},SelectedIndex=item.FixedTime.HasValue?1:item.BeforeBedtimeMinutes.HasValue?2:item.FinishAtBedtime?3:0};panel.Children.Add(choice);
        var value=Field(panel,"Clock time or minutes before bed",item.FixedTime?.ToString("h:mm tt")??item.BeforeBedtimeMinutes?.ToString()??"");
        DialogActions(panel,()=>Run(()=>{TimeOnly? clock=null;int? before=null;if(choice.SelectedIndex==1){if(!TimeOnly.TryParse(value.Text,out var parsed))throw new ArgumentException("Use a time like 9:30 PM.");clock=parsed;}if(choice.SelectedIndex==2){if(!int.TryParse(value.Text,out var parsed)||parsed is <1 or >1440)throw new ArgumentException("Use 1–1440 minutes before bedtime.");before=parsed;}_repo.Change(d=>{var task=d.StackItems.Single(i=>i.Id==id);task.FixedTime=clock;task.BeforeBedtimeMinutes=before;task.FinishAtBedtime=choice.SelectedIndex==3;if(task.StartedAt>DateTimeOffset.UtcNow)task.StartedAt=null;});}));value.Focus();
    }
    public void Dispose(){_draftTimer.Stop();CancelGesture();_repo.Changed-=DataChanged;}
}

// A quiet progress fill: painting never mutates the journal or receives input.
internal sealed class FlowWater : Control
{
    public double Progress {get;set;}
    public FlowWater(){IsHitTestVisible=false;}
    public override void Render(DrawingContext context)
    {
        if(Progress<=0)return;
        using(context.PushClip(new RoundedRect(new Rect(Bounds.Size),new CornerRadius(16))))
        {var height=Bounds.Height*.88*Progress;context.FillRectangle(Brush.Parse("#354C535F"),new Rect(0,Bounds.Height-height,Bounds.Width,height));}
    }
}
