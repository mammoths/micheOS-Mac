using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Miche.Mac.Models;
using Miche.Mac.Services;
using System.Globalization;
using System.Text;

namespace Miche.Mac.Controls;

public sealed partial class VisionView
{
    private string? _calendarMonth, _calendarDate;
    public bool CalendarMode => _calendarMonth is not null;
    public Func<Guid[], PointerReleasedEventArgs, bool>? CalendarDropRequested { get; set; }
    private readonly Popup _mentionPicker = new() { Placement = PlacementMode.Bottom, IsLightDismissEnabled = true };
    private IBrush CanvasInk => Brush.Parse(CalendarMode ? "#453E38" : "#F4E5D1");
    private IBrush CanvasMuted => Brush.Parse(CalendarMode ? "#887C6F" : "#BEA8B9");
    private void InitializeMicheMentions() => _root.Children.Add(_mentionPicker);
    public bool SetCalendarTarget(string month, string? date = null)
    {
        if (!WorkspaceStore.CalendarMonthValid(month) || date is not null && (!WorkspaceStore.CalendarDateValid(date) || !date.StartsWith(month + "-"))) return false;
        if (!PrepareToLeave()) return false;
        var first = !CalendarMode;
        _calendarMonth = month; _calendarDate = date; _artifactId = null;
        if (_session is not null) _micheId = _session.Store.Snapshot.Index.RootMicheId;
        _selected = null; _selection.Clear(); _scroll.Offset = default;
        if (first)
        {
            _root.RowDefinitions=new RowDefinitions("Auto,*,Auto,Auto");
            foreach(var footer in _root.Children.OfType<StackPanel>().Where(p=>p.HorizontalAlignment==HorizontalAlignment.Right))Grid.SetRow(footer,3);
            Grid.SetRowSpan(_dialogBorder,4);
            Background = Brush.Parse("#F7F1E5"); Foreground=CanvasInk; _caption.Foreground = CanvasMuted; _message.Foreground = CanvasMuted;
            _dialogBorder.Background=Brush.Parse("#F7F1E5");_dialogBorder.BorderBrush=Brush.Parse("#D6CAB8");
            CalendarPaper.Style(this);
            _bar.Children.Clear(); _bar.Children.Add(_caption);
            AddButton(_bar, "write +", () => BeginText(VisibleInsertionPoint));
            AddButton(_bar, "photo +", async () => await PickImage(VisibleInsertionPoint));
            _shapeButton = AddButton(_bar, "shape +", () => ShowShapes(VisibleInsertionPoint));
            AddButton(_bar, "restore", ShowRecovery); _bar.Children.Add(_message);
            _zoomHost.Child = null;
            var paper = new Grid(); paper.Children.Add(new CalendarPaper()); paper.Children.Add(_canvas); _zoomHost.Child = paper;
        }
        RefreshBoard(); return true;
    }
    private bool SaveCanvasItems(IEnumerable<VisionItem> items) => _session!.Act(() => {
        if (CalendarMode) _session.Store.SaveCalendarItems(_calendarMonth!, _calendarDate, items);
        else _session.Store.SaveVisionItems(_micheId, items, _artifactId);
    });
    private Guid AssetOwner(VisionItem item) => CalendarMode ? _session!.Store.Snapshot.Calendar.Items.Single(i => i.Id == item.Id).OwnerMicheId : _micheId;
    private Models.Miche? LinkedMiche(Guid id)
    {
        var state = _session!.Store.Snapshot;
        return state.Index.Miches.Concat(state.Trash.Select(t => t.Miche)).SingleOrDefault(m => m.Id == id);
    }
    private Control LinkedNote(Guid id, Control body)
    {
        var miche = LinkedMiche(id);
        var panel = new StackPanel { Spacing = 5 };
        var label = new Button { Content = "↗ " + (miche?.Name ?? "Miche"), FontSize = 11, Foreground = Brush.Parse("#4B4742"), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
        label.Click += (_, e) => { _session!.OpenLinkedMiche(id); e.Handled = true; };
        // A link click should never start a canvas drag.
        panel.Children.Add(label);
        if (body is TextBlock text) text.Foreground = Brush.Parse("#393631");
        panel.Children.Add(body);
        return new Border { Background = Brush.Parse(miche is null ? "#DABBB6" : WorkspaceStore.MicheNoteColor(miche)), CornerRadius = new CornerRadius(14), Padding = new Thickness(12,8), Child = panel };
    }
    private void StyleLinkedEditor()
    {
        if (_editor is null || _editing is null) return;
        if (_editing.LinkedMicheId is { } id)
        {
            var miche = LinkedMiche(id);
            _editor.Foreground = Brush.Parse("#393631"); _editor.Background = Brush.Parse(miche is null ? "#DABBB6" : WorkspaceStore.MicheNoteColor(miche));
            _editor.Padding = new Thickness(10,6); _editor.MinWidth = 220;
            _editor.Watermark = "a note for " + miche?.Name;
        }
        else _editor.Watermark = CalendarMode ? "a thought, a plan, or /@Miche" : "";
    }
    private void UpdateMicheMentions()
    {
        if (_session is null || _editor is null || _editing is null || _editing.LinkedMicheId is not null) { _mentionPicker.IsOpen = false; return; }
        var text = (_editor.Text ?? "").TrimStart();
        if (!text.StartsWith("/@", StringComparison.Ordinal)) { _mentionPicker.IsOpen = false; return; }
        var query = MentionKey(text[2..].Split(new[] { "--", "\n" }, StringSplitOptions.None)[0]);
        var choices = new StackPanel { Spacing = 4 };
        foreach (var miche in _session.Store.Snapshot.Index.Miches.Where(m => MentionKey(m.Name).Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            var button = new Button { Content = "↗ " + miche.Name, Background = Brush.Parse(WorkspaceStore.MicheNoteColor(miche)), Foreground = Brush.Parse("#393631"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12,8), HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Click += (_, _) => ChooseMiche(miche.Id, MentionBody(text, miche.Name)); choices.Children.Add(button);
        }
        if (choices.Children.Count == 0) choices.Children.Add(new TextBlock { Text = "no matching Miche", Foreground = Brush.Parse("#887C6F"), Margin = new Thickness(8) });
        _mentionPicker.PlacementTarget = _editor;
        _mentionPicker.Child = new Border { Background = Brush.Parse("#F7F1E5"), CornerRadius = new CornerRadius(14), Padding = new Thickness(8), Width = 280, MaxHeight = 300, Child = new ScrollViewer { Content = choices } };
        _mentionPicker.IsOpen = true;
    }
    internal static string MentionBody(string text, string name)
    {
        var tail = text.TrimStart()[2..];
        var delimiter=tail.IndexOf("--",StringComparison.Ordinal);if(delimiter>=0)return tail[(delimiter+2)..].Trim();
        delimiter=tail.IndexOf('\n');if(delimiter>=0)return tail[(delimiter+1)..].Trim();
        return "";
    }
    internal static string MentionKey(string text)=>new string(text.Trim().TrimStart('@').Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark).ToArray()).Normalize(NormalizationForm.FormC);
    private void ChooseMiche(Guid id, string body)
    {
        if (_editing is null || _editor is null) return;
        _mentionPicker.IsOpen = false; _editing.LinkedMicheId = id; _editing.RoundedFrame = true;
        _editor.Text = body; StyleLinkedEditor();
        Dispatcher.UIThread.Post(() => { _editor?.Focus(); if (_editor is not null) _editor.CaretIndex = _editor.Text?.Length ?? 0; });
    }
    private bool TryResolveMicheMention()
    {
        if (_editing is null || _editor is null || _editing.LinkedMicheId is not null || _session is null) return false;
        var text = (_editor.Text ?? "").TrimStart(); if (!text.StartsWith("/@")) return false;
        var query = MentionKey(text[2..].Split(new[]{"--","\n"},StringSplitOptions.None)[0]);
        var miche = _session.Store.Snapshot.Index.Miches.FirstOrDefault(m => MentionKey(m.Name).Equals(query,StringComparison.OrdinalIgnoreCase));
        if (miche is null) return false;
        ChooseMiche(miche.Id, MentionBody(text, miche.Name)); return true;
    }
    public void ShowPlanItem(Guid id)
    {
        if (!PrepareToLeave()) return;
        Dialog("a place for this thought");
        var input = new TextBox { Text = _calendarDate ?? DateTime.Today.ToString("yyyy-MM-dd"), Watermark = "yyyy-mm-dd", MaxLength = 10 };
        _dialogContents.Children.Add(input);
        bool Plan(string? day)
        {
            var month = day is null ? _calendarMonth ?? DateTime.Today.ToString("yyyy-MM") : WorkspaceStore.CalendarDateValid(day) ? day[..7] : "";
            var ok = _session!.Act(() => {
                if (CalendarMode) _session.Store.ScheduleCalendarItem(id, day, month);
                else _session.Store.PlanVisionItem(_micheId, id, day, month, _artifactId);
            }, day is null ? "Saved in the month margin." : "Planned for " + day + ".");
            if (ok) { CloseDialog(); RefreshBoard(); } return ok;
        }
        AddButton(_dialogContents, "put on this day", () => Plan(input.Text));
        AddButton(_dialogContents, "leave in month margin", () => Plan(null));
        AddButton(_dialogContents, "open calendar", () => {
            var item=Items().Single(i=>i.Id==id);var calendarId=CalendarMode?id:item.CalendarItemId;
            if(calendarId is null){if(!Plan(null))return;item=Items().Single(i=>i.Id==id);calendarId=item.CalendarItemId;}
            CloseDialog();_session!.OpenCalendar(_session.Store.Snapshot.Calendar.Items.Single(i=>i.Id==calendarId).Date);
        });
        AddButton(_dialogContents, "cancel", CancelDialog);
    }
}
