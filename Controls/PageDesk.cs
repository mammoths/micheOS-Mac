using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Miche.Mac.Services;
namespace Miche.Mac.Controls;
public sealed class PageDesk:Canvas
{
    private WorkspaceSession? _session;private readonly Dictionary<Guid,PageView> _cards=new();private Guid? _moving;private bool _resize;private Point _start;private (double X,double Y,double W,double H) _before;private bool _dragged;private IPointer? _pointer;
    public PageDesk(){Background=Brushes.Transparent;MinHeight=650;ClipToBounds=false;Focusable=true;PointerPressed+=(_,e)=>{if(e.ClickCount==2&&e.Source==this&&_session is not null){var point=e.GetPosition(this);_session.CreatePage(point.X,point.Y);e.Handled=true;}};KeyDown+=(_,e)=>{if(e.Key==Key.Escape){CancelGesture();e.Handled=true;}};}
    public void Connect(WorkspaceSession session){_session=session;Refresh();}
    public void Refresh()
    {
        if(_session is null)return;var miche=_session.Store.Snapshot.Index.ActiveMicheId;
        var wanted=_session.Pages.Snapshot.Pages.Where(p=>p.MicheId==miche&&p.ParentId is null&&p.DeletedAt is null&&!p.Floating).ToDictionary(p=>p.Id);
        foreach(var id in _cards.Keys.Where(id=>!wanted.ContainsKey(id)).ToArray()){Children.Remove(_cards[id]);_cards.Remove(id);}
        foreach(var p in wanted.Values){if(!_cards.TryGetValue(p.Id,out var card)){card=_session.PageViewFor(p.Id);Detach(card);_cards[p.Id]=card;Children.Add(card);Wire(card);}if(_moving!=p.Id){SetLeft(card,p.Left);SetTop(card,p.Top);card.Width=p.Width;card.Height=p.Height;}card.RefreshHost();}
        Height=Math.Max(650,wanted.Values.Select(p=>p.Top+p.Height+80).DefaultIfEmpty(650).Max());
    }
    public static void Detach(PageView view){var old=TopLevel.GetTopLevel(view);if(view.Parent is Panel p)p.Children.Remove(view);else if(view.Parent is ContentControl c)c.Content=null;old?.UpdateLayout();}
    private readonly HashSet<Guid> _wired=new();
    private void Wire(PageView card)
    {
        if(!_wired.Add(card.PageId))return;
        card.Header.PointerPressed+=(_,e)=>{var control=e.Source as Control;if(control is TextBox||control?.GetVisualAncestors().Any(a=>a is TextBox or Button)==true||control is Button)return;Start(card,e,false);};
        card.MoveGrip.AddHandler(PointerPressedEvent,(_,e)=>Start(card,e,false),RoutingStrategies.Tunnel);
        card.ResizeGrip.AddHandler(PointerPressedEvent,(_,e)=>Start(card,e,true),RoutingStrategies.Tunnel);
        card.PointerMoved+=(_,e)=>Move(card,e);card.PointerReleased+=(_,e)=>End(card,e);card.PointerCaptureLost+=(_,_)=>{if(_moving==card.PageId)CancelGesture();};
    }
    private void Start(PageView card,PointerPressedEventArgs e,bool resize){if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed||_moving is not null)return;_moving=card.PageId;_pointer=e.Pointer;_resize=resize;_start=e.GetPosition(this);_before=(GetLeft(card),GetTop(card),card.Width,card.Height);_dragged=false;e.Pointer.Capture(card);card.Focus();e.Handled=true;}
    private void Move(PageView card,PointerEventArgs e){if(_moving!=card.PageId)return;var d=e.GetPosition(this)-_start;_dragged|=Math.Abs(d.X)+Math.Abs(d.Y)>4;if(!_dragged)return;if(_resize){card.Width=Math.Clamp(_before.W+d.X,280,Math.Min(2000,Math.Max(280,Bounds.Width-GetLeft(card))));card.Height=Math.Clamp(_before.H+d.Y,220,2000);}else{SetLeft(card,Math.Clamp(_before.X+d.X,0,Math.Max(0,Bounds.Width-card.Width)));SetTop(card,Math.Clamp(_before.Y+d.Y,0,20000));}e.Handled=true;}
    private void End(PageView card,PointerReleasedEventArgs e){if(_moving!=card.PageId)return;Move(card,e);var changed=_dragged;_moving=null;_pointer=null;e.Pointer.Capture(null);if(changed&&_session is not null){var target=!_resize?_cards.Values.LastOrDefault(v=>v!=card&&e.GetPosition(this).X>=GetLeft(v)&&e.GetPosition(this).X<=GetLeft(v)+v.Width&&e.GetPosition(this).Y>=GetTop(v)&&e.GetPosition(this).Y<=GetTop(v)+42):null;
        if(target is not null)_session.CollectPage(card.PageId,target.PageId);
        else if(!_session.Act(()=>_session.Pages.Place(card.PageId,GetLeft(card),GetTop(card),card.Width,card.Height)))Restore(card);Refresh();}e.Handled=true;}
    private void Restore(PageView card){SetLeft(card,_before.X);SetTop(card,_before.Y);card.Width=_before.W;card.Height=_before.H;}
    public void CancelGesture(){if(_moving is Guid id&&_cards.TryGetValue(id,out var card))Restore(card);_moving=null;_pointer?.Capture(null);_pointer=null;}
}
