using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Miche.Mac.Services;

namespace Miche.Mac.Controls;

public sealed class CalendarWidgetView : UserControl, IDisposable
{
    private readonly WorkspaceSession _session;
    private readonly StackPanel _root=new(){Spacing=14,Margin=new Thickness(22,16,22,16)};
    private readonly Dictionary<string,Bitmap> _images=new();
    private readonly DispatcherTimer _clock=new(){Interval=TimeSpan.FromMinutes(1)};
    private string _date="";
    public CalendarWidgetView(WorkspaceSession session)
    {
        _session=session;CalendarPaper.Style(this);
        Content=new Border{Background=Brush.Parse("#F7F1E5"),CornerRadius=new CornerRadius(3),BorderBrush=Brush.Parse("#D6CAB8"),BorderThickness=new Thickness(1),Child=_root};
        _session.Changed+=Refresh;_clock.Tick+=(_,_)=>{if(_date!=DateTime.Today.ToString("yyyy-MM-dd"))Refresh();};_clock.Start();Refresh();
    }
    public void Refresh()
    {
        var today=DateTime.Today;_date=today.ToString("yyyy-MM-dd");_root.Children.Clear();
        var top=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto,Auto")};top.Children.Add(CalendarPaper.Label("今日 / CALENDAR",11,"#A74D43"));
        var open=CalendarPaper.Button("↗",()=>_session.OpenCalendar(_date));Grid.SetColumn(open,1);top.Children.Add(open);
        var hide=CalendarPaper.Button("×",()=>_session.Act(()=>_session.Store.ShowCalendarWidget(_session.Store.Snapshot.Index.ActiveMicheId,false)));Grid.SetColumn(hide,2);top.Children.Add(hide);_root.Children.Add(top);
        var stamp=new Grid{ColumnDefinitions=new ColumnDefinitions("Auto,*")};
        stamp.Children.Add(CalendarPaper.Label(today.ToString("dd"),66,"#A74D43","Georgia"));
        var calendar=new StackPanel{Spacing=7,Margin=new Thickness(18,12,0,0)};calendar.Children.Add(CalendarPaper.Label(today.ToString("dddd").ToLowerInvariant(),20,"#A74D43","Georgia"));calendar.Children.Add(CalendarPaper.Label(today.ToString("MMMM / yyyy").ToUpperInvariant(),11));Grid.SetColumn(calendar,1);stamp.Children.Add(calendar);_root.Children.Add(stamp);
        _root.Children.Add(new Border{Height=1,Background=Brush.Parse("#D6CAB8")});
        var notes=new StackPanel{Spacing=10};
        foreach(var item in _session.Store.Snapshot.Calendar.Items.Where(i=>i.Date==_date&&i.Content.DeletedAt is null).OrderBy(i=>i.Content.Top))notes.Children.Add(CalendarPreview.Make(_session,item,_images,false));
        if(notes.Children.Count==0)notes.Children.Add(CalendarPaper.Label("a little room for what you had in mind",14,"#887C6F","Georgia"));
        _root.Children.Add(new ScrollViewer{Content=notes,MaxHeight=260});
        var footer=new WrapPanel();footer.Children.Add(CalendarPaper.Button("open today’s page",()=>_session.OpenCalendar(_date)));footer.Children.Add(CalendarPaper.Button("yesterday ↖",()=>_session.OpenCalendar(today.AddDays(-1).ToString("yyyy-MM-dd"))));_root.Children.Add(footer);
    }
    public void Dispose(){_clock.Stop();_session.Changed-=Refresh;foreach(var image in _images.Values)image.Dispose();_images.Clear();}
}
