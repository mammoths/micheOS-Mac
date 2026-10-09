using Avalonia.Controls;
namespace Miche.Mac.Services;

public sealed partial class WorkspaceSession
{
    private CalendarWindow? _calendarWindow;
    public CalendarWindow? CalendarWindow=>_calendarWindow;
    public bool OpenCalendar(string? date=null)
    {
        if(_calendarWindow is null)
        {
            _calendarWindow=new CalendarWindow(this);_calendarWindow.Closed+=(_,_)=>_calendarWindow=null;
        }
        if(date is not null&&!_calendarWindow.Calendar.ShowDate(date))return false;
        _calendarWindow.Show();if(_calendarWindow.WindowState==WindowState.Minimized)_calendarWindow.WindowState=WindowState.Normal;
        _calendarWindow.Activate();_calendarWindow.Calendar.Editor.BoardCanvas.Focus();return true;
    }
    public void OpenCalendarWidget()=>Act(()=>Store.ShowCalendarWidget(Store.Snapshot.Index.ActiveMicheId));
    public bool OpenLinkedMiche(Guid id)
    {
        if(!Store.Snapshot.Index.Miches.Any(m=>m.Id==id)){Act(()=>throw new ArgumentException("Restore this Miche from recently deleted to open it."));return false;}
        if(!Act(()=>{SaveAllEditors();Store.Activate(id);}))return false;
        var home=OpenHome();home.HideVisionForNavigation();return true;
    }
}
