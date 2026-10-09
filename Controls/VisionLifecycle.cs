using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Miche.Mac.Models;

namespace Miche.Mac.Controls;

public sealed partial class VisionView
{
    private Guid? _artifactId;
    private readonly TextBlock _caption=new() {Text="VISION",FontFamily=new FontFamily("Menlo"),FontSize=11,
        Foreground=Brush.Parse("#E6C89E"),VerticalAlignment=VerticalAlignment.Center,MaxWidth=260,TextTrimming=TextTrimming.CharacterEllipsis};
    private readonly StackPanel _dialogContents=new() {Spacing=10};
    private readonly Border _dialogBorder=new() {IsVisible=false,Background=Brush.Parse("#2C2033"),BorderBrush=Brush.Parse("#5D435C"),BorderThickness=new Thickness(1),
        CornerRadius=new CornerRadius(12),Padding=new Thickness(18),Margin=new Thickness(16),MaxWidth=460,MaxHeight=420,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
    private Button? _freshButton;
    private Button? _returnButton;
    public Guid? ArtifactTargetId=>_artifactId;
    public Border LifecycleDialog=>_dialogBorder;
    private void InitializeLifecycle(Panel bar)
    {
        _freshButton=new Button {Content="start fresh",FontSize=11,Margin=new Thickness(4,2)};
        _freshButton.Click+=(_,_)=>ShowStartFresh(); bar.Children.Add(_freshButton);
        AddButton(bar,"past visions",ShowPastVisions);
        _returnButton=new Button {Content="return to current",FontSize=11,Margin=new Thickness(4,2),IsVisible=false};
        _returnButton.Click+=(_,_)=>ReturnToCurrent(); bar.Children.Add(_returnButton);
        _dialogBorder.Child=new ScrollViewer {Content=_dialogContents}; Grid.SetRowSpan(_dialogBorder,2); _root.Children.Add(_dialogBorder);
    }
    private void RefreshLifecycle()
    {
        if(CalendarMode){_caption.Text=_calendarDate is null?"MONTH MARGIN":DateOnly.Parse(_calendarDate).ToString("dddd · MMM d");return;}
        var artifact=_artifactId is { } id ? _session?.Store.Snapshot.VisionArtifacts.SingleOrDefault(a=>a.Id==id) : null;
        _caption.Text=artifact is null ? "VISION / current" : "PAST VISION / "+artifact.Title;
        if(_freshButton is not null) _freshButton.IsVisible=_artifactId is null;
        if(_returnButton is not null) _returnButton.IsVisible=_artifactId.HasValue;
    }
    private void Dialog(string title)
    {
        _dialogContents.Children.Clear();
        _dialogContents.Children.Add(new TextBlock {Text=title,FontFamily=new FontFamily("Georgia"),FontSize=24,TextWrapping=TextWrapping.Wrap});
        _dialogBorder.IsVisible=true;
    }
    private void CloseDialog()=>_dialogBorder.IsVisible=false;
    private void CancelDialog() {CloseDialog();_canvas.Focus();}
    public void ShowStartFresh()
    {
        if(_artifactId is not null || !PrepareToLeave()) return;
        Dialog("a fresh canvas");
        _dialogContents.Children.Add(new TextBlock {Text="Keep this board as a past Vision, or clear it. Cleared boards stay recoverable.",TextWrapping=TextWrapping.Wrap});
        AddButton(_dialogContents,"save & start fresh",()=>ShowTitleEntry("a name for this Vision","",title=>SaveAndStartFresh(title)));
        AddButton(_dialogContents,"clear without saving",()=>SaveAndStartFresh(null));
        AddButton(_dialogContents,"cancel",CancelDialog);
    }
    public bool SaveAndStartFresh(string? title)
    {
        if(_artifactId is not null || !PrepareToLeave()) return false;
        if(!_session!.Act(()=>_session.Store.StartFreshVision(_micheId,title),title is null ? "Board cleared. Recover it from past visions." : "Vision saved. Your current canvas is fresh.")) return false;
        _selected=null;_selection.Clear(); CloseDialog(); RefreshBoard(); _canvas.Focus(); return true;
    }
    private void ShowTitleEntry(string caption,string initial,Func<string,bool> submit)
    {
        Dialog(caption);
        var input=new TextBox {Text=initial,MaxLength=80,Name="VisionTitleBox",Watermark="a title"};
        Avalonia.Automation.AutomationProperties.SetName(input,"Name the past Vision"); _dialogContents.Children.Add(input);
        void Confirm() {if(submit(input.Text ?? "")) CloseDialog();}
        input.KeyDown+=(_,e)=> {if(e.Key==Key.Enter) {Confirm();e.Handled=true;}};
        AddButton(_dialogContents,"save",Confirm); AddButton(_dialogContents,"cancel",CancelDialog);
        Dispatcher.UIThread.Post(()=>{input.Focus();input.SelectAll();});
    }
    public bool OpenArtifact(Guid id)
    {
        if(!PrepareToLeave()) return false;
        var artifact=_session!.Store.Snapshot.VisionArtifacts.SingleOrDefault(a=>a.Id==id && a.MicheId==_micheId && a.DeletedAt is null);
        if(artifact is null) return false;
        _artifactId=id;_selected=null;_selection.Clear();_scroll.Offset=default;CloseDialog();RefreshBoard();_canvas.Focus();return true;
    }
    public bool ReturnToCurrent()
    {
        if(!PrepareToLeave()) return false;
        _artifactId=null;_selected=null;_selection.Clear();_scroll.Offset=default;CloseDialog();RefreshBoard();_canvas.Focus();return true;
    }
    public void ShowPastVisions()
    {
        if(!PrepareToLeave()) return;
        Dialog("past visions"); var state=_session!.Store.Snapshot;
        foreach(var artifact in state.VisionArtifacts.Where(a=>a.MicheId==_micheId && a.DeletedAt is null).OrderByDescending(a=>a.CreatedAt))
        {
            var group=new StackPanel {Spacing=4};
            AddButton(group,"open · "+artifact.Title,()=>OpenArtifact(artifact.Id));
            group.Children.Add(new TextBlock {Text=artifact.CreatedAt.ToLocalTime().ToString("MMM d, yyyy · h:mm tt"),FontSize=11,Foreground=Brush.Parse("#BEA8B9")});
            var actions=new WrapPanel();
            AddButton(actions,"rename",()=>ShowTitleEntry("rename this Vision",artifact.Title,title=>_session.Act(()=>_session.Store.RenameVisionArtifact(_micheId,artifact.Id,title))));
            AddButton(actions,"move to recently deleted",()=>{if(_session.Act(()=>_session.Store.SetVisionArtifactDeleted(_micheId,artifact.Id,true))) ShowPastVisions();});
            group.Children.Add(actions);_dialogContents.Children.Add(group);
        }
        if(!state.VisionArtifacts.Any(a=>a.MicheId==_micheId && a.DeletedAt is null)) _dialogContents.Children.Add(new TextBlock {Text="no saved visions yet"});
        foreach(var artifact in state.VisionArtifacts.Where(a=>a.MicheId==_micheId && a.DeletedAt is not null))
            AddButton(_dialogContents,"restore · "+artifact.Title,()=>{if(_session.Act(()=>_session.Store.SetVisionArtifactDeleted(_micheId,artifact.Id,false))) ShowPastVisions();});
        foreach(var reset in state.VisionResets.Where(r=>r.MicheId==_micheId).OrderByDescending(r=>r.CreatedAt))
            AddButton(_dialogContents,"recover as a past vision · "+reset.CreatedAt.ToLocalTime().ToString("MMM d, h:mm tt"),()=>{
                Guid recovered=Guid.Empty;
                if(_session.Act(()=>recovered=_session.Store.RecoverVisionReset(_micheId,reset.Id))) OpenArtifact(recovered);
            });
        AddButton(_dialogContents,"done",CancelDialog);
    }
}
