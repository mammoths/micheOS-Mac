using Avalonia.Controls;
using Avalonia.Threading;
namespace Miche.Mac.Services;
public sealed partial class WorkspaceSession
{
    private MeiliWindow? _meiliWindow;
    private bool _meiliQuitPending;
    private bool _meiliQuitApproved;
    public MeiliWindow? MeiliWindow => _meiliWindow;
    public void OpenMeili()
    {
        if(_meiliWindow is null)
        {
            _meiliWindow=new MeiliWindow(Store.DirectoryPath);
            _meiliWindow.Closed+=(_,_)=>{_meiliWindow=null;_meiliQuitApproved=false;if(!IsQuitting&&_home?.IsVisible!=true&&_pageWindows.Count==0&&_floating.Count==0&&_flowWindow?.IsVisible!=true)Dispatcher.UIThread.Post(()=>{if(!TryQuit())OpenHome();});};
        }
        if(_meiliWindow.WindowState==WindowState.Minimized)_meiliWindow.WindowState=WindowState.Normal;
        _meiliWindow.Show();_meiliWindow.Activate();
    }
}
