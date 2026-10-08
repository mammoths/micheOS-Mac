using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Miche.Mac.Controls;
using Miche.Mac.Models;

namespace Miche.Mac.Services;

// One writer and one set of live widget editors for the entire app, independent of home.
public sealed partial class WorkspaceSession : IDisposable
{
    private readonly Dictionary<Guid, DumpView> _views = new();
    private readonly Dictionary<Guid, WidgetWindow> _floating = new();
    private MainWindow? _home;
    private bool _disposed;
    public WorkspaceStore Store { get; }
    public bool IsQuitting { get; private set; }
    public IReadOnlyDictionary<Guid, WidgetWindow> FloatingWindows => _floating;
    public event Action? Changed;
    public event Action<string>? StatusChanged;
    public event Action<string>? ErrorOccurred;
    public event Action? QuitCompleted;

    public WorkspaceSession(string directory)
    {
        Store = new WorkspaceStore(directory);
        Store.Changed += OnStoreChanged;
        InitializeExistingFlow();
    }

    public MainWindow OpenHome()
    {
        var created = _home is null;
        if (_home is null)
        {
            _home = new MainWindow(this);
            _home.Closed += (_, _) => _home = null;
        }
        if (!_home.IsVisible) _home.Show();
        if (_home.WindowState == WindowState.Minimized) _home.WindowState = WindowState.Normal;
        _home.Activate();
        if(Store.Snapshot.Clipboard.Floating&&_clipboardWindow is null)PopoutClipboard();
        Synchronize();
        if (created)
        {
            var state = Store.Snapshot;
            var widget = state.Widgets.SingleOrDefault(w => w.MicheId == state.Index.ActiveMicheId);
            if (widget is not null && state.Placements.Single(p => p.WidgetInstanceId == widget.Id).Mode == "docked")
            {
                _home.UpdateLayout();
                var view = ViewFor(widget.Id);
                view.Editor.Focus(); view.RestoreCursor(WorkspaceStore.EditorFor(widget));
            }
        }
        return _home;
    }

    public DumpView ViewFor(Guid widgetId)
    {
        if (!_views.TryGetValue(widgetId, out var view))
        { view = new DumpView(this, widgetId); _views.Add(widgetId, view); }
        return view;
    }

    public bool Act(Action action, string message = "Saved on this Mac.")
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException)
        { Notify(StatusChanged,ex.Message); Notify(ErrorOccurred,ex.Message); return false; }
        Notify(StatusChanged,Store.LastNotificationWarning ?? message); return true;
    }
    private static void Notify(Action<string>? observers,string message)
    { if(observers is not null) foreach(Action<string> observer in observers.GetInvocationList()) try {observer(message);} catch(Exception) { } }

    public void OpenDump(bool local = false)
    {
        var before = Store.Snapshot.Widgets.SingleOrDefault(w => w.MicheId == Store.Snapshot.Index.ActiveMicheId);
        var view = before is null ? null : ViewFor(before.Id);
        view?.StopAutosave();
        if (!Act(() => Store.ShowDump(true, local, view?.EditorSnapshot()))) return;
        var state = Store.Snapshot;
        var widget = state.Widgets.Single(w => w.MicheId == state.Index.ActiveMicheId);
        if (_floating.TryGetValue(widget.Id, out var window)) { window.Show(); window.Activate(); }
        var editor=WorkspaceStore.EditorFor(widget);
        Dispatcher.UIThread.Post(() => {var target=ViewFor(widget.Id);target.Editor.Focus();target.RestoreCursor(editor);});
    }

    public void PopOut(Guid widgetId)
    {
        if (_floating.TryGetValue(widgetId, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Show(); existing.Activate(); return;
        }
        Act(() => Store.SetHost(widgetId, "floating", ViewFor(widgetId).EditorSnapshot()));
    }

    public void Dock(Guid widgetId)
    {
        var editor = ViewFor(widgetId).EditorSnapshot();
        if (Act(() => Store.SetHost(widgetId, "docked", editor, BoundsFor(widgetId), activateOrigin: true)))
        {
            if (_home is null || !_home.IsVisible) OpenHome();
            Dispatcher.UIThread.Post(() => { var view = ViewFor(widgetId); view.Editor.Focus(); view.RestoreCursor(editor); });
        }
    }

    public void HideWidget(Guid widgetId) =>
        Act(() => Store.SetHost(widgetId, "hidden", ViewFor(widgetId).EditorSnapshot(), BoundsFor(widgetId)),
            "Dump hidden here. Your draft and captures are still saved.");

    public bool SaveEditor(DumpView view) => Act(() => Store.SaveEditors(new Dictionary<Guid, EditorState> {
        [view.WidgetId] = view.EditorSnapshot()
    }), "Draft saved on this Mac.");

    public void SaveAllEditors()
    {
        _home?.CancelDeskGesture();
        if (_home is not null && !_home.PrepareVisionToLeave()) throw new IOException("Vision draft could not be saved. Keep this window open and retry.");
        if (_flowWindow is not null && !_flowWindow.SaveBeforeClose()) throw new IOException("Flow could not save. Keep its window open and retry.");
        if(_clipboardWindow is not null)Store.SetClipboardFloating(true,_clipboardWindow.Geometry());
        Store.SaveEditors(_views.ToDictionary(p => p.Key, p => p.Value.EditorSnapshot()),
            _floating.ToDictionary(p => p.Key, p => p.Value.Geometry()));
    }

    public bool BeforeHomeClose()
    {
        if (IsQuitting) return true;
        if (!Act(SaveAllEditors)) return false;
        if (_floating.Count > 0 || _clipboardWindow?.IsVisible == true || _flowWindow?.IsVisible == true || _meiliWindow?.IsVisible == true) _home?.Hide();
        else Dispatcher.UIThread.Post(() => TryQuit());
        return false;
    }

    public bool TryQuit()
    {
        if (IsQuitting) return true;
        if (_meiliWindow is not null && !_meiliQuitApproved)
        {
            if (!_meiliQuitPending) { _meiliQuitPending=true; _meiliWindow.Flush(ok=>{_meiliQuitPending=false;if(ok){_meiliQuitApproved=true;TryQuit();}}); }
            return false;
        }
        if (!Act(SaveAllEditors, "Drafts saved.")) return false;
        IsQuitting = true;
        foreach (var view in _views.Values) view.StopAutosave();
        foreach (var window in _floating.Values.ToArray()) window.CloseForTransfer();
        _floating.Clear();
        _clipboardWindow?.CloseForTransfer();
        _flowWindow?.Close();
        _meiliWindow?.CloseSaved();
        _home?.Close();
        Dispose();
        QuitCompleted?.Invoke();
        return true;
    }

    private WindowGeometry? BoundsFor(Guid widgetId) => _floating.TryGetValue(widgetId, out var window) ? window.Geometry() : null;
    private void OnStoreChanged()
    {
        Synchronize();
        foreach (var view in _views.Values) view.Refresh();
        Changed?.Invoke();
    }

    private void Synchronize()
    {
        var state = Store.Snapshot;
        var alive = state.Index.Miches.Select(m => m.Id).ToHashSet();
        var wanted = state.Placements.Where(p => p.Mode == "floating" &&
            state.Widgets.Any(w => w.Id == p.WidgetInstanceId && alive.Contains(w.MicheId))).Select(p => p.WidgetInstanceId).ToHashSet();
        foreach (var id in _floating.Keys.Where(id => !wanted.Contains(id)).ToArray())
        {
            var window = _floating[id];
            if (window.Content is DumpView view) Detach(view);
            _floating.Remove(id);
            window.CloseForTransfer();
        }
        foreach (var id in wanted.Where(id => !_floating.ContainsKey(id)))
        {
            var widget = state.Widgets.Single(w => w.Id == id);
            var niche = state.Index.Miches.Single(m => m.Id == widget.MicheId);
            var view = ViewFor(id);
            Detach(view);
            var window = new WidgetWindow(this, id, niche.Name, state.Placements.Single(p => p.WidgetInstanceId == id).Window) { Content = view };
            _floating.Add(id, window);
            window.Show();
            window.UpdateLayout();
            view.Editor.Focus();
            view.RestoreCursor(WorkspaceStore.EditorFor(widget));
        }
        foreach (var (id, window) in _floating)
        {
            var widget = state.Widgets.Single(w => w.Id == id);
            window.Title = "Dump · " + state.Index.Miches.Single(m => m.Id == widget.MicheId).Name;
        }
        if (_floating.Count == 0 && _clipboardWindow?.IsVisible != true && _flowWindow?.IsVisible != true && _home is not null && !_home.IsVisible && !IsQuitting) OpenHome();
    }

    public static void Detach(DumpView view)
    {
        var oldRoot = TopLevel.GetTopLevel(view);
        view.GetVisualAncestors().OfType<WidgetDesk>().FirstOrDefault()?.CancelGesture();
        if (view.Parent is ContentControl parent && ReferenceEquals(parent.Content, view)) parent.Content = null;
        // Drain the old root's queued layout while the editor is detached. Avalonia's
        // pending arrange queue can otherwise follow the moved control into a new root.
        oldRoot?.UpdateLayout();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var view in _views.Values) view.StopAutosave();
        _flowClock.Stop();
        _flow?.Dispose();
        Store.Changed -= OnStoreChanged;
        Store.Dispose();
    }
}
