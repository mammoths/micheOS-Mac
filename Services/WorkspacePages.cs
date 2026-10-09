using Avalonia.Controls;
using Avalonia.Threading;
using Miche.Mac.Controls;
namespace Miche.Mac.Services;
public sealed partial class WorkspaceSession
{
    public PageRepository Pages { get; private set; }=null!;
    private readonly Dictionary<Guid,PageView> _pageViews=new();
    private readonly Dictionary<Guid,PageWindow> _pageWindows=new();
    private readonly HashSet<Guid> _pageTransfers=new();
    private bool _pageQuitApproved,_pageQuitPending;
    private void InitializePages(){Pages=new PageRepository(Store.DirectoryPath);Pages.ContentChanged+=()=>{foreach(var v in _pageViews.Values)v.Editor.ChildrenChanged();foreach(var (id,w) in _pageWindows)w.Title="Miche · "+(Pages.Get(id).Title.Length==0?"untitled page":Pages.Get(id).Title);};Pages.Changed+=()=>{foreach(var v in _pageViews.Values)v.RefreshHost();_home?.RefreshPages();};}
    public PageView PageViewFor(Guid id){if(!_pageViews.TryGetValue(id,out var view)){view=new PageView(this,id);_pageViews.Add(id,view);}return view;}
    public void CreatePage(double x=24,double y=24,Guid? parent=null,bool table=false){Guid id=Guid.Empty;if(!Act(()=>id=Pages.Create(parent is Guid parentId?Pages.Get(parentId).MicheId:Store.Snapshot.Index.ActiveMicheId,Math.Min(x,Math.Max(0,(_home?.Bounds.Width??980)-500)),y,parent)))return;if(table)Act(()=>Pages.SaveContent(id,"table",new(){TableContent.Create()}));if(parent is not null)PopoutPage(id);else Dispatcher.UIThread.Post(()=>PageViewFor(id).Editor.Focus());}
    public void PopoutPage(Guid id,bool expanded=false)
    {
        if(_pageWindows.TryGetValue(id,out var existing)){existing.Show();existing.Activate();if(expanded)existing.WindowState=WindowState.Maximized;return;}
        if(!_pageTransfers.Add(id))return;var view=PageViewFor(id);view.Flush(ok=>{_pageTransfers.Remove(id);if(!ok)return;PageDesk.Detach(view);if(!Act(()=>Pages.Host(id,true))){_home?.RefreshPages();return;}var window=new PageWindow(this,id,view);_pageWindows.Add(id,window);window.Show();view.RefreshHost();if(expanded)window.WindowState=WindowState.Maximized;});
    }
    public void DockPage(Guid id,Action? done=null)
    {
        if(!_pageWindows.TryGetValue(id,out var window)||!_pageTransfers.Add(id)){done?.Invoke();return;}var view=PageViewFor(id);view.Flush(ok=>{_pageTransfers.Remove(id);if(!ok){done?.Invoke();return;}window.Content=null;if(!Act(()=>{Pages.Host(id,false,window.Geometry());Store.Activate(Pages.Get(id).MicheId);})){window.Content=view;done?.Invoke();return;}_pageWindows.Remove(id);window.CloseForTransfer();OpenHome();_home?.RefreshPages();done?.Invoke();});
    }
    public void CollectPage(Guid id,Guid parent){var view=PageViewFor(id);view.Flush(ok=>{if(ok)Act(()=>Pages.Nest(id,parent));});}
    public void DeletePage(Guid id){PageViewFor(id).Flush(ok=>{if(!ok)return;if(Act(()=>Pages.Delete(id,true))&&_pageWindows.Remove(id,out var w))w.CloseForTransfer();});}
    public void ShowDeletedPages(){var window=new Window{Title="Miche · recently deleted pages",Width=420,Height=360};var list=new StackPanel{Margin=new Avalonia.Thickness(20),Spacing=12};foreach(var p in Pages.Snapshot.Pages.Where(p=>p.DeletedAt is not null)){var b=new Button{Content="restore · "+(p.Title.Length==0?"untitled":p.Title)};b.Click+=(_,_)=>{if(Act(()=>Pages.Delete(p.Id,false)))window.Close();};list.Children.Add(b);}if(list.Children.Count==0)list.Children.Add(new TextBlock{Text="nothing deleted"});window.Content=new ScrollViewer{Content=list};window.Show();}
    private bool FlushPagesForQuit()
    {
        if(_pageQuitApproved||_pageViews.Count==0)return true;if(_pageQuitPending)return false;_pageQuitPending=true;var views=_pageViews.Values.ToArray();int index=0;void next(){if(index==views.Length){_pageQuitPending=false;_pageQuitApproved=true;TryQuit();return;}views[index++].Flush(ok=>{if(!ok){_pageQuitPending=false;return;}next();});}next();return false;
    }
    private void RestorePageWindows(){foreach(var p in Pages.Snapshot.Pages.Where(p=>p.Floating&&p.DeletedAt is null&&Store.Snapshot.Index.Miches.Any(m=>m.Id==p.MicheId)).ToArray()){if(_pageWindows.ContainsKey(p.Id))continue;var view=PageViewFor(p.Id);PageDesk.Detach(view);var window=new PageWindow(this,p.Id,view);_pageWindows.Add(p.Id,window);window.Show();}}
}
