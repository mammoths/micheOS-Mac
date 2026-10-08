using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Miche.Mac.Controls;
using Miche.Mac.Models;
using Miche.Mac.Services;

namespace Miche.Mac;

public sealed class ClipboardWindow : Window
{
    private bool _transfer;
    public ScrollViewer Viewport {get;}=new(){VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto};
    public ClipboardWindow(WorkspaceSession session,WindowGeometry? saved)
    {
        Title=session.Store.Snapshot.Clipboard.Title;Width=360;Height=180;MinWidth=240;MinHeight=150;
        Background=Brush.Parse("#241B2D");
        // Standard Mac title bar keeps this small shelf easy to drag and close.
        WindowStartupLocation=WindowStartupLocation.CenterScreen;Content=Viewport;
        if(saved is not null)Opened+=(_,_)=>{
            var screen=Screens.ScreenFromPoint(new PixelPoint(saved.X,saved.Y))??Screens.Primary;var area=screen?.WorkingArea;var scaling=Math.Max(.1,RenderScaling);
            Width=Math.Clamp(saved.Width,MinWidth,Math.Max(MinWidth,(area?.Width??1600)/scaling));Height=Math.Clamp(saved.Height,MinHeight,Math.Max(MinHeight,(area?.Height??1000)/scaling));
            if(area is { } bounds)Position=new PixelPoint(Math.Clamp(saved.X,bounds.X,Math.Max(bounds.X,bounds.Right-(int)(Width*scaling))),Math.Clamp(saved.Y,bounds.Y,Math.Max(bounds.Y,bounds.Bottom-(int)(Height*scaling))));
        };
        Closing+=(_,e)=>{if(_transfer||session.IsQuitting)return;e.Cancel=true;Dispatcher.UIThread.Post(()=>session.DockClipboard());};
        KeyDown+=(_,e)=>{if(e.Key==Key.Q&&e.KeyModifiers.HasFlag(KeyModifiers.Meta)){session.TryQuit();e.Handled=true;}};
        session.Changed+=Refresh;Closed+=(_,_)=>session.Changed-=Refresh;
        void Refresh(){Title=session.Store.Snapshot.Clipboard.Title;session.ClipboardView.Refresh();}
    }
    public WindowGeometry Geometry()=>new(){X=Position.X,Y=Position.Y,Width=Width,Height=Height};
    public void CloseForTransfer(){_transfer=true;Close();}
}
