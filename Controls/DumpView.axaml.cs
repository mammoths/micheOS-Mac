using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Miche.Mac.Models;
using Miche.Mac.Services;

namespace Miche.Mac.Controls;

public partial class DumpView : UserControl
{
    private readonly WorkspaceSession _session;
    private readonly DispatcherTimer _draftTimer;
    private bool _restoring = true;
    private string _scope = "canonical";
    private Guid? _lastFlush;
    public Guid WidgetId { get; }
    public Guid MicheId { get; }
    public TextBox Editor => CaptureBox;

    public DumpView(WorkspaceSession session, Guid widgetId)
    {
        _session = session;
        WidgetId = widgetId;
        var widget = session.Store.Snapshot.Widgets.Single(w => w.Id == widgetId);
        MicheId = widget.MicheId;
        InitializeComponent();
        CaptureBox.AddHandler(InputElement.KeyDownEvent, CaptureKeyDown, RoutingStrategies.Tunnel);
        _scope = widget.CaptureScope;
        var initial = WorkspaceStore.EditorFor(widget);
        CaptureBox.Text = initial.Text;
        RestoreCursor(initial);
        _draftTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _draftTimer.Tick += (_, _) => { _draftTimer.Stop(); _session.SaveEditor(this); };
        _restoring = false;
        _session.ErrorOccurred += message => { Feedback.Text = message; Feedback.IsVisible = true; };
        Refresh();
    }

    public EditorState EditorSnapshot() => new() {
        Text = CaptureBox.Text ?? "", CaretIndex = CaptureBox.CaretIndex,
        SelectionStart = CaptureBox.SelectionStart, SelectionEnd = CaptureBox.SelectionEnd
    };
    public void StopAutosave() => _draftTimer.Stop();
    public void RestoreCursor(EditorState state)
    {
        CaptureBox.CaretIndex = state.CaretIndex;
        CaptureBox.SelectionStart = state.SelectionStart;
        CaptureBox.SelectionEnd = state.SelectionEnd;
    }
    public void Refresh()
    {
        var state = _session.Store.Snapshot;
        var widget = state.Widgets.Single(w => w.Id == WidgetId);
        if (_scope != widget.CaptureScope)
        {
            FlushPanel.IsVisible = false; ArchivePanel.IsVisible = false;
            _draftTimer.Stop(); _scope = widget.CaptureScope; _restoring = true;
            var draft = WorkspaceStore.EditorFor(widget); CaptureBox.IsUndoEnabled = false;
            CaptureBox.Text = draft.Text; RestoreCursor(draft); CaptureBox.IsUndoEnabled = true; _restoring = false;
        }
        ScopeLabel.Text = _scope == "canonical" ? "all Miches · shared capture" : "this Miche · local capture";
        UndoFlushButton.IsVisible = _lastFlush.HasValue;
        var entries = _session.Store.DumpEntries(_scope == "local" ? MicheId : null);
        FlushButton.IsEnabled = entries.Count > 0;
        if(FlushPanel.IsVisible) UpdateFlushPrompt(entries.Count);
        var floating = state.Placements.Single(p => p.WidgetInstanceId == WidgetId).Mode == "floating";
        PopOutButton.IsVisible = !floating;
        DockButton.IsVisible = floating;
        HomeButton.IsVisible = floating;
        ResizeGrip.IsVisible = !floating;
        FloatWindowButtons.IsVisible = floating;
        CaptureList.Children.Clear();
        foreach (var note in entries)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(new StackPanel { Spacing = 6, Margin = new Thickness(0,10,18,10), Children = {
                new TextBlock { Text = note.Text, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = note.OriginMicheName + " · " + note.CreatedAt.ToLocalTime().ToString("MMM d, h:mm tt"),
                    FontSize = 11, Foreground = Brush.Parse("#BEA8B9") }
            } });
            var remove = new Button { Content = "×", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            Avalonia.Automation.AutomationProperties.SetName(remove, "Delete capture: " + note.Text[..Math.Min(50, note.Text.Length)]);
            remove.Click += (_, _) => _session.Act(() => _session.Store.SetCaptureDeleted(note.Id, true), "Capture moved to recently deleted.");
            Grid.SetColumn(remove, 1); row.Children.Add(remove); CaptureList.Children.Add(row);
        }
        if (CaptureList.Children.Count == 0)
            CaptureList.Children.Add(new TextBlock { Text = "nothing here yet. give a thought somewhere to land.",
                Foreground = Brush.Parse("#BEA8B9"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12) });
    }
    public void SwitchScope(bool local)
    {
        _draftTimer.Stop();
        if (_session.Act(() => _session.Store.SetDumpScope(WidgetId,local,EditorSnapshot())))
        { FlushPanel.IsVisible = false; ArchivePanel.IsVisible = false; CaptureBox.Focus(); }
    }
    private void SharedScope(object? sender, RoutedEventArgs e) => SwitchScope(false);
    private void LocalScope(object? sender, RoutedEventArgs e) => SwitchScope(true);
    private void BeginFlush(object? sender, RoutedEventArgs e)
    {
        var entries = _session.Store.DumpEntries(_scope == "local" ? MicheId : null);
        UpdateFlushPrompt(entries.Count);
        FlushPanel.IsVisible = true; ArchivePanel.IsVisible = false;
    }
    private void UpdateFlushPrompt(int count) => FlushPrompt.Text = $"Archive {count} captures from " + (_scope == "local" ? "this Miche?" : "all Miches? Local captures are included.") + " You can restore them anytime.";
    private void CancelFlush(object? sender, RoutedEventArgs e) => FlushPanel.IsVisible = false;
    public bool ConfirmFlush()
    {
        if (!_session.Act(() => _lastFlush = _session.Store.FlushDump(_scope == "local" ? MicheId : null), "Captures archived. Your draft is untouched.")) return false;
        FlushPanel.IsVisible = false; Refresh(); return true;
    }
    private void FlushConfirmed(object? sender, RoutedEventArgs e) => ConfirmFlush();
    private void UndoFlush(object? sender, RoutedEventArgs e)
    { if (_lastFlush is { } id && _session.Act(() => _session.Store.RestoreDumpBatch(id))) { _lastFlush = null; Refresh(); } }
    private void ShowArchives(object? sender, RoutedEventArgs e)
    {
        FlushPanel.IsVisible = false; ArchivePanel.IsVisible = !ArchivePanel.IsVisible; if (!ArchivePanel.IsVisible) return;
        ArchiveList.Children.Clear();
        var state = _session.Store.Snapshot;
        foreach (var batch in state.DumpArchives.Where(b => (_scope == "canonical" || b.CaptureIds.Any(id => state.RootDump.Any(n => n.Id == id && n.OwnerMicheId == MicheId))) &&
            b.CaptureIds.Any(id => state.RootDump.Any(n => n.Id == id && n.ArchiveBatchId == b.Id && n.DeletedAt is null))).OrderByDescending(b => b.CreatedAt))
        {
            // A local view can see a canonical batch containing its records but
            // restoration still has the batch's original breadth, shown explicitly.
            var count=state.RootDump.Count(n=>n.ArchiveBatchId==batch.Id && n.DeletedAt is null);
            var button = new Button { Content = "restore " + count + " · " + batch.ScopeTitle + " · " + batch.CreatedAt.ToLocalTime().ToString("MMM d, h:mm tt"),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            button.Click += (_, _) => { if (_session.Act(() => _session.Store.RestoreDumpBatch(batch.Id))) ArchivePanel.IsVisible = false; };
            ArchiveList.Children.Add(button);
        }
        if (ArchiveList.Children.Count == 0) ArchiveList.Children.Add(new TextBlock { Text = "no past flushes" });
    }
    private void DraftChanged(object? sender, TextChangedEventArgs e)
    {
        if (_restoring || _draftTimer is null) return;
        _draftTimer.Stop(); _draftTimer.Start();
    }
    private void Send(object? sender, RoutedEventArgs e) => Submit();
    private void Submit()
    {
        if (_session.Act(() => _session.Store.Capture(CaptureBox.Text ?? "", MicheId, WidgetId)))
        {
            _restoring = true; CaptureBox.Text = ""; _restoring = false;
            Feedback.IsVisible = false;
            _draftTimer.Stop(); CaptureBox.Focus();
        }
    }
    private void CaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { Submit(); e.Handled = true; }
    }
    private void PopOut(object? sender, RoutedEventArgs e) => _session.PopOut(WidgetId);
    private void Dock(object? sender, RoutedEventArgs e) => _session.Dock(WidgetId);
    private void Hide(object? sender, RoutedEventArgs e) => _session.HideWidget(WidgetId);
    private void Home(object? sender, RoutedEventArgs e) => _session.OpenHome();
    private void ResizePressed(object? sender, PointerPressedEventArgs e) =>
        this.GetVisualAncestors().OfType<WidgetDesk>().FirstOrDefault()?.BeginGesture(e, resize: true);
    private void HeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is Control control && (control is Button || control.GetVisualAncestors().Any(p => p is Button or TextBox))) return;
        if (_session.FloatingWindows.TryGetValue(WidgetId, out var window)) { MacWindowChrome.HeaderPressed(window,e); }
        else this.GetVisualAncestors().OfType<WidgetDesk>().FirstOrDefault()?.BeginGesture(e, resize: false);
    }
}
