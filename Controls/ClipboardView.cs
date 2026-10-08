using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Miche.Mac.Models;
using Miche.Mac.Services;

namespace Miche.Mac.Controls;

public sealed class ClipboardView : UserControl
{
    private readonly WorkspaceSession _session;
    private readonly TextBlock _caption=new(){FontSize=11,FontFamily=new FontFamily("Menlo"),Foreground=Brush.Parse("#DFA6AD")};
    private readonly TextBox _name=new(){MaxLength=120,IsVisible=false,Watermark="name this shelf"};
    public TextBox LabelEditor {get;}=new(){MaxLength=120,IsVisible=false};
    public TextBox LinkEditor {get;}=new(){Watermark="paste a link + enter",MaxLength=4096,FontSize=11,BorderThickness=new Thickness(0)};
    private readonly WrapPanel _entries=new();
    private readonly StackPanel _options=new(){IsVisible=false,Spacing=4};
    private readonly TextBlock _message=new(){FontSize=10,TextWrapping=TextWrapping.Wrap,Foreground=Brush.Parse("#BEA8B9"),IsVisible=false};
    private readonly Button _popout;
    private bool _recovery;
    public ClipboardView(WorkspaceSession session)
    {
        _session=session;
        var body=new StackPanel {Spacing=8};
        var header=new Grid {ColumnDefinitions=new ColumnDefinitions("*,Auto,Auto")};header.Children.Add(_caption);
        _popout=Button("↗",()=>{if(_session.ClipboardWindow is null)_session.PopoutClipboard();else _session.DockClipboard();},"Pop out or dock Clipboard");Grid.SetColumn(_popout,1);header.Children.Add(_popout);
        var more=Button("⋯",()=>{_options.IsVisible=!_options.IsVisible;Refresh();},"Clipboard options");Grid.SetColumn(more,2);header.Children.Add(more);body.Children.Add(header);body.Children.Add(_entries);
        var add=new Grid {ColumnDefinitions=new ColumnDefinitions("*,Auto")};add.Children.Add(LinkEditor);
        var file=Button("+ file",async()=>await PickFile(),"Add a file to Clipboard");Grid.SetColumn(file,1);add.Children.Add(file);body.Children.Add(add);
        LinkEditor.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){AddLink();e.Handled=true;}};
        _options.Children.Add(Button("rename",()=>{_name.Text=_session.Store.Snapshot.Clipboard.Title;_name.IsVisible=true;_name.Focus();_name.SelectAll();}));
        _options.Children.Add(_name);_name.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){if(Rename(_name.Text??""))_name.IsVisible=_options.IsVisible=false;e.Handled=true;}else if(e.Key==Key.Escape){_name.IsVisible=false;e.Handled=true;}};
        _options.Children.Add(Button("recently deleted",()=>{_recovery=!_recovery;Refresh();}));
        _options.Children.Add(Button("hide here",()=>_session.Act(()=>_session.Store.ShowClipboard(_session.Store.Snapshot.Index.ActiveMicheId,false))));body.Children.Add(_options);body.Children.Add(_message);
        Content=new Border {Padding=new Thickness(12,10),CornerRadius=new CornerRadius(14),BorderBrush=Brush.Parse("#5D435C"),BorderThickness=new Thickness(1),Background=Brush.Parse("#2C2033"),Child=body};
        DragDrop.SetAllowDrop(this,true);
        DragDrop.AddDragOverHandler(this,(_,e)=>{e.DragEffects=e.DataTransfer.TryGetFiles()?.Any()==true||WorkspaceStore.SafeClipboardUrl(e.DataTransfer.TryGetText()?.Trim())?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;});
        DragDrop.AddDropHandler(this,(_,e)=>{e.Handled=true;foreach(var item in e.DataTransfer.TryGetFiles()??Array.Empty<IStorageItem>()){var path=item.TryGetLocalPath();if(path is not null)AddFile(path);}var text=e.DataTransfer.TryGetText()?.Trim();if(WorkspaceStore.SafeClipboardUrl(text)){LinkEditor.Text=text;AddLink();}});
        Refresh();
    }
    private static Button Button(string text,Action action,string? description=null)
    {
        var button=new Button {Content=text,FontSize=11,Padding=new Thickness(5,3),Margin=new Thickness(2),CornerRadius=new CornerRadius(8)};
        Avalonia.Automation.AutomationProperties.SetName(button,description??text);ToolTip.SetTip(button,description??text);button.Click+=(_,_)=>action();return button;
    }
    private void Message(string text){_message.Text=text;_message.IsVisible=true;}
    public bool Rename(string name)=>_session.Act(()=>_session.Store.RenameClipboard(name));
    public bool AddLink()
    {
        var url=(LinkEditor.Text??"").Trim();var label=LabelEditor.Text;
        if(string.IsNullOrWhiteSpace(label)&&Uri.TryCreate(url,UriKind.Absolute,out var uri))
        {var host=uri.Host.StartsWith("www.")?uri.Host[4..]:uri.Host;label=host.Equals("linkedin.com",StringComparison.OrdinalIgnoreCase)?"LinkedIn":host;}
        if(!_session.Act(()=>_session.Store.AddClipboardLink(label??"",url)))return false;
        LabelEditor.Text="";LinkEditor.Text="";Refresh();return true;
    }
    public bool AddFile(string path)
    {var ok=_session.Act(()=>_session.Store.AddClipboardFile(path,string.IsNullOrWhiteSpace(LabelEditor.Text)?null:LabelEditor.Text));if(ok){LabelEditor.Text="";Refresh();}return ok;}
    private async Task PickFile()
    {
        var top=TopLevel.GetTopLevel(this);if(top is null)return;
        var files=await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {Title="Keep a file in Clipboard",AllowMultiple=true});
        foreach(var file in files){var path=file.TryGetLocalPath();if(path is not null)AddFile(path);}
    }
    private async Task Open(ClipboardEntry entry)
    {
        try {
            var top=TopLevel.GetTopLevel(this);if(top is null)return;bool ok;
            if(entry.Url is not null)ok=await top.Launcher.LaunchUriAsync(new Uri(entry.Url));
            else {var file=await top.StorageProvider.TryGetFileFromPathAsync(_session.Store.ClipboardFilePath(entry.FileName!));ok=file is not null&&await top.Launcher.LaunchFileAsync(file);}
            if(!ok)Message("Couldn’t open this item. Check its link or saved file.");
        }catch(Exception ex)when(ex is IOException or ArgumentException or UnauthorizedAccessException){Message(ex.Message);}
    }
    public async Task CopyUrl(ClipboardEntry entry)
    {
        var clipboard=TopLevel.GetTopLevel(this)?.Clipboard;if(clipboard is null||entry.Url is null)return;
        await clipboard.SetTextAsync(entry.Url);Message("copied ♡");
    }
    public void Refresh()
    {
        var shelf=_session.Store.Snapshot.Clipboard;_caption.Text=shelf.Title;
        _popout.Content=_session.ClipboardWindow is null?"↗":"↙";_entries.Children.Clear();
        foreach(var entry in shelf.Entries.Where(e=>_recovery?e.DeletedAt is not null:e.DeletedAt is null))
        {
            var chip=new StackPanel {Orientation=Orientation.Horizontal,Spacing=0};
            chip.Children.Add(Button((entry.Url is null?"▧ ":"↗ ")+entry.Label,async()=>{if(_recovery)_session.Act(()=>_session.Store.SetClipboardDeleted(entry.Id,false));else if(entry.Url is not null)await CopyUrl(entry);else await Open(entry);},_recovery?"Restore "+entry.Label:entry.Url is not null?"Copy "+entry.Label+" URL":"Open "+entry.Label));
            if(!_recovery&&entry.Url is not null)chip.Children.Add(Button("↗",async()=>await Open(entry),"Open "+entry.Label+" link"));
            if(_options.IsVisible&&!_recovery)chip.Children.Add(Button("×",()=>_session.Act(()=>_session.Store.SetClipboardDeleted(entry.Id,true)),"Remove "+entry.Label));
            _entries.Children.Add(new Border {Background=Brush.Parse("#392B40"),CornerRadius=new CornerRadius(9),Margin=new Thickness(0,0,5,5),Child=chip});
        }
        if(_entries.Children.Count==0)_entries.Children.Add(new TextBlock {Text=_recovery?"nothing deleted":"drop your everyday things here ♡",FontSize=11,Foreground=Brush.Parse("#BEA8B9")});
    }
}
