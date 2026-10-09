using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Miche.Mac.Models;
using Miche.Mac.Services;
using CalendarItem = Miche.Mac.Models.CalendarItem;

namespace Miche.Mac.Controls;

public sealed partial class CalendarView
{
    private readonly Dictionary<string,StackPanel> _noteHosts=new();
    private readonly Dictionary<Guid,Control> _previews=new();
    private readonly Popup _inlineMentions=new(){Placement=PlacementMode.Bottom,IsLightDismissEnabled=true};
    private TextBox? _inlineEditor;
    private VisionItem? _inlineItem;
    private string? _inlineDate;
    private bool _savingInline, _inlineExisting;
    public TextBox? InlineEditor=>_inlineEditor;
    public IReadOnlyDictionary<Guid,Control> NotePreviews=>_previews;
    private void InitializeInlineWriting()=>_root.Children.Add(_inlineMentions);
    public bool BeginInline(string date,CalendarItem? existing=null)
    {
        if(_inlineEditor is not null&&_inlineDate==date&&existing is null&&!_inlineExisting)
        {_inlineEditor.Focus();_inlineEditor.CaretIndex=_inlineEditor.Text?.Length??0;return true;}
        if(!ShowDate(date)||!_noteHosts.TryGetValue(date,out var host))return false;
        var notes=_session.Store.Snapshot.Calendar.Items.Where(i=>i.Date==date&&i.Content.DeletedAt is null).ToArray();
        _inlineDate=date;_inlineExisting=existing is not null;
        _inlineItem=existing is null?new VisionItem{Left=24,Top=Math.Min(10000,notes.Select(i=>i.Content.Top+i.Content.Height+14).DefaultIfEmpty(24).Max()),FontSize=18}:WorkspaceStore.CopyVision(existing.Content);
        var editor=_inlineEditor=new TextBox {Text=_inlineItem.Text,FontFamily=new FontFamily("Georgia"),FontSize=_view=="month"?12:16,
            Foreground=Brush.Parse("#453E38"),Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(3),
            MinHeight=32,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MaxLength=20000,Watermark="keep writing…"};
        AutomationProperties.SetName(editor,"Write on calendar day "+date);
        var index=host.Children.Count;
        if(existing is not null&&_previews.TryGetValue(existing.Id,out var preview)){index=host.Children.IndexOf(preview);host.Children.Remove(preview);}
        host.Children.Insert(Math.Max(0,index),editor);StyleInlineMention();
        editor.TextChanged+=(_,_)=>UpdateInlineMentions();
        editor.LostFocus+=(_,_)=>{if(!_savingInline&&!_inlineMentions.IsOpen&&ReferenceEquals(editor,_inlineEditor))CommitInline();};
        editor.AddHandler(KeyDownEvent,(_,e)=>{
            if(e.Key==Key.Escape){ClearInline();Refresh();e.Handled=true;}
            else if(e.Key==Key.Enter&&!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {if(CommitInline(true)&&_inlineEditor is null)BeginInline(date);e.Handled=true;}
        },RoutingStrategies.Tunnel);
        _status.Text="write right on the day · enter for another note · shift-enter for a new line";
        Dispatcher.UIThread.Post(()=>{if(ReferenceEquals(editor,_inlineEditor)){editor.Focus();editor.CaretIndex=editor.Text?.Length??0;editor.BringIntoView();}});
        return true;
    }
    public bool CommitInline(bool allowEmptyMention=false)
    {
        if(_inlineEditor is null||_inlineItem is null||_inlineDate is null||_savingInline)return true;
        ResolveInlineMention();
        var text=(_inlineEditor.Text??"").Trim();
        if(text.Length==0)
        {if(allowEmptyMention&&_inlineItem.LinkedMicheId is not null)return true;ClearInline();Refresh();return true;}
        var state=_session.Store.Snapshot;
        var saved=state.Calendar.Items.SingleOrDefault(i=>i.Id==_inlineItem.Id);
        if(saved is not null&&(saved.Date!=_inlineDate||saved.Content.DeletedAt is not null))
        {_status.Text="This note moved. Your writing is still here; copy it before leaving.";return false;}
        var item=WorkspaceStore.CopyVision(saved?.Content??_inlineItem);item.Text=text;item.LinkedMicheId=_inlineItem.LinkedMicheId;
        if(item.LinkedMicheId is not null)item.RoundedFrame=true;
        NotePresentation.Fit(item);
        _savingInline=true;
        try
        {
            if(!_session.Act(()=>_session.Store.SaveCalendarItems(_inlineDate[..7],_inlineDate,new[]{item})))return false;
            Editor.ClearSaveError();ClearInline();Refresh();return true;
        }
        finally{_savingInline=false;}
    }
    private void ClearInline()
    {
        var editor=_inlineEditor;_inlineEditor=null;_inlineItem=null;_inlineDate=null;_inlineExisting=false;_inlineMentions.IsOpen=false;
        if(editor?.Parent is Panel panel)panel.Children.Remove(editor);
    }
    private void StyleInlineMention()
    {
        if(_inlineEditor is null||_inlineItem?.LinkedMicheId is not { } id)return;
        var miche=_session.Store.Snapshot.Index.Miches.Concat(_session.Store.Snapshot.Trash.Select(t=>t.Miche)).Single(m=>m.Id==id);
        _inlineEditor.Background=Brush.Parse(WorkspaceStore.MicheNoteColor(miche));_inlineEditor.CornerRadius=new CornerRadius(8);
        _inlineEditor.Padding=new Thickness(6);_inlineEditor.Watermark="a note for "+miche.Name;
    }
    private void ChooseInlineMiche(Guid id,string body)
    {
        if(_inlineItem is null||_inlineEditor is null)return;
        _inlineMentions.IsOpen=false;_inlineItem.LinkedMicheId=id;_inlineEditor.Text=body;StyleInlineMention();
        Dispatcher.UIThread.Post(()=>{_inlineEditor?.Focus();if(_inlineEditor is not null)_inlineEditor.CaretIndex=_inlineEditor.Text?.Length??0;});
    }
    private void ResolveInlineMention()
    {
        if(_inlineItem?.LinkedMicheId is not null||_inlineEditor is null)return;
        var text=(_inlineEditor.Text??"").TrimStart();if(!text.StartsWith("/@"))return;
        var query=VisionView.MentionKey(text[2..].Split(new[]{"--","\n"},StringSplitOptions.None)[0]);
        var miche=_session.Store.Snapshot.Index.Miches.FirstOrDefault(m=>VisionView.MentionKey(m.Name).Equals(query,StringComparison.OrdinalIgnoreCase));
        if(miche is not null)ChooseInlineMiche(miche.Id,VisionView.MentionBody(text,miche.Name));
    }
    private void UpdateInlineMentions()
    {
        if(_inlineEditor is null||_inlineItem?.LinkedMicheId is not null){_inlineMentions.IsOpen=false;return;}
        var text=(_inlineEditor.Text??"").TrimStart();if(!text.StartsWith("/@")){_inlineMentions.IsOpen=false;return;}
        var query=VisionView.MentionKey(text[2..].Split(new[]{"--","\n"},StringSplitOptions.None)[0]);
        var choices=new StackPanel{Spacing=4};
        foreach(var miche in _session.Store.Snapshot.Index.Miches.Where(m=>VisionView.MentionKey(m.Name).Contains(query,StringComparison.OrdinalIgnoreCase)))
        {
            var button=new Button {Content="↗ "+miche.Name,Background=Brush.Parse(WorkspaceStore.MicheNoteColor(miche)),Foreground=Brush.Parse("#393631"),CornerRadius=new CornerRadius(10),Padding=new Thickness(10,6),HorizontalAlignment=HorizontalAlignment.Stretch};
            button.Click+=(_,_)=>ChooseInlineMiche(miche.Id,VisionView.MentionBody(text,miche.Name));choices.Children.Add(button);
        }
        if(choices.Children.Count==0){_inlineMentions.IsOpen=false;return;}
        _inlineMentions.PlacementTarget=_inlineEditor;
        _inlineMentions.Child=new Border{Background=Brush.Parse("#F7F1E5"),CornerRadius=new CornerRadius(12),Padding=new Thickness(8),Width=260,MaxHeight=260,Child=new ScrollViewer{Content=choices}};
        _inlineMentions.IsOpen=true;
    }
}
