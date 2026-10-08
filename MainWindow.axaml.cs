using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Miche.Mac.Services;
using Miche.Mac.Controls;

namespace Miche.Mac;

public partial class MainWindow : Window
{
    private readonly WorkspaceStore? _store;
    private bool _renaming;
    private KeyModifiers _inputModifiers;
    private Control? _commandReturnFocus;
    private Guid _menuActiveId;
    private (Guid Id, string Name)[]? _menuMiches;

    private readonly WorkspaceSession _session;
    private ClipboardView? _clipboardView;
    public MainWindow(WorkspaceSession session)
    {
        _session = session; _store = session.Store;
        InitializeComponent();
        MacWindowChrome.Apply(this);
        VisionOverlay.Connect(_session);
        VisionOverlay.CloseRequested += () => { VisionOverlay.IsVisible = false; SpaceMenuButton.Focus(); };
        DateLabel.Text = DateTime.Now.ToString("ddd / dd MMM").ToUpperInvariant();
        _session.Changed += Refresh;
        _session.StatusChanged += ShowStatus;
        Closed += (_, _) => { VisionOverlay.Dispose(); _session.Changed -= Refresh; _session.StatusChanged -= ShowStatus; };
        Closing += (_, e) => { if (!_session.IsQuitting) e.Cancel = !_session.BeforeHomeClose(); };
        Refresh();
        AddHandler(InputElement.TextInputEvent, (_, e) => {
            if (e.Text != "/" || _inputModifiers != KeyModifiers.None || IsTextEntry(e.Source) || NamingPanel.IsVisible || ManagePanel.IsVisible || CommandOverlay.IsVisible) return;
            if (OpenCommands()) e.Handled = true;
        }, RoutingStrategies.Tunnel);
        KeyUp += (_, e) => _inputModifiers = e.KeyModifiers;
        KeyDown += (_, e) => {
            _inputModifiers = e.KeyModifiers;
            if (e.Handled) return;
            if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.None && !IsTextEntry(e.Source) && !NamingPanel.IsVisible && !ManagePanel.IsVisible)
            { ToggleVision(); e.Handled = true; }
            if (e.Key == Key.Escape && !VisionOverlay.IsVisible)
            {
                if (CommandOverlay.IsVisible) { CloseCommands(); e.Handled = true; return; }
                if (!NamingPanel.IsVisible && !ManagePanel.IsVisible && !LookupSuggestions.IsOpen && WindowState == WindowState.FullScreen) WindowState = WindowState.Normal;
                else CloseOverlays();
                e.Handled = true;
            }
            if (e.Key == Key.N && e.KeyModifiers == KeyModifiers.Meta) { BeginName(false); e.Handled = true; }
            if (e.Key == Key.Q && e.KeyModifiers == KeyModifiers.Meta) { _session.TryQuit(); e.Handled = true; }
        };
    }
    private static bool IsTextEntry(object? source) => source is TextBox || source is Control control && control.GetVisualAncestors().Any(v => v is TextBox);
    public bool OpenCommands()
    {
        if (NamingPanel.IsVisible || ManagePanel.IsVisible) return false;
        if (!PrepareVisionToLeave()) return false;
        _commandReturnFocus = FocusManager?.GetFocusedElement() as Control;
        VisionOverlay.IsVisible = false; CancelDeskGesture(); CloseOverlays();
        CommandOverlay.IsVisible = true; CommandBox.Text = "/";
        CommandBox.Focus(); CommandBox.CaretIndex = 1; UpdateLookup(); return true;
    }
    private void CloseCommands()
    {
        CommandOverlay.IsVisible = false;
        if (_commandReturnFocus is { IsEffectivelyVisible: true }) _commandReturnFocus.Focus(); else SpaceMenuButton.Focus();
        _commandReturnFocus = null;
    }
    private void CancelCommands(object? sender, RoutedEventArgs e) => CloseCommands();
    private void WindowHeaderPressed(object? sender, PointerPressedEventArgs e) => MacWindowChrome.HeaderPressed(this,e);
    private void ShowStatus(string message) => Status.Text = message;

    public void RefreshClipboard()=>Refresh();
    private void Refresh()
    {
        if (_store is null) return;
        var state = _store.Snapshot;
        var active = state.Index.Miches.Single(m => m.Id == state.Index.ActiveMicheId);
        var names = state.Index.Miches.Select(m => (m.Id, m.Name)).ToArray();
        // Autosaving a draft must not dismiss a menu the user just opened.
        if (_menuActiveId != state.Index.ActiveMicheId || _menuMiches?.SequenceEqual(names) != true)
        {
            _menuActiveId = state.Index.ActiveMicheId; _menuMiches = names;
            SpaceMenuButton.ContextMenu?.Close();
            var menu = new ContextMenu { Background = Brush.Parse("#2C2033"), BorderBrush = Brush.Parse("#5D435C"), BorderThickness = new Thickness(1) };
            foreach (var miche in state.Index.Miches)
            {
                var item = new MenuItem { Header = miche.Name, FontSize = 13 };
                item.Click += (_, _) => { Act(() => { _session.SaveAllEditors(); _store.Activate(miche.Id); }); CloseOverlays(); };
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var create = new MenuItem { Header = "+ new" }; create.Click += NewSpace; menu.Items.Add(create);
            var rename = new MenuItem { Header = "rename this niche" }; rename.Click += RenameSpace; menu.Items.Add(rename);
            var manage = new MenuItem { Header = "manage · recently deleted" }; manage.Click += ManageSpace; menu.Items.Add(manage);
            var vision = new MenuItem { Header = "vision · V" }; vision.Click += (_, _) => ToggleVision(); menu.Items.Add(vision);
            var flow = new MenuItem {Header="flow · local time blocks"}; flow.Click += OpenFlow; menu.Items.Add(flow);
            var search = new MenuItem { Header = "search · /" }; search.Click += (_, _) => OpenCommands(); menu.Items.Add(search);
            menu.Items.Add(new Separator());
            var quit = new MenuItem { Header = "quit · ⌘Q" }; quit.Click += (_, _) => _session.TryQuit(); menu.Items.Add(quit);
            SpaceMenuButton.ContextMenu = menu;
        }
        SpaceTitle.Text = active.Name;
        Title = "Miche · " + active.Name;
        var widget = state.Widgets.SingleOrDefault(w => w.MicheId == active.Id);
        var placement = widget is null ? null : state.Placements.Single(p => p.WidgetInstanceId == widget.Id);
        var showDump = placement?.Mode == "docked";
        DumpHost.IsVisible = showDump; DeskScroll.IsVisible = showDump;
        if (showDump)
        {
            DeskPanel.Configure(placement!, p => _session.Act(() => _store.SetDeskPlacement(p.WidgetInstanceId,
                p.PosX, p.PosY, p.WidthFraction, p.Height), "Desk position saved."));
            var view = _session.ViewFor(widget!.Id);
            if (!ReferenceEquals(DumpHost.Content, view))
            {
                var editor = view.EditorSnapshot();
                WorkspaceSession.Detach(view); DumpHost.Content = view;
                UpdateLayout();
                view.Editor.Focus(); view.RestoreCursor(editor);
            }
        }
        else { DeskPanel.CancelGesture(); DumpHost.Content = null; }
        DumpHost.IsVisible = showDump;
        DeskScroll.IsVisible = showDump;
        CompactHeader.IsVisible = showDump;
        var showClipboard=state.Clipboard.VisibleIn.Contains(active.Id)&&!state.Clipboard.Floating;
        if(showClipboard){_clipboardView??=_session.ClipboardView;ClipboardHost.Content=_clipboardView;_clipboardView.Refresh();}
        ClipboardHost.IsVisible=showClipboard;
        if(!showClipboard)ClipboardHost.Content=null;
        DeskPanel.IsVisible=showDump;
        DeskScroll.IsVisible=CompactHeader.IsVisible=showDump||showClipboard;
        EmptyBoard.IsVisible = !showDump&&!showClipboard;
        DeleteSpaceButton.IsEnabled = active.Id != state.Index.RootMicheId;
        TrashList.Children.Clear();
        foreach (var archived in state.Trash.OrderByDescending(n => n.DeletedAt))
            AddRecoveryRow("niche · " + archived.Miche.Name, () => _store.Restore(archived.Miche.Id));
        foreach (var note in state.RootDump.Where(n => n.DeletedAt is not null).OrderByDescending(n => n.DeletedAt))
            AddRecoveryRow("capture · " + note.Text, () => _store.SetCaptureDeleted(note.Id, false));
        if (TrashList.Children.Count == 0) TrashList.Children.Add(new TextBlock { Text = "nothing deleted", Foreground = Brush.Parse("#BEA8B9") });
    }

    private void AddRecoveryRow(string text, Action restore)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,16,8) });
        var button = new Button { Content = "restore", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        button.Click += (_, _) => Act(restore, "Restored.");
        Grid.SetColumn(button, 1); row.Children.Add(button); TrashList.Children.Add(row);
    }

    private bool Act(Action action, string message = "Saved on this Mac.") => _session.Act(action, message);
    public void CancelDeskGesture() => DeskPanel.CancelGesture();
    public bool PrepareVisionToLeave() => VisionOverlay.PrepareToLeave();
    public void ToggleVision()
    {
        if (VisionOverlay.IsVisible)
        { if (VisionOverlay.PrepareToLeave()) { VisionOverlay.IsVisible = false; SpaceMenuButton.Focus(); } }
        else
        {
            CancelDeskGesture(); CloseOverlays(); VisionOverlay.IsVisible = true; VisionOverlay.RefreshBoard();
            Dispatcher.UIThread.Post(() => { if (VisionOverlay.IsVisible) VisionOverlay.BoardCanvas.Focus(); });
        }
    }

    private void OpenSpaceMenu(object? sender, RoutedEventArgs e) => SpaceMenuButton.ContextMenu?.Open(SpaceMenuButton);
    private void NewSpace(object? sender, RoutedEventArgs e) => BeginName(false);
    private void RenameSpace(object? sender, RoutedEventArgs e) => BeginName(true);
    private void BeginName(bool rename)
    {
        if (_store is null) return;
        if (!PrepareVisionToLeave()) return; VisionOverlay.IsVisible = false;
        _renaming = rename;
        CloseOverlays(); Board.IsVisible = false; NamingPanel.IsVisible = true;
        NamingTitle.Text = rename ? "a new name for this miche" : "a little space of your own";
        NameBox.Text = rename ? _store.Snapshot.Index.Miches.Single(m => m.Id == _store.Snapshot.Index.ActiveMicheId).Name : "";
        Dispatcher.UIThread.Post(() => { NameBox.Focus(); NameBox.SelectAll(); });
    }
    private void ConfirmName(object? sender, RoutedEventArgs e) => SubmitName();
    private void SubmitName()
    {
        if (_store is null) return;
        var success = Act(() => {
            if (_renaming) _store.Rename(_store.Snapshot.Index.ActiveMicheId, NameBox.Text ?? "");
            else { _session.SaveAllEditors(); _store.Create(NameBox.Text ?? ""); }
        });
        if (success) CloseOverlays();
    }
    private void NameKeyDown(object? sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { SubmitName(); e.Handled = true; } }
    private void ManageSpace(object? sender, RoutedEventArgs e)
    { if (!PrepareVisionToLeave()) return; VisionOverlay.IsVisible = false; CloseOverlays(); Refresh(); Board.IsVisible = false; ManagePanel.IsVisible = true; }
    private void DeleteSpace(object? sender, RoutedEventArgs e)
    { if (_store is not null) Act(() => { _session.SaveAllEditors(); _store.Delete(_store.Snapshot.Index.ActiveMicheId); }, "Moved to recently deleted. Restore it here anytime."); }
    private void CancelOverlay(object? sender, RoutedEventArgs e) => CloseOverlays();
    private void CloseOverlays()
    { SpaceMenuButton.ContextMenu?.Close(); LookupSuggestions.IsOpen = false;
      CommandOverlay.IsVisible = false; NamingPanel.IsVisible = false; ManagePanel.IsVisible = false; Board.IsVisible = true; }
    private void OpenMeili(object? sender, RoutedEventArgs e)
    { if (!PrepareVisionToLeave()) return; _session.OpenMeili(); QueryBox.Text=""; CloseOverlays(); }
    private void OpenFlow(object? sender, RoutedEventArgs e)
    { if (!PrepareVisionToLeave()) return; if (_session.OpenFlow()) { QueryBox.Text=""; CloseOverlays(); } }
    private void AddDump(object? sender, RoutedEventArgs e)
    { QueryBox.Text = ""; CommandOverlay.IsVisible = false; LookupSuggestions.IsOpen = false; _session.OpenDump(); }
    private void AddLocalDump(object? sender, RoutedEventArgs e)
    { QueryBox.Text = ""; CommandOverlay.IsVisible = false; LookupSuggestions.IsOpen = false; _session.OpenDump(local:true); }

    private void QueryFocused(object? sender, GotFocusEventArgs e) => UpdateLookup();
    private void QueryChanged(object? sender, TextChangedEventArgs e)
    { if (QueryBox is not null && CommandBox is not null && LookupSuggestions is not null && (QueryBox.IsFocused || CommandBox.IsFocused)) UpdateLookup(); }
    private void UpdateLookup()
    {
        var query = ((CommandOverlay.IsVisible ? CommandBox.Text : QueryBox.Text) ?? "").Trim().TrimStart('/');
        var matches = query.Length == 0 || "dump".Contains(query, StringComparison.OrdinalIgnoreCase);
        var newMatches = query.Length == 0 || "new".Contains(query, StringComparison.OrdinalIgnoreCase);
        var groundMatches = query.Length == 0 || "dumping-ground".Contains(query,StringComparison.OrdinalIgnoreCase);
        var flowMatches=query.Length==0 || "flow".Contains(query,StringComparison.OrdinalIgnoreCase) || query.Equals("today",StringComparison.OrdinalIgnoreCase);
        FlowSuggestion.IsVisible=CommandFlowSuggestion.IsVisible=flowMatches;
        MeiliSuggestion.IsVisible=CommandMeiliSuggestion.IsVisible=query.Length==0 || "thedailymeili".Contains(query,StringComparison.OrdinalIgnoreCase) || "meili".Equals(query,StringComparison.OrdinalIgnoreCase);
        ClipboardSuggestion.IsVisible=CommandClipboardSuggestion.IsVisible=query.Length==0||"clipboard".Contains(query,StringComparison.OrdinalIgnoreCase);
        var localMatches = query.Length == 0 || "local-dump".Contains(query,StringComparison.OrdinalIgnoreCase);
        DumpSuggestion.IsVisible = CommandDumpSuggestion.IsVisible = matches;
        NewSuggestion.IsVisible = CommandNewSuggestion.IsVisible = newMatches;
        GroundSuggestion.IsVisible = CommandGroundSuggestion.IsVisible = groundMatches;
        LocalSuggestion.IsVisible = CommandLocalSuggestion.IsVisible = localMatches;
        LookupHint.Text = CommandHint.Text = flowMatches && query.Length>0 ? "local Flow · shared tasks for this Mac" : matches || groundMatches ? "one collection · captures from all Miches" : newMatches ? "a little space of your own" : localMatches ? "capture and browse only this Miche’s local Dump" : "Try /thedailymeili, /dump, /new or /flow.";
        LookupSuggestions.IsOpen = !CommandOverlay.IsVisible;
    }
    private void OpenClipboard(object? sender,RoutedEventArgs e)
    {
        if(!PrepareVisionToLeave())return;VisionOverlay.IsVisible=false;CloseOverlays();
        _session.OpenClipboard();
    }
    private void QueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var query = ((sender as TextBox)?.Text ?? "").Trim().TrimStart('/');
            if (query.Equals("new", StringComparison.OrdinalIgnoreCase)) { BeginName(false); QueryBox.Text = ""; }
            else if (query.Equals("thedailymeili",StringComparison.OrdinalIgnoreCase) || query.Equals("meili",StringComparison.OrdinalIgnoreCase)) OpenMeili(sender,e);
            else if (query.Equals("flow",StringComparison.OrdinalIgnoreCase) || query.Equals("today",StringComparison.OrdinalIgnoreCase)) OpenFlow(sender,e);
            else if (query.Equals("clipboard",StringComparison.OrdinalIgnoreCase)) OpenClipboard(sender,e);
            else if (query.Equals("local-dump",StringComparison.OrdinalIgnoreCase)) AddLocalDump(sender,e);
            else if (query.Equals("dumping-ground",StringComparison.OrdinalIgnoreCase)) AddDump(sender,e);
            else if (query.Length == 0 || "dump".Contains(query,StringComparison.OrdinalIgnoreCase)) AddDump(sender,e);
            else { e.Handled = true; return; }
            e.Handled = true;
        }
        if (e.Key == Key.Escape) { if (CommandOverlay.IsVisible) CloseCommands(); else { LookupSuggestions.IsOpen = false; SpaceMenuButton.Focus(); } e.Handled = true; }
    }
}
