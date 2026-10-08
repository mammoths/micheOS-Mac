using Avalonia.Controls;
using Miche.Mac.Controls;

namespace Miche.Mac.Services;

public sealed partial class WorkspaceSession
{
    private ClipboardView? _clipboard;
    private ClipboardWindow? _clipboardWindow;
    public ClipboardView ClipboardView=>_clipboard??=new ClipboardView(this);
    public ClipboardWindow? ClipboardWindow=>_clipboardWindow;
    private static void DetachClipboard(ClipboardView view)
    {if(view.Parent is ContentControl parent&&ReferenceEquals(parent.Content,view))parent.Content=null;}
    public bool PopoutClipboard()
    {
        if(_clipboardWindow is not null){_clipboardWindow.Show();_clipboardWindow.Activate();return true;}
        if(!Store.Snapshot.Clipboard.Floating&&!Act(()=>Store.SetClipboardFloating(true)))return false;
        DetachClipboard(ClipboardView);_clipboardWindow=new ClipboardWindow(this,Store.Snapshot.Clipboard.Window);
        _clipboardWindow.Viewport.Content=ClipboardView;_clipboardWindow.Show();_clipboardWindow.Activate();ClipboardView.Refresh();_home?.RefreshClipboard();return true;
    }
    public bool DockClipboard()
    {
        var window=_clipboardWindow;DetachClipboard(ClipboardView);_clipboardWindow=null;
        if(!Act(()=>Store.SetClipboardFloating(false,window?.Geometry())))
        {_clipboardWindow=window;if(window is not null)window.Viewport.Content=ClipboardView;else _home?.RefreshClipboard();return false;}
        window?.CloseForTransfer();OpenHome();_home?.RefreshClipboard();ClipboardView.Refresh();return true;
    }
    public bool OpenClipboard()
    {
        if(Store.Snapshot.Clipboard.Floating)return PopoutClipboard();
        return Act(()=>Store.ShowClipboard(Store.Snapshot.Index.ActiveMicheId));
    }
}
