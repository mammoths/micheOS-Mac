using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Miche.Mac.Models;
using Miche.Mac.Services;
namespace Miche.Mac.Controls;
public sealed class TableView:Grid
{
    private PageBlock _table;private readonly Func<PageBlock,bool> _save;private readonly HashSet<(int Row,int Col)> _selection=new();private readonly Dictionary<(int,int),TextBox> _editors=new();private (int Row,int Col) _anchor;private bool _rebuilding;
    public PageBlock Snapshot=>TableContent.Copy(_table);
    public TableView(PageBlock table,Func<PageBlock,bool> save){_table=TableContent.Copy(table);_save=save;Build();}
    public bool Change(Action<PageBlock> action){var next=Snapshot;action(next);TableContent.Validate(next);if(!_save(next))return false;_table=next;Build();return true;}
    public void SelectCells(IEnumerable<(int Row,int Col)> cells){_selection.Clear();foreach(var cell in cells)_selection.Add(cell);Paint();}
    public bool ResizeSelection(bool rows,double value)=>Change(t=>TableContent.Resize(t,rows,_selection.Select(c=>rows?c.Row:c.Col),value));
    public bool ColorSelection(string color)=>Change(t=>{foreach(var (r,c) in _selection)t.CellColors[$"{r}:{c}"]=color;});
    private void Build()
    {
        _rebuilding=true;Children.Clear();_editors.Clear();ColumnDefinitions.Clear();RowDefinitions.Clear();TableContent.Dimensions(_table);
        foreach(var w in _table.ColumnWidths)ColumnDefinitions.Add(new ColumnDefinition(w,GridUnitType.Pixel));foreach(var h in _table.RowHeights)RowDefinitions.Add(new RowDefinition(h,GridUnitType.Pixel));
        for(int r=0;r<_table.Cells.Count;r++)for(int c=0;c<_table.Cells[r].Count;c++){
            var row=r;var col=c;var layer=new Grid();var text=new TextBox{Text=string.Concat(_table.Cells[r][c].Select(x=>x.Text)),FontFamily="Georgia",FontSize=16,Foreground=Brush.Parse("#F4E5D1"),Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(7),AcceptsReturn=true,TextWrapping=TextWrapping.Wrap};_editors[(r,c)]=text;
            var border=new Border{BorderBrush=Brush.Parse("#73576C"),BorderThickness=new Thickness(.5),Child=layer};Grid.SetRow(border,r);Grid.SetColumn(border,c);Children.Add(border);layer.Children.Add(text);
            text.AddHandler(PointerPressedEvent,(_,e)=>{if(e.KeyModifiers.HasFlag(KeyModifiers.Shift)){e.Handled=true;_selection.Clear();for(int a=Math.Min(row,_anchor.Row);a<=Math.Max(row,_anchor.Row);a++)for(int b=Math.Min(col,_anchor.Col);b<=Math.Max(col,_anchor.Col);b++)_selection.Add((a,b));Paint();}else if(e.KeyModifiers.HasFlag(KeyModifiers.Meta)||e.KeyModifiers.HasFlag(KeyModifiers.Control)){if(!_selection.Add((row,col)))_selection.Remove((row,col));e.Handled=true;Paint();}else{if(!_selection.Contains((row,col))){_selection.Clear();_selection.Add((row,col));}_anchor=(row,col);Paint();}},RoutingStrategies.Tunnel);
            text.TextChanged+=(_,_)=>{if(_rebuilding)return;var next=Snapshot;next.Cells[row][col]=new(){new PageRun{Text=text.Text??"",Size=16}};if(_save(next))_table=next;};
            text.KeyDown+=(_,e)=>{if(e.Key!=Key.Tab)return;e.Handled=true;int flat=row*_table.Cells[0].Count+col+(e.KeyModifiers.HasFlag(KeyModifiers.Shift)?-1:1);if(flat<0)return;if(flat>=_table.Cells.Count*_table.Cells[0].Count){if(!Change(t=>TableContent.Insert(t,true,t.Cells.Count)))return;}var next=(flat/_table.Cells[0].Count,flat%_table.Cells[0].Count);Dispatcher.UIThread.Post(()=>_editors[next].Focus());};
            var menu=new ContextMenu();void add(string label,Action action){var m=new MenuItem{Header=label};m.Click+=(_,_)=>action();menu.Items.Add(m);}
            add("Row above",()=>Change(t=>TableContent.Insert(t,true,row)));add("Row below",()=>Change(t=>TableContent.Insert(t,true,row+1)));add("Column left",()=>Change(t=>TableContent.Insert(t,false,col)));add("Column right",()=>Change(t=>TableContent.Insert(t,false,col+1)));
            add("Delete selected rows",()=>{if(_selection.Select(x=>x.Row).Distinct().Count()>=_table.Cells.Count)return;Change(t=>{foreach(var i in _selection.Select(x=>x.Row).Distinct().OrderDescending())TableContent.Remove(t,true,i);});_selection.Clear();Paint();});
            add("Delete selected columns",()=>{if(_selection.Select(x=>x.Col).Distinct().Count()>=_table.Cells[0].Count)return;Change(t=>{foreach(var i in _selection.Select(x=>x.Col).Distinct().OrderDescending())TableContent.Remove(t,false,i);});_selection.Clear();Paint();});
            menu.Items.Add(new Separator());foreach(var (name,color) in TableContent.Palette)add("Color · "+name,()=>ColorSelection(color));
            add("Equal column widths",()=>ResizeSelection(false,_table.ColumnWidths[col]));add("Equal row heights",()=>ResizeSelection(true,_table.RowHeights[row]));
            text.ContextMenu=menu;text.PointerPressed+=(_,e)=>{if(e.GetCurrentPoint(text).Properties.IsRightButtonPressed){if(!_selection.Contains((row,col)))SelectCells(new[]{(row,col)});}};
            void handle(bool rows){var grip=new Border{Background=Brushes.Transparent,Width=rows?double.NaN:5,Height=rows?5:double.NaN,HorizontalAlignment=rows?Avalonia.Layout.HorizontalAlignment.Stretch:Avalonia.Layout.HorizontalAlignment.Right,VerticalAlignment=rows?Avalonia.Layout.VerticalAlignment.Bottom:Avalonia.Layout.VerticalAlignment.Stretch,Cursor=new Cursor(rows?StandardCursorType.SizeNorthSouth:StandardCursorType.SizeWestEast)};layer.Children.Add(grip);Point start=default;double size=0;bool moving=false;grip.PointerPressed+=(_,e)=>{if(!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)return;if(!_selection.Contains((row,col)))SelectCells(new[]{(row,col)});start=e.GetPosition(this);size=rows?_table.RowHeights[row]:_table.ColumnWidths[col];moving=true;e.Pointer.Capture(grip);e.Handled=true;};grip.PointerMoved+=(_,e)=>{if(!moving)return;var d=e.GetPosition(this)-start;foreach(var i in _selection.Select(x=>rows?x.Row:x.Col).Distinct())if(rows)RowDefinitions[i].Height=new GridLength(Math.Clamp(size+d.Y,28,1000));else ColumnDefinitions[i].Width=new GridLength(Math.Clamp(size+d.X,40,1000));e.Handled=true;};grip.PointerReleased+=(_,e)=>{if(!moving)return;moving=false;var d=e.GetPosition(this)-start;e.Pointer.Capture(null);ResizeSelection(rows,size+(rows?d.Y:d.X));e.Handled=true;};grip.PointerCaptureLost+=(_,_)=>{if(moving){moving=false;Build();}};}
            handle(false);handle(true);Avalonia.Automation.AutomationProperties.SetName(text,$"Table row {r+1} column {c+1}");
        }
        _rebuilding=false;_selection.RemoveWhere(x=>x.Row>=_table.Cells.Count||x.Col>=_table.Cells[0].Count);Paint();
    }
    private void Paint(){foreach(var (key,text) in _editors){if(text.Parent is Grid layer&&layer.Parent is Border cell){cell.Background=_table.CellColors.TryGetValue($"{key.Item1}:{key.Item2}",out var color)?Brush.Parse(color):Brush.Parse("#302638");cell.BorderBrush=Brush.Parse(_selection.Contains(key)?"#DFA6AD":"#73576C");cell.BorderThickness=new Thickness(_selection.Contains(key)?1:.5);}}}
}
