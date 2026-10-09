using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Miche.Mac.Controls;
using Miche.Mac.Models;
using Miche.Mac.Services;

namespace Miche.Mac;

public sealed class CalendarWindow : Window
{
    public CalendarView Calendar { get; }
    private readonly WorkspaceSession _session;
    public CalendarWindow(WorkspaceSession session)
    {
        _session=session;var geometry=session.Store.Snapshot.Calendar.Window;
        Title="Miche · calendar";Width=geometry?.Width??1180;Height=geometry?.Height??820;MinWidth=760;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Calendar=new CalendarView(session);Content=Calendar;Calendar.CloseRequested+=Close;
        Closing+=(_,e)=>{if(!session.IsQuitting&&!SaveBeforeClose())e.Cancel=true;};
        Closed+=(_,_)=>Calendar.Dispose();
        KeyDown+=(_,e)=>{
            if(e.Handled)return;
            if(e.Key==Key.Q&&e.KeyModifiers==KeyModifiers.Meta){session.TryQuit();e.Handled=true;}
            else if((e.Key==Key.C&&e.KeyModifiers==KeyModifiers.None||e.Key==Key.Escape)&&e.Source is Control control&&control is not TextBox&&!control.GetVisualAncestors().Any(v=>v is TextBox))
            {if(e.Key!=Key.Escape||!Calendar.Editor.Escape())Close();e.Handled=true;}
        };
    }
    public bool SaveBeforeClose()=>Calendar.PrepareToLeave()&&_session.Act(()=>_session.Store.SaveCalendarView(Calendar.ViewMode,new WindowGeometry{X=Position.X,Y=Position.Y,Width=Bounds.Width,Height=Bounds.Height}));
}
