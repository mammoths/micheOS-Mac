using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Miche.Mac.Services;
namespace Miche.Mac.Controls;
public sealed class PageView:Border
{
    public Guid PageId { get; }
    public PageWebView Editor { get; }
    public TextBox TitleEditor { get; }
    public Border Header { get; }
    public Button ResizeGrip { get; }
    public Button MoveGrip { get; }
    private readonly WorkspaceSession _session;
    private readonly Button _pop;
    public PageView(WorkspaceSession session,Guid id)
    {
        _session=session;PageId=id;Background=Brush.Parse("#261E2D");BorderBrush=Brush.Parse("#62495E");BorderThickness=new Thickness(1);CornerRadius=new CornerRadius(14);ClipToBounds=true;
        var layout=new Grid{RowDefinitions=new RowDefinitions("42,*,24")};Header=new Border{Background=Brush.Parse("#2D2233"),Padding=new Thickness(14,0,6,0)};
        var head=new Grid{ColumnDefinitions=new ColumnDefinitions("Auto,*,Auto,Auto")};TitleEditor=new TextBox{Text=session.Pages.Get(id).Title,Watermark="untitled",FontFamily="Georgia",FontSize=16,BorderThickness=new Thickness(0),Background=Brushes.Transparent,MaxLength=160,VerticalContentAlignment=Avalonia.Layout.VerticalAlignment.Center};MoveGrip=new Button{Content="⋮⋮",Padding=new Thickness(3),FontSize=13,Cursor=new Cursor(StandardCursorType.SizeAll)};head.Children.Add(MoveGrip);Grid.SetColumn(TitleEditor,1);head.Children.Add(TitleEditor);
        _pop=new Button{Content="↗",FontSize=15};Grid.SetColumn(_pop,2);head.Children.Add(_pop);_pop.Click+=(_,_)=>{if(session.Pages.Get(id).Floating)session.DockPage(id);else session.PopoutPage(id);};
        var more=new Button{Content="⋯",FontSize=16};Grid.SetColumn(more,3);head.Children.Add(more);var menu=new ContextMenu();void item(string text,Action action){var m=new MenuItem{Header=text};m.Click+=(_,_)=>action();menu.Items.Add(m);}
        item("Expand page",()=>session.PopoutPage(id,true));item("Move out of parent page",()=>session.Act(()=>session.Pages.Nest(id,null)));item("Recently deleted pages…",()=>session.ShowDeletedPages());item("Move to recently deleted",()=>session.DeletePage(id));more.Click+=(_,_)=>menu.Open(more);
        Header.Child=head;layout.Children.Add(Header);Editor=new PageWebView(session,id);Grid.SetRow(Editor,1);layout.Children.Add(Editor);
        var foot=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto")};var status=new TextBlock{Text="",FontFamily="Menlo",FontSize=9,Margin=new Thickness(12,2),Foreground=Brush.Parse("#C9A7B6"),TextTrimming=TextTrimming.CharacterEllipsis};foot.Children.Add(status);Editor.Status+=s=>status.Text=s;
        ResizeGrip=new Button{Content="⌟",FontSize=15,Padding=new Thickness(5,0),Cursor=new Cursor(StandardCursorType.SizeAll)};Grid.SetColumn(ResizeGrip,1);foot.Children.Add(ResizeGrip);Grid.SetRow(foot,2);layout.Children.Add(foot);Child=layout;
        TitleEditor.TextChanged+=(_,_)=>{session.Act(()=>session.Pages.SaveTitle(id,TitleEditor.Text??""));Editor.Rename(TitleEditor.Text??"");};
        Avalonia.Automation.AutomationProperties.SetName(TitleEditor,"Page title");Avalonia.Automation.AutomationProperties.SetName(_pop,"Pop out or dock page");Avalonia.Automation.AutomationProperties.SetName(ResizeGrip,"Resize page widget");
        AddHandler(KeyDownEvent,(_,e)=>{if(e.Key==Key.Q&&e.KeyModifiers==KeyModifiers.Meta){session.TryQuit();e.Handled=true;}},RoutingStrategies.Tunnel);
    }
    public void RefreshHost(){var p=_session.Pages.Get(PageId);_pop.Content=p.Floating?"↙":"↗";ResizeGrip.IsVisible=!p.Floating;Editor.ChildrenChanged();}
    public void Flush(Action<bool> done)=>Editor.Flush(done);
}
