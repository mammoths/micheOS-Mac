using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Miche.Mac.Models;
using Miche.Mac.Services;
namespace Miche.Mac.Controls;
public sealed partial class VisionView
{
    private static void ScaleTable(VisionItem item,double factor){if(item.Table is null)return;TableContent.Dimensions(item.Table);item.Table.ColumnWidths=item.Table.ColumnWidths.Select(w=>Math.Clamp(w*factor,40,1000)).ToList();item.Table.RowHeights=item.Table.RowHeights.Select(h=>Math.Clamp(h*factor,28,1000)).ToList();item.Width=Math.Min(4000,item.Table.ColumnWidths.Sum());item.Height=Math.Min(4000,item.Table.RowHeights.Sum()+26);}
    public bool AddTable(Point point)
    {
        if(!PrepareToLeave())return false;var table=TableContent.Create();TableContent.Dimensions(table);var item=new VisionItem{Kind="table",Table=table,Left=point.X,Top=point.Y,Width=360,Height=188};
        if(!SaveCanvasItems(new[]{item}))return false;_selection.Clear();_selected=item.Id;RefreshBoard();return true;
    }
    private Control MakeTable(VisionItem item)
    {
        var layout=new Grid{RowDefinitions=new RowDefinitions("26,*")};layout.Children.Add(new TextBlock{Text="table · drag here",FontFamily="Menlo",FontSize=10,Margin=new Thickness(8,5),Foreground=Brush.Parse("#C9A7B6")});
        var table=new TableView(item.Table!,changed=>{var latest=Items().Single(i=>i.Id==item.Id);latest.Table=TableContent.Copy(changed);TableContent.Dimensions(latest.Table);latest.Width=Math.Min(4000,latest.Table.ColumnWidths.Sum());latest.Height=Math.Min(4000,latest.Table.RowHeights.Sum()+26);_committing=true;try{var ok=SaveCanvasItems(new[]{latest});if(ok&&_objects.TryGetValue(item.Id,out var control))Position(control,latest);return ok;}finally{_committing=false;}});
        var scroll=new ScrollViewer{Content=table,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto};Grid.SetRow(scroll,1);layout.Children.Add(scroll);return layout;
    }
}
