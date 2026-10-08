using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Miche.Mac.Controls;
using Miche.Mac.Models;
using Miche.Mac.Services;

namespace Miche.Mac;

public sealed class FlowWindow : Window
{
    public FlowView View {get;}
    public FlowWindow(WorkspaceSession session,FlowRepository repository,string originName)
    {
        MacWindowChrome.Apply(this);Title="Flow · "+originName;Width=620;Height=760;MinWidth=480;MinHeight=360;Background=Brush.Parse("#241B2D");
        var root=new Grid {RowDefinitions=new("46,*")};Content=root;
        var header=new Grid {ColumnDefinitions=new("*,Auto"),Margin=new Thickness(18,0),Background=Brushes.Transparent};
        header.Children.Add(new TextBlock {Text="FLOW / "+originName+" · local",FontFamily=new("Menlo"),FontSize=12,Foreground=Brush.Parse("#E6C89E"),VerticalAlignment=VerticalAlignment.Center});
        var buttons=new WindowButtons {VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(buttons,1);header.Children.Add(buttons);header.PointerPressed+=(_,e)=>MacWindowChrome.HeaderPressed(this,e);root.Children.Add(header);
        View=new FlowView(repository);View.HomeRequested+=()=>session.OpenHome();Grid.SetRow(View,1);root.Children.Add(View);
        var saved=repository.Window;WindowStartupLocation=saved is null?WindowStartupLocation.CenterScreen:WindowStartupLocation.Manual;
        Opened+=(_,_)=>{
            if(saved is null)return;var area=(Screens.ScreenFromPoint(new PixelPoint(saved.X,saved.Y))??Screens.Primary)?.WorkingArea;var scale=Math.Max(.1,RenderScaling);
            Width=Math.Max(MinWidth,Math.Min(saved.Width,(area?.Width??1600)/scale));Height=Math.Max(MinHeight,Math.Min(saved.Height,(area?.Height??1000)/scale));
            Position=new PixelPoint(area is {} a?Math.Clamp(saved.X,a.X,Math.Max(a.X,a.Right-(int)(Width*scale))):saved.X,area is {} b?Math.Clamp(saved.Y,b.Y,Math.Max(b.Y,b.Bottom-(int)(Height*scale))):saved.Y);
        };
        Closing+=(_,e)=>{if(!session.IsQuitting)e.Cancel=!SaveBeforeClose();};Closed+=(_,_)=>View.Dispose();
        KeyDown+=(_,e)=>{if(e.Key==Key.Q&&e.KeyModifiers==KeyModifiers.Meta){e.Handled=true;session.TryQuit();}};
    }
    public bool SaveBeforeClose()
    {
        if(!View.PrepareToLeave())return false;
        try {View.Repository.SaveWindow(new WindowGeometry {X=Position.X,Y=Position.Y,Width=Width,Height=Height});return true;}
        catch(Exception e) when(e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException){View.ShowError(e.Message);return false;}
    }
}
