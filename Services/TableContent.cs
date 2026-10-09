using System.Text.Json;
using Miche.Mac.Models;
namespace Miche.Mac.Services;
// Shared document contract used by page blocks, standalone table widgets and Vision.
public static class TableContent
{
    public static readonly (string Name,string Color)[] Palette={ ("Ink","#302638"),("Rose","#573B4B"),("Lavender","#45405D"),("Sage","#354A43"),("Sand","#514735"),("Sky","#344753") };
    public static PageBlock Create(int rows=3,int columns=3)=>new(){Kind="table",Cells=Enumerable.Range(0,rows).Select(_=>Enumerable.Range(0,columns).Select(_=>new List<PageRun>()).ToList()).ToList()};
    public static PageBlock Copy(PageBlock block){Validate(block);return JsonSerializer.Deserialize<PageBlock>(JsonSerializer.Serialize(block))!;}
    public static void Validate(PageBlock b)
    {
        if(b.Kind!="table"||b.Cells is null||b.Cells.Count is <1 or >50||b.Cells[0] is null||b.Cells[0].Count is <1 or >12||b.Cells.Any(r=>r is null||r.Count!=b.Cells[0].Count||r.Any(c=>c is null||c.Count>2000||c.Any(t=>t is null||t.Text is null||t.Text.Length>100000||!double.IsFinite(t.Size)||t.Size<10||t.Size>96)))||b.ColumnWidths is null||b.RowHeights is null||b.CellColors is null||b.ColumnWidths.Count!=0&&b.ColumnWidths.Count!=b.Cells[0].Count||b.RowHeights.Count!=0&&b.RowHeights.Count!=b.Cells.Count||b.ColumnWidths.Any(w=>!double.IsFinite(w)||w<40||w>1000)||b.RowHeights.Any(h=>!double.IsFinite(h)||h<28||h>1000))throw new InvalidDataException("Invalid table content or dimensions.");
        foreach(var (key,color) in b.CellColors){var parts=key.Split(':');if(parts.Length!=2||!int.TryParse(parts[0],out var r)||!int.TryParse(parts[1],out var c)||r<0||c<0||r>=b.Cells.Count||c>=b.Cells[0].Count||!Palette.Any(p=>p.Color==color))throw new InvalidDataException("Invalid table color or cell reference.");}
    }
    public static void Dimensions(PageBlock b){if(b.ColumnWidths.Count==0)b.ColumnWidths=Enumerable.Repeat(120d,b.Cells[0].Count).ToList();if(b.RowHeights.Count==0)b.RowHeights=Enumerable.Repeat(54d,b.Cells.Count).ToList();}
    public static void Insert(PageBlock b,bool row,int index)
    {
        Validate(b);Dimensions(b);if(row){if(b.Cells.Count==50)throw new ArgumentException("A table supports up to 50 rows.");b.Cells.Insert(index,Enumerable.Range(0,b.Cells[0].Count).Select(_=>new List<PageRun>()).ToList());b.RowHeights.Insert(index,54);}else{if(b.Cells[0].Count==12)throw new ArgumentException("A table supports up to 12 columns.");foreach(var r in b.Cells)r.Insert(index,new());b.ColumnWidths.Insert(index,120);}RemapColors(b,row,index,false);
    }
    public static void Remove(PageBlock b,bool row,int index)
    {Validate(b);Dimensions(b);if(row){if(b.Cells.Count==1)throw new ArgumentException("Keep at least one row.");b.Cells.RemoveAt(index);b.RowHeights.RemoveAt(index);}else{if(b.Cells[0].Count==1)throw new ArgumentException("Keep at least one column.");foreach(var r in b.Cells)r.RemoveAt(index);b.ColumnWidths.RemoveAt(index);}RemapColors(b,row,index,true);}
    private static void RemapColors(PageBlock b,bool row,int index,bool remove)
    {var next=new Dictionary<string,string>();foreach(var (key,color) in b.CellColors){var parts=key.Split(':').Select(int.Parse).ToArray();var axis=row?0:1;if(remove&&parts[axis]==index)continue;if(parts[axis]>=index)parts[axis]+=remove?-1:1;next[$"{parts[0]}:{parts[1]}"]=color;}b.CellColors=next;}
    public static void Resize(PageBlock b,bool rows,IEnumerable<int> indices,double value){Dimensions(b);foreach(var i in indices.Distinct())(rows?b.RowHeights:b.ColumnWidths)[i]=Math.Clamp(value,rows?28:40,1000);}
}
