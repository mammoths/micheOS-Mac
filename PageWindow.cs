using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Miche.Mac.Controls;
using Miche.Mac.Models;
using Miche.Mac.Services;
namespace Miche.Mac;
public sealed class PageWindow:Window
{
    private bool _transfer,_closing;
    public PageWindow(WorkspaceSession session,Guid id,PageView view)
    {
        var p=session.Pages.Get(id);Title="Miche · "+(string.IsNullOrWhiteSpace(p.Title)?"untitled page":p.Title);Width=p.Window?.Width??640;Height=p.Window?.Height??580;MinWidth=300;MinHeight=260;WindowStartupLocation=WindowStartupLocation.CenterScreen;Content=view;
        if(p.Window is not null){var candidate=new PixelPoint(p.Window.X,p.Window.Y);if(Screens.All.Any(s=>s.WorkingArea.Contains(candidate))) {WindowStartupLocation=WindowStartupLocation.Manual;Position=candidate;}}
        Closing+=(_,e)=>{if(_transfer||session.IsQuitting)return;e.Cancel=true;if(_closing)return;_closing=true;session.DockPage(id,()=>_closing=false);};
        KeyDown+=(_,e)=>{if(e.Key==Key.Q&&e.KeyModifiers==KeyModifiers.Meta){session.TryQuit();e.Handled=true;}};
    }
    public WindowGeometry Geometry()=>new(){X=Position.X,Y=Position.Y,Width=Bounds.Width,Height=Bounds.Height};
    public void CloseForTransfer(){_transfer=true;Content=null;Close();}
}
