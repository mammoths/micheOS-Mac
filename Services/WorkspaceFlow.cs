using Avalonia.Controls;
using Avalonia.Threading;

namespace Miche.Mac.Services;

public sealed partial class WorkspaceSession
{
    private FlowRepository? _flow;
    private FlowWindow? _flowWindow;
    private readonly DispatcherTimer _flowClock=new() {Interval=TimeSpan.FromSeconds(1)};
    public FlowRepository? Flow => _flow;
    public FlowWindow? FlowWindow => _flowWindow;
    private void InitializeExistingFlow()
    {
        _flowClock.Tick+=(_,_)=>TickFlow();
        if(!File.Exists(Path.Combine(Store.DirectoryPath,"flow.json")))return;
        // Invalid Flow does not replace data or prevent opening the rest of Miche.
        try {EnsureFlow();}catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException) {Notify(StatusChanged,e.Message);}
    }
    private FlowRepository EnsureFlow()
    {
        if(_flow is not null)return _flow;
        _flow=new FlowRepository(Path.Combine(Store.DirectoryPath,"flow.json"),Store.Snapshot.Index.RootMicheId);
        _flowClock.Start();TickFlow();return _flow;
    }
    private void TickFlow()
    {
        if(_flow is null||IsQuitting)return;
        try {_flow.AdvanceStackFlow(DateTimeOffset.UtcNow);_flowWindow?.View.UpdateClock();}
        catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {_flowWindow?.View.ShowError("Flow could not save its next block. "+e.Message);Notify(StatusChanged,e.Message);}
    }
    public bool OpenFlow()
    {
        try
        {
            var repository=EnsureFlow();
            if(_flowWindow is null)
            {
                _flowWindow=new FlowWindow(this,repository,Store.Snapshot.Index.Miches.Single(m=>m.Id==repository.OriginMicheId).Name);
                _flowWindow.Closed+=(_,_)=>{
                    _flowWindow=null;
                    if(!IsQuitting&&(_home is null||!_home.IsVisible)&&_floating.Count==0&&_clipboardWindow?.IsVisible!=true&&_meiliWindow?.IsVisible!=true)Dispatcher.UIThread.Post(()=>{if(!TryQuit())OpenHome();});
                };
            }
            if(_flowWindow.WindowState==WindowState.Minimized)_flowWindow.WindowState=WindowState.Normal;
            _flowWindow.Show();_flowWindow.Activate();return true;
        }
        catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        {Notify(StatusChanged,e.Message);Notify(ErrorOccurred,e.Message);return false;}
    }
}
