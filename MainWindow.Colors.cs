using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Miche.Mac.Controls;

namespace Miche.Mac;

public partial class MainWindow
{
    private readonly Popup _colorPicker=new(){Name="MicheColorPicker",Placement=PlacementMode.Top,VerticalOffset=-8,IsLightDismissEnabled=true};
    private Guid? _colorMicheId;
    private string? _homeColor;
    private void InitializeMicheColors()
    {
        _colorPicker.PlacementTarget=ColorPickerButton;
        ((Grid)Content!).Children.Add(_colorPicker);
    }
    private void RefreshMicheColor(Models.Miche miche)
    {
        if(_colorMicheId is { } id&&id!=miche.Id)_colorPicker.IsOpen=false;
        var color=MichePalette.For(miche);
        if(_homeColor==color.Note)return;
        _homeColor=color.Note;
        var accent=Brush.Parse(color.Note);
        Background=new LinearGradientBrush {
            StartPoint=new RelativePoint(0,0,RelativeUnit.Relative),EndPoint=new RelativePoint(0,1,RelativeUnit.Relative),
            GradientStops=new GradientStops {new GradientStop(Color.Parse(color.Top),0),new GradientStop(Color.Parse(color.Bottom),1)}
        };
        HomeMotif.Stroke=HeaderMotif.Stroke=accent;
        MichePrefix.Foreground=SpaceTitle.Foreground=QuerySlash.Foreground=DateLabel.Foreground=accent;
        ColorPickerIcon.Stroke=accent;ColorPickerDot.Fill=accent;
        ToolTip.SetTip(ColorPickerButton,"Miche color · "+color.Name);
    }
    private void OpenMicheColors(object? sender,RoutedEventArgs e)
    {
        if(_colorPicker.IsOpen){_colorPicker.IsOpen=false;return;}
        SpaceMenuButton.ContextMenu?.Close();LookupSuggestions.IsOpen=false;
        var state=_store!.Snapshot;var miche=state.Index.Miches.Single(m=>m.Id==state.Index.ActiveMicheId);
        _colorMicheId=miche.Id;
        var current=MichePalette.For(miche);
        var panel=new StackPanel {Spacing=12};
        var heading=new Grid {ColumnDefinitions=new ColumnDefinitions("*,Auto")};
        heading.Children.Add(new TextBlock {Text="color for "+miche.Name,FontFamily=new FontFamily("Georgia"),FontSize=17,TextWrapping=TextWrapping.Wrap,MaxWidth=214});
        var close=new Button {Content="×",Padding=new Thickness(4),VerticalAlignment=VerticalAlignment.Top};
        AutomationProperties.SetName(close,"Close Miche colors");close.Click+=(_,_)=>{_colorPicker.IsOpen=false;ColorPickerButton.Focus();};
        Grid.SetColumn(close,1);heading.Children.Add(close);panel.Children.Add(heading);
        var colors=new UniformGrid {Columns=3};
        foreach(var color in MichePalette.Colors)
        {
            var choice=new StackPanel {Spacing=6,HorizontalAlignment=HorizontalAlignment.Center};
            choice.Children.Add(new Border {Width=52,Height=30,CornerRadius=new CornerRadius(9),Background=Brush.Parse(color.Note),
                Child=new TextBlock {Text=current.Note==color.Note?"✓":"",Foreground=Brush.Parse("#393631"),FontSize=18,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}});
            choice.Children.Add(new TextBlock {Text=color.Name,FontSize=10,HorizontalAlignment=HorizontalAlignment.Center,Foreground=Brush.Parse("#F4E5D1")});
            var button=new Button {Content=choice,Padding=new Thickness(8),Tag=color.Note};
            AutomationProperties.SetName(button,"Set Miche color to "+color.Name);
            button.Click+=(_,_)=>{
                if(Act(()=>_store.SetMicheNoteColor(miche.Id,color.Note),"Miche color · "+color.Name))
                {_colorPicker.IsOpen=false;ColorPickerButton.Focus();}
            };
            colors.Children.Add(button);
        }
        panel.Children.Add(colors);
        _colorPicker.Child=new Border {Width=282,Padding=new Thickness(14),CornerRadius=new CornerRadius(14),Background=Brush.Parse(current.Surface),BorderBrush=Brush.Parse(current.Note),BorderThickness=new Thickness(1),Child=panel};
        _colorPicker.IsOpen=true;
    }
}
