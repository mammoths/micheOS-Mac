using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Miche.Mac.Controls;

namespace Miche.Mac;

public sealed class MeiliWindow : Window
{
    public MeiliWebView Studio { get; }
    public bool CanCloseSaved { get; private set; }
    private bool _closing;
    public MeiliWindow(string directory)
    {
        Title="Miche · The Daily Meili";Width=1120;Height=820;MinWidth=520;MinHeight=420;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=Brush.Parse("#302238");MacWindowChrome.Apply(this);
        var grid=new Grid {RowDefinitions=new RowDefinitions("46,*,28")};
        var header=new Grid {ColumnDefinitions=new ColumnDefinitions("*,Auto,Auto"),Margin=new Thickness(18,0)};
        header.Children.Add(new TextBlock {Text="miche / the daily meili",Foreground=Brush.Parse("#DFA6AD"),VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center});
        var full=new Button {Content="full size",Margin=new Thickness(0,0,14,0)};full.Click+=(_,_)=>WindowState=WindowState==WindowState.FullScreen?WindowState.Normal:WindowState.FullScreen;
        Avalonia.Automation.AutomationProperties.SetName(full,"Full size Meili Studio");Grid.SetColumn(full,1);header.Children.Add(full);
        var buttons=new WindowButtons();Grid.SetColumn(buttons,2);header.Children.Add(buttons);header.PointerPressed+=(_,e)=>MacWindowChrome.HeaderPressed(this,e);grid.Children.Add(header);
        Studio=new MeiliWebView(directory);Grid.SetRow(Studio,1);grid.Children.Add(Studio);
        var status=new TextBlock {Text="Meili Studio · local project",Margin=new Thickness(18,4),FontSize=12,Foreground=Brush.Parse("#BEA8B9")};Grid.SetRow(status,2);grid.Children.Add(status);Studio.StatusChanged+=s=>status.Text=s;Content=grid;
        Closing+=(_,e)=>{if(CanCloseSaved)return;e.Cancel=true;if(_closing)return;_closing=true;Flush(ok=>{_closing=false;if(ok){CanCloseSaved=true;Close();}});};
    }
    public void Flush(Action<bool> done)=>Studio.Flush((ok,message)=>{if(!ok)StudioStatus(message);done(ok);});
    public void CloseSaved() {CanCloseSaved=true;Close();}
    private void StudioStatus(string message) { if(Content is Grid g && g.Children.LastOrDefault() is TextBlock t)t.Text=message; }
}
