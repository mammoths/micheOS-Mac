using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Miche.Mac.Models;
using Miche.Mac.Services;

namespace Miche.Mac.Controls;

public sealed partial class VisionView : UserControl, IDisposable
{
    private WorkspaceSession? _session;
    private Guid _micheId;
    private Guid? _selected;
    private readonly Grid _root = new() { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent, Height = 1600, Focusable = true };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    private readonly StackPanel _tools = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly TextBlock _message = new() { Foreground = Brush.Parse("#BEA8B9"), FontSize = 11, FontFamily = new FontFamily("Menlo"), TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
    private readonly Dictionary<string,Bitmap> _images = new();
    private readonly Dictionary<Guid,Border> _objects = new();
    private TextBox? _editor;
    private VisionItem? _editing;
    private bool _committing;
    private CanvasGesture? _gesture;
    private WrapPanel _bar = null!;
    private sealed record CanvasGesture(VisionItem Before, Point Start, IPointer Pointer, bool Resize)
    { public VisionItem Preview { get; set; } = WorkspaceStore.CopyVision(Before); public bool Started { get; set; } }
    public Canvas BoardCanvas => _canvas;
    public TextBox? DraftEditor => _editor;
    public Guid? SelectedId => _selected;
    public IReadOnlyDictionary<Guid,Border> ObjectControls => _objects;
    public ScrollViewer BoardScroll => _scroll;
    public event Action? CloseRequested;
    public VisionView()
    {
        Focusable = true; Background = Brush.Parse("#241B2D"); Content = _root;
        var bar = _bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12,8) };
        bar.Children.Add(_caption);
        AddButton(bar, "table +",()=>AddTable(VisibleInsertionPoint));
        AddButton(bar, "image +", async () => await PickImage(VisibleInsertionPoint));
        _shapeButton=AddButton(bar, "shape +", () => ShowShapes(VisibleInsertionPoint));
        _root.Children.Add(_shapePicker);
        AddButton(bar, "recently deleted", ShowRecovery);
        AddButton(bar, "close · esc", () => { if (PrepareToLeave()) CloseRequested?.Invoke(); });
        bar.Children.Add(_message);
        _root.Children.Add(bar); Grid.SetRow(_scroll,1); _root.Children.Add(_scroll); InitializeZoom();
        Grid.SetRow(_tools,2);_tools.Margin=new Thickness(8,4);_tools.VerticalAlignment=VerticalAlignment.Center;_root.Children.Add(_tools);
        InitializeLifecycle(bar);
        InitializeMicheMentions();
        ScrollViewer.SetBringIntoViewOnFocusChange(_canvas,false);
        _scroll.SizeChanged += (_, _) => UpdateExtent();
        _canvas.PointerPressed += BlankPressed;
        _canvas.PointerMoved += MovePointer;
        _canvas.PointerReleased += ReleasePointer;
        _canvas.PointerCaptureLost += (_, _) => CancelGesture();
        AddHandler(KeyDownEvent, CanvasKeyDown, RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, (_, e) => { e.DragEffects = e.DataTransfer.TryGetFiles()?.Any() == true ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; });
        DragDrop.AddDropHandler(this, (_, e) => {
            e.Handled = true;
            var point = e.GetPosition(_canvas);
            foreach (var file in e.DataTransfer.TryGetFiles() ?? Array.Empty<IStorageItem>())
            { var path = file.TryGetLocalPath(); if (path is not null) { ImportImage(path,point); point += new Vector(24,24); } }
        });
    }
    private static Button AddButton(Panel parent, string text, Action action)
    { var button = new Button { Content = text, FontSize = 11, Margin = new Thickness(4,2) }; button.Click += (_, _) => action(); parent.Children.Add(button); return button; }
    public void Connect(WorkspaceSession session)
    {
        _session = session; _micheId = CalendarMode ? session.Store.Snapshot.Index.RootMicheId : session.Store.Snapshot.Index.ActiveMicheId;
        session.Changed += DataChanged;
        session.ErrorOccurred += Error;
        RefreshBoard();
    }
    private void Error(string message) => _message.Text = message;
    private void DataChanged()
    {
        if (_committing || HasCanvasGesture) return;
        if(CalendarMode){RefreshBoard();return;}
        var active = _session!.Store.Snapshot.Index.ActiveMicheId;
        if (_micheId != active)
        {
            if (_editor is not null) return; // transitions must first commit using the original identity
            _micheId = active; _artifactId=null; CloseDialog(); _selected = null; _selection.Clear(); _scroll.Offset = default;
        }
        if(_artifactId is { } id && !_session.Store.Snapshot.VisionArtifacts.Any(a=>a.Id==id && a.DeletedAt is null)) _artifactId=null;
        RefreshBoard();
    }
    private List<VisionItem> Items()
    {
        var state=_session?.Store.Snapshot;
        if(CalendarMode)return state?.Calendar.Items.Where(i=>i.Month==_calendarMonth&&i.Date==_calendarDate).Select(i=>WorkspaceStore.CopyVision(i.Content)).ToList()??new();
        return (_artifactId is { } id ? state?.VisionArtifacts.SingleOrDefault(a=>a.Id==id && a.MicheId==_micheId)?.Items : state?.VisionBoards.SingleOrDefault(b=>b.MicheId==_micheId)?.Items) ?? new();
    }
    public void RefreshBoard()
    {
        if (_session is null || HasCanvasGesture) return;
        if(!CalendarMode)
        {var miche=_session.Store.Snapshot.Index.Miches.SingleOrDefault(m=>m.Id==_micheId);if(miche is not null)Background=Brush.Parse(MichePalette.For(miche).Surface);}
        StyleLinkedEditor();
        var liveIds=Items().Where(i=>i.DeletedAt is null).Select(i=>i.Id).ToHashSet();_selection.RemoveWhere(id=>!liveIds.Contains(id));if(_selected is { } selected&&!liveIds.Contains(selected))_selected=null;
        foreach (var child in _canvas.Children.ToArray()) if (!ReferenceEquals(child,_editor)) _canvas.Children.Remove(child);
        _objects.Clear();
        var items = Items().Where(i => i.DeletedAt is null && i.Id != _editing?.Id).OrderBy(i => WorkspaceStore.IsVisionShape(i.Kind) ? 0 : 1).ThenBy(i => i.CreatedAt).ToArray();
        foreach (var item in items)
        {
            var control = MakeObject(item); _objects[item.Id] = control;
            _canvas.Children.Add(control);
        }
        if (_editor is not null && !_canvas.Children.Contains(_editor)) _canvas.Children.Add(_editor);
        _canvas.Height = Math.Max(1600, items.Select(i => i.Top + i.Height + 160).DefaultIfEmpty(0).Max());
        UpdateExtent();
        RefreshConnections();
        RefreshTools();
        RefreshLifecycle();
    }
    private void UpdateExtent()
    {
        var items=Items().Where(i=>i.DeletedAt is null).ToArray();
        _canvas.Width=Math.Max(Math.Max(1,(_scroll.Bounds.Width-16)/_zoom),items.Select(i=>i.Left+i.Width+160).DefaultIfEmpty(0).Max());
        _canvas.Height=Math.Max(Math.Max(1600,(_scroll.Bounds.Height-16)/_zoom),items.Select(i=>i.Top+i.Height+160).DefaultIfEmpty(0).Max());
    }
    private Border MakeObject(VisionItem item)
    {
        Control content;
        if (item.Kind == "text") content = new TextBlock { Text = item.Text, FontFamily = new FontFamily("Georgia"), FontSize = item.FontSize,
            Foreground = CanvasInk, TextWrapping = TextWrapping.Wrap };
        else if(item.Kind=="table")content=MakeTable(item);
        else if (WorkspaceStore.IsVisionShape(item.Kind)) content = new VisionPrimitive(item.Kind,CanvasInk);
        else
        {
            try
            {
                if (!_images.TryGetValue(item.FileName!, out var bitmap))
                { bitmap = new Bitmap(_session!.Store.VisionAssetPath(AssetOwner(item),item.FileName!)); _images.Add(item.FileName!,bitmap); }
                content = new Image { Source = bitmap, Stretch = Stretch.Uniform };
            }
            catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
            { content = new TextBlock { Text = "image unavailable", Foreground = Brush.Parse("#BEA8B9") }; }
        }
        if(item.Kind=="text"&&item.LinkedMicheId is { } linked)content=LinkedNote(linked,content);
        else if(item.Kind=="text"&&item.RoundedFrame)content=new Border {Padding=new Thickness(8,5),CornerRadius=new CornerRadius(12),Background=Brush.Parse(CalendarMode?"#EDE5D6":"#392B40"),BorderBrush=CanvasMuted,BorderThickness=new Thickness(1),Child=content};
        if(item.Kind=="text"){content=NotePresentation.Decorate(content,item.NoteShape,CanvasInk);if(item.NoteShape is not null)content.HorizontalAlignment=HorizontalAlignment.Stretch;}
        var layer = new Grid(); layer.Children.Add(content);
        var border = new Border { Background = Brushes.Transparent, BorderBrush = Brush.Parse("#DFA6AD"),
            BorderThickness = new Thickness(IsSelected(item.Id) ? 1 : 0), Child = layer };
        Avalonia.Automation.AutomationProperties.SetName(border,WorkspaceStore.VisionObjectName(item));
        Position(border,item);
        border.ContextMenu = new ContextMenu { Background = Brush.Parse("#2C2033") };
        var remove = new MenuItem { Header = "move to recently deleted" };
        remove.Click += (_, _) => Delete(item.Id); border.ContextMenu.Items.Add(remove);
        if (item.Kind == "text") { var edit = new MenuItem { Header = "edit text" }; edit.Click += (_, _) => BeginText(new Point(item.Left,item.Top),item); border.ContextMenu.Items.Add(edit); }
        var plan=new MenuItem{Header=CalendarMode?"move to another day…":"plan on calendar…"};plan.Click+=(_,_)=>ShowPlanItem(item.Id);border.ContextMenu.Items.Add(plan);
        border.PointerPressed += (_, e) => {
            if(e.Source is Control button&&(button is Button||button.GetVisualAncestors().Any(v=>v is Button)))return;
            if(item.Kind=="table"&&e.Source is Control cell&&cell.GetVisualAncestors().Any(v=>v is TableView))return;
            if(_dialogBorder.IsVisible) {e.Handled=true;return;}
            if (!e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed) return;
            if (_editor is not null && !CommitDraft()) { e.Handled = true; return; }
            SelectObject(item.Id,e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) {RefreshBoard();e.Handled=true;return;}
            if (e.ClickCount > 1 && item.Kind == "text") { BeginText(new Point(item.Left,item.Top),item); e.Handled = true; return; }
            BeginGesture(item,e,resize:false); UpdateSelection(); e.Handled = true;
        };
        if (item.Id == _selected && SelectedIds.Count==1)
        {
            var grip = new Border { Name = "VisionResizeGrip", Width = 16, Height = 16, Background = Brush.Parse("#DFA6AD"),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = new Cursor(StandardCursorType.BottomRightCorner) };
            layer.Children.Add(grip);
            grip.PointerPressed += (_,e) => { if (e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed) { BeginGesture(item,e,resize:true); e.Handled = true; } };
        }
        AddConnectionMenu(item,layer,border);
        border.AddHandler(PointerPressedEvent,(_,e)=>{if(_connectingFrom is not null&&e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed&&TryConnectionTarget(item.Id))e.Handled=true;},RoutingStrategies.Tunnel);
        return border;
    }
    private static void Position(Control control, VisionItem item)
    {
        Canvas.SetLeft(control,item.Left); Canvas.SetTop(control,item.Top); control.Width = item.Width; control.Height = item.Height;
        control.RenderTransform = new RotateTransform(item.Rotation); control.RenderTransformOrigin = RelativePoint.Center;
        if(control is Border b&&b.Child is Grid grid)
        {var first=grid.Children.FirstOrDefault();var label=first is Border frame?frame.Child:first;if(label is TextBlock text)text.FontSize=item.FontSize;}
    }
    private void UpdateSelection()
    {
        foreach (var (id,border) in _objects) border.BorderThickness = new Thickness(IsSelected(id) ? 1 : 0);
        RefreshTools();
    }
    private void RefreshTools()
    {
        _tools.Children.Clear();
        if(_selectedConnection is { } edge){AddButton(_tools,"remove connection",()=>DisconnectObjects(edge.Source,edge.Target));return;}
        if(SelectedIds.Count>1){_tools.Children.Add(new TextBlock {Text=$"{SelectedIds.Count} selected",FontSize=11,VerticalAlignment=VerticalAlignment.Center});return;}
        if (_selected is not { } id || Items().SingleOrDefault(i => i.Id == id && i.DeletedAt is null) is not { } item) return;
        if(CalendarMode){if(item.Kind=="text")AddButton(_tools,"edit",()=>BeginText(new Point(item.Left,item.Top),item));AddButton(_tools,"day…",()=>ShowPlanItem(id));AddButton(_tools,"delete",()=>Delete(id));return;}
        AddButton(_tools,"connect",()=>BeginConnection(id));
        if (item.Kind == "text") {AddButton(_tools,"edit", () => BeginText(new Point(item.Left,item.Top),item));AddButton(_tools,item.RoundedFrame?"unbox":"round box",()=>ToggleTextFrame(id));}
        AddButton(_tools,CalendarMode?"day…":"calendar…",()=>ShowPlanItem(id));
        AddButton(_tools,"↶ 5°", () => Rotate(id,-5)); AddButton(_tools,"↷ 5°", () => Rotate(id,5));
        AddButton(_tools,"delete", () => Delete(id));
    }
    private void BlankPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!ReferenceEquals(e.Source,_canvas)) return;
        if(_dialogBorder.IsVisible) {e.Handled=true;return;}
        if(_connectingFrom is not null){CancelConnection();e.Handled=true;return;}
        if (_editor is not null && !CommitDraft()) { e.Handled = true; return; }
        if(SelectConnectionAt(e))return;
        var point = e.GetPosition(_canvas);
        if (e.GetCurrentPoint(_canvas).Properties.IsRightButtonPressed)
        {
            var menu = new ContextMenu { Background = Brush.Parse("#2C2033") };
            var text = new MenuItem { Header = "write here" }; text.Click += (_,_) => BeginText(point);
            var image = new MenuItem { Header = "image here" }; image.Click += async (_,_) => await PickImage(point);
            var table=new MenuItem{Header="table here"};table.Click+=(_,_)=>AddTable(point);menu.Items.Add(table);menu.Items.Add(text); menu.Items.Add(image); AddShapeMenuItems(menu,point); _canvas.ContextMenu = menu; menu.Open(_canvas); e.Handled = true;
        }
        else if (e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed) { BeginMarquee(e); e.Handled = true; }
    }
    public void BeginText(Point point, VisionItem? existing = null)
    {
        if (_editor is not null && !CommitDraft()) return;
        CancelGesture(); _selected = null; _selection.Clear();
        _editing = existing is null ? new VisionItem { Left = Math.Clamp(point.X,0,Math.Max(0,_canvas.Width-280)), Top = Math.Clamp(point.Y,0,10000) }
            : WorkspaceStore.CopyVision(existing);
        _editor = new TextBox { Text = _editing.Text, FontFamily = new FontFamily("Georgia"), FontSize = _editing.FontSize,
            Foreground = CanvasInk, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Padding = new Thickness(0), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 20000,
            Width = _editing.Width, MinHeight = Math.Max(60,_editing.Height), MaxHeight = 4000 };
        Canvas.SetLeft(_editor,_editing.Left); Canvas.SetTop(_editor,_editing.Top);
        _editor.TextChanged+=(_,_)=>{FitTextDraft();UpdateMicheMentions();};
        StyleLinkedEditor();
        FitTextDraft();
        _editor.LostFocus += (_,_) => { if (!_committing && !_mentionPicker.IsOpen && _editor is not null) CommitDraft(); };
        RefreshBoard(); Dispatcher.UIThread.Post(() => { _editor?.Focus(); if (_editor is not null) _editor.CaretIndex = _editor.Text?.Length ?? 0; });
    }
    public bool ToggleTextFrame(Guid id)
    {
        if(!PrepareToLeave())return false;
        var item=Items().SingleOrDefault(i=>i.Id==id&&i.Kind=="text"&&i.DeletedAt is null);if(item is null)return false;
        item=WorkspaceStore.CopyVision(item);item.RoundedFrame=!item.RoundedFrame;
        NotePresentation.Fit(item);
        return SaveCanvasItems(new[]{item});
    }
    private void FitTextDraft()
    {
        if(_editor is null||_editing is null)return;
        var measure=new TextBlock {Text=_editor.Text??"",FontFamily=new FontFamily("Georgia"),FontSize=_editing.FontSize,TextWrapping=TextWrapping.Wrap};
        measure.Measure(new Size(Math.Max(280,_editing.Width),double.PositiveInfinity));
        _editor.Width=string.IsNullOrEmpty(_editor.Text)?140:Math.Clamp(measure.DesiredSize.Width+4,8,4000);
        _editor.MinHeight=Math.Clamp(measure.DesiredSize.Height+2,24,4000);
    }
    public bool CommitDraft(bool allowEmptyMention = false)
    {
        if (_editor is null || _editing is null || _committing) return true;
        if(TryResolveMicheMention()&&string.IsNullOrWhiteSpace(_editor?.Text)){if(!allowEmptyMention)CancelDraft();return true;}
        var text = (_editor?.Text ?? "").Trim();
        if (text.Length == 0) { CancelDraft(); return true; }
        var item = WorkspaceStore.CopyVision(_editing); item.Text = text;
        NotePresentation.Fit(item);
        _committing = true;
        try
        {
            if (!SaveCanvasItems(new[]{item})) return false;
            _message.Text = "";
            _editor = null; _editing = null; _selection.Clear(); _selected = item.Id; RefreshBoard(); _canvas.Focus(); return true;
        }
        finally { _committing = false; }
    }
    public void CancelDraft() { _mentionPicker.IsOpen=false; _editor = null; _editing = null; RefreshBoard(); _canvas.Focus(); }
    internal void ClearSaveError()=>_message.Text="";
    public bool PrepareToLeave() { CancelConnection(); CancelGesture(); return CommitDraft(); }
    public void Rotate(Guid id, double degrees)
    {
        if (!CommitDraft()) return;
        var item = Items().Single(i => i.Id == id); item.Rotation = (item.Rotation + degrees + 540) % 360 - 180;
        SaveCanvasItems(new[]{item});
    }
    public void Delete(Guid id)
    {
        if (!CommitDraft()) return;
        _session!.Act(() => {if(CalendarMode)_session.Store.SetCalendarDeleted(id,true);else _session.Store.SetVisionDeleted(_micheId,id,true,_artifactId);}, "Object moved to recently deleted.");
        _selected = null; _selection.Clear(); RefreshBoard();
    }
    public bool ImportImage(string source, Point point)
    {
        if (!CommitDraft()) return false;
        var saved = _session!.Act(() => { var id = VisionImages.Import(_session.Store,_micheId,source,point.X,point.Y,_artifactId,_calendarMonth,_calendarDate); _selection.Clear(); _selected = id; RefreshBoard(); }, "Image added.");
        if (saved) _message.Text = "";
        return saved;
    }
    private async System.Threading.Tasks.Task PickImage(Point point)
    {
        if (!CommitDraft()) return;
        var micheId = _micheId;
        var artifactId=_artifactId;
        var calendarMonth=_calendarMonth;var calendarDate=_calendarDate;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Add an image to Vision", AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("PNG and JPEG") {
                // Use native content types on Mac; globs are for other storage providers.
                Patterns = OperatingSystem.IsMacOS() ? null : new[] { "*.png", "*.jpg", "*.jpeg" },
                AppleUniformTypeIdentifiers = new[] { "public.png", "public.jpeg" },
                MimeTypes = new[] { "image/png", "image/jpeg" }
            } } });
        if (_micheId != micheId || _artifactId!=artifactId || _calendarMonth!=calendarMonth || _calendarDate!=calendarDate) { _message.Text = "Return to that page to add the image."; return; }
        foreach (var file in files) { var path = file.TryGetLocalPath(); if (path is not null) ImportImage(path,point); }
    }
    private void ShowRecovery()
    {
        if (!CommitDraft()) return;
        var menu = new ContextMenu { Background = Brush.Parse("#2C2033") };
        foreach (var item in Items().Where(i => i.DeletedAt is not null))
        {
            var restore = new MenuItem { Header = "restore · " + WorkspaceStore.VisionObjectName(item) };
            restore.Click += (_,_) => _session!.Act(() => {if(CalendarMode)_session.Store.SetCalendarDeleted(item.Id,false);else _session.Store.SetVisionDeleted(_micheId,item.Id,false,_artifactId);}); menu.Items.Add(restore);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "nothing deleted", IsEnabled = false });
        menu.Open(this);
    }
    private void BeginGesture(VisionItem item, PointerPressedEventArgs e, bool resize)
    {
        if(!resize && SelectedIds.Count>1) {BeginGroupGesture(e);return;}
        CancelGesture(); _gesture = new CanvasGesture(WorkspaceStore.CopyVision(item),e.GetPosition(_canvas),e.Pointer,resize);
        _canvas.Focus(); e.Pointer.Capture(_canvas);
    }
    private void MovePointer(object? sender, PointerEventArgs e)
    {
        if(MoveConnectionPointer(e))return;
        if(MoveSelectionPointer(e))return;
        if (_gesture is not { } g) return;
        var delta = e.GetPosition(_canvas) - g.Start;
        if (!g.Started && Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4) return;
        g.Started = true; var item = WorkspaceStore.CopyVision(g.Before);
        if (g.Resize)
        {
            var angle = item.Rotation * Math.PI / 180;
            var dx = delta.X * Math.Cos(angle) + delta.Y * Math.Sin(angle);
            var dy = -delta.X * Math.Sin(angle) + delta.Y * Math.Cos(angle);
            if (WorkspaceStore.IsVisionShape(item.Kind))
            {
                var availableWidth=Math.Max(0,_canvas.Width-item.Left);
                if (item.Kind=="horizontal-line") { if(availableWidth<24)return;item.Width=Math.Clamp(item.Width+dx,24,Math.Min(4000,availableWidth)); }
                else if (item.Kind=="vertical-line") item.Height=Math.Clamp(item.Height+dy,24,4000);
                else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    var min=Math.Max(24/item.Width,24/item.Height);
                    var max=Math.Min(Math.Min(4000/item.Width,4000/item.Height),availableWidth/item.Width);
                    if(max<min)return;
                    var factor=Math.Clamp(1+(Math.Abs(dx/item.Width)>Math.Abs(dy/item.Height)?dx/item.Width:dy/item.Height),min,max);
                    item.Width*=factor;item.Height*=factor;
                }
                else { if(availableWidth<24)return;item.Width=Math.Clamp(item.Width+dx,24,Math.Min(4000,availableWidth));item.Height=Math.Clamp(item.Height+dy,24,4000); }
            }
            else
            {
            var factor = 1 + (Math.Abs(dx / item.Width) > Math.Abs(dy / item.Height) ? dx / item.Width : dy / item.Height);
            var min = item.Kind == "text" ? Math.Max(60 / item.Width, 8 / item.FontSize) : Math.Max(60 / item.Width,60 / item.Height);
            var max = Math.Min(Math.Min(4000 / item.Width,4000 / item.Height), Math.Max(0,_canvas.Width-item.Left) / item.Width);
            if (item.Kind == "text") max = Math.Min(max,200 / item.FontSize);
            if (max < min) { e.Handled = true; return; }
            factor = Math.Clamp(factor, min, max);
            item.Width *= factor; item.Height *= factor;
            if(item.Kind=="table")ScaleTable(item,factor);
            if (item.Kind == "text") item.FontSize *= factor;
            }
        }
        else { item.Left = Math.Clamp(item.Left + delta.X,0,Math.Max(0,_canvas.Width-item.Width)); item.Top = Math.Clamp(item.Top + delta.Y,0,10000); }
        g.Preview = item; Position(_objects[item.Id],item);
        RefreshConnections();
        _canvas.Height = Math.Max(_canvas.Height,item.Top + item.Height + 160); e.Handled = true;
    }
    private void ReleasePointer(object? sender, PointerReleasedEventArgs e)
    {
        if(ReleaseConnectionPointer(e))return;
        if(ReleaseSelectionPointer(e))return;
        if (_gesture is not { } g) return;
        MovePointer(sender,e); // A fast drag may deliver only the final release position.
        _gesture = null; g.Pointer.Capture(null);
        if(g.Started&&!g.Resize&&WorkspaceStore.IsVisionShape(g.Preview.Kind)&&TryAttachShape(g.Preview,e.GetPosition(_canvas))) {RefreshBoard();e.Handled=true;return;}
        if(!g.Started&&!g.Resize&&CalendarMode&&g.Before.Kind=="text"){BeginText(new Point(g.Before.Left,g.Before.Top),g.Before);e.Handled=true;return;}
        if (g.Started && !(CalendarMode&&!g.Resize&&CalendarDropRequested?.Invoke(new[]{g.Before.Id},e)==true)) SaveCanvasItems(new[]{g.Preview});
        RefreshBoard(); e.Handled = true;
    }
    public bool CancelGesture()
    {
        if(CancelSelectionGesture())return true;
        if (_gesture is not { } g) return false;
        _gesture = null; g.Pointer.Capture(null); RefreshBoard(); return true;
    }
    public bool Escape()
    {
        if(CancelConnection())return true;
        if (_editor is not null) { CancelDraft(); return true; }
        if (CancelGesture()) return true;
        if (_selected is not null) { _selected = null; _selection.Clear(); RefreshBoard(); return true; }
        return false;
    }
    private void CanvasKeyDown(object? sender, KeyEventArgs e)
    {
        if(_dialogBorder.IsVisible)
        { if(e.Key==Key.Escape) {CloseDialog(); _canvas.Focus(); e.Handled=true;} return; }
        if(_mentionPicker.IsOpen&&e.Key==Key.Escape){_mentionPicker.IsOpen=false;e.Handled=true;return;}
        if(e.Source is Control tableCell&&tableCell.GetVisualAncestors().Any(v=>v is TableView)){if(e.Key==Key.Escape)_canvas.Focus();return;}
        HandleVisionClipboardKey(e);if(e.Handled)return;
        if (HandleSelectionSizeKey(e)||HandleZoomKey(e)) return;
        if(_editor is null&&e.Key is Key.Delete or Key.Back&&_selectedConnection is { } edge){DisconnectObjects(edge.Source,edge.Target);e.Handled=true;return;}
        if (e.Key == Key.Escape) { if (!Escape()) CloseRequested?.Invoke(); e.Handled = true; }
        else if (_editor is not null && e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { CommitDraft(true); e.Handled = true; }
        else if (_editor is null && e.Key is Key.Delete or Key.Back && _selected is { } id) { Delete(id); e.Handled = true; }
    }
    public void Dispose()
    {
        CancelGesture();
        if (_session is not null) { _session.Changed -= DataChanged; _session.ErrorOccurred -= Error; }
        foreach (var bitmap in _images.Values) bitmap.Dispose(); _images.Clear();
    }
}
