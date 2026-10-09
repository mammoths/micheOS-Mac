using System.Text.Json;
using Avalonia.Media.Imaging;
using Miche.Mac.Models;
namespace Miche.Mac.Services;

public sealed class PageRepository
{
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    private PageBook _book;
    public string DirectoryPath { get; }
    public string FilePath=>Path.Combine(DirectoryPath,"pages.json");
    public event Action? Changed;
    public event Action? ContentChanged;
    public PageBook Snapshot=>JsonSerializer.Deserialize<PageBook>(JsonSerializer.Serialize(_book,Json),Json)!;
    public PageRepository(string directory)
    {
        DirectoryPath=directory;
        _book=File.Exists(FilePath)?JsonSerializer.Deserialize<PageBook>(File.ReadAllText(FilePath),Json)??throw new InvalidDataException("Pages file is empty; it has been preserved."):new();
        Validate(_book);
    }
    public PageNote Get(Guid id)=>Snapshot.Pages.Single(p=>p.Id==id);
    private void Commit(PageBook next,bool layout=true)
    {
        Validate(next);var bytes=JsonSerializer.SerializeToUtf8Bytes(next,Json);if(bytes.Length>20000000)throw new InvalidDataException("Pages are too large to save. Your previous save is intact.");
        var temp=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(bytes);f.Flush(true);}if(File.Exists(FilePath))File.Copy(FilePath,FilePath+".bak",true);File.Move(temp,FilePath,true);_book=next;}
        finally{if(File.Exists(temp))File.Delete(temp);}
        var observers=layout?Changed:ContentChanged;if(observers is not null)foreach(Action observer in observers.GetInvocationList())try{observer();}catch{ }
    }
    public Guid Create(Guid micheId,double left=24,double top=24,Guid? parent=null)
    {
        var next=Snapshot;var page=new PageNote{MicheId=micheId,Left=Math.Max(0,left),Top=Math.Max(0,top),ParentId=parent};next.Pages.Add(page);Commit(next);return page.Id;
    }
    public void SaveTitle(Guid id,string title){var next=Snapshot;next.Pages.Single(p=>p.Id==id).Title=title;Commit(next,false);}
    public void SaveContent(Guid id,string title,List<PageBlock> blocks)
    {var next=Snapshot;var p=next.Pages.Single(p=>p.Id==id);p.Title=title;p.Blocks=JsonSerializer.Deserialize<List<PageBlock>>(JsonSerializer.Serialize(blocks))!;Commit(next,false);}
    public void Place(Guid id,double left,double top,double width,double height)
    {var next=Snapshot;var p=next.Pages.Single(p=>p.Id==id);p.Left=left;p.Top=top;p.Width=width;p.Height=height;Commit(next);}
    public void Host(Guid id,bool floating,WindowGeometry? geometry=null)
    {var next=Snapshot;var p=next.Pages.Single(p=>p.Id==id);p.Floating=floating;if(geometry is not null)p.Window=geometry;Commit(next);}
    public void Nest(Guid id,Guid? parent)
    {
        var next=Snapshot;var p=next.Pages.Single(p=>p.Id==id);
        if(parent is not null){var target=next.Pages.Single(p=>p.Id==parent);if(target.MicheId!=p.MicheId||target.DeletedAt is not null)throw new ArgumentException("Collect pages within the same Miche.");}
        p.ParentId=parent;Commit(next);
    }
    public void Delete(Guid id,bool deleted)
    {var next=Snapshot;next.Pages.Single(p=>p.Id==id).DeletedAt=deleted?DateTimeOffset.UtcNow:null;Commit(next);}
    public string ImportImage(byte[] data)
    {
        if(data.Length==0||data.Length>10000000)throw new ArgumentException("Choose a PNG or JPEG under 10 MB.");
        bool png=data.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10});bool jpg=data.Length>3&&data[0]==255&&data[1]==216&&data[2]==255;
        if(!png&&!jpg)throw new ArgumentException("Choose a PNG or JPEG image.");
        using(var bitmap=new Bitmap(new MemoryStream(data)))if((long)bitmap.PixelSize.Width*bitmap.PixelSize.Height>40000000)throw new ArgumentException("Choose an image under 40 megapixels.");
        var folder=Path.Combine(DirectoryPath,"page-images");Directory.CreateDirectory(folder);if(!OperatingSystem.IsWindows())File.SetUnixFileMode(folder,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        var name=Guid.NewGuid().ToString("N")+(png?".png":".jpg");File.WriteAllBytes(Path.Combine(folder,name),data);if(!OperatingSystem.IsWindows())File.SetUnixFileMode(Path.Combine(folder,name),UnixFileMode.UserRead|UnixFileMode.UserWrite);return name;
    }
    public string ImageData(string name)
    {
        if(!SafeImage(name))throw new ArgumentException("Invalid image reference.");var path=Path.Combine(DirectoryPath,"page-images",name);
        if(new FileInfo(path).LinkTarget is not null||new DirectoryInfo(Path.GetDirectoryName(path)!).LinkTarget is not null)throw new IOException("Image links are not supported.");
        return "data:image/"+(name.EndsWith(".png")?"png":"jpeg")+";base64,"+Convert.ToBase64String(File.ReadAllBytes(path));
    }
    private static bool SafeImage(string name)=>name.Length==36&&Guid.TryParseExact(name[..32],"N",out _)&&(name.EndsWith(".png")||name.EndsWith(".jpg"));
    private static void Validate(PageBook book)
    {
        if(book.Version!=1||book.Pages is null||book.Pages.Count>2000||book.Pages.Any(p=>p is null)||book.Pages.Select(p=>p.Id).Distinct().Count()!=book.Pages.Count)throw new InvalidDataException("Invalid pages file. The saved file is intact.");
        foreach(var p in book.Pages){
            if(p.Window is not null&&(!double.IsFinite(p.Window.Width)||!double.IsFinite(p.Window.Height)||p.Window.Width<280||p.Window.Height<220||p.Window.Width>8000||p.Window.Height>8000))throw new InvalidDataException("Invalid page window geometry.");
            if(p.Id==Guid.Empty||p.MicheId==Guid.Empty||p.Title is null||p.Title.Length>160||p.Blocks is null||p.Blocks.Count>1000||!double.IsFinite(p.Left)||!double.IsFinite(p.Top)||p.Left<0||p.Top<0||p.Left>20000||p.Top>20000||!double.IsFinite(p.Width)||!double.IsFinite(p.Height)||p.Width<280||p.Width>2000||p.Height<220||p.Height>2000)throw new InvalidDataException("Invalid page content or placement.");
            var seen=new HashSet<Guid>{p.Id};var ancestor=p;while(ancestor.ParentId is Guid parent){ancestor=book.Pages.SingleOrDefault(a=>a.Id==parent)??throw new InvalidDataException("Missing parent page.");if(!seen.Add(parent)||ancestor.MicheId!=p.MicheId)throw new InvalidDataException("Pages cannot contain themselves or cross Miches.");}
            if(p.Blocks.Select(b=>b?.Id).Distinct().Count()!=p.Blocks.Count)throw new InvalidDataException("Duplicate page block.");
            foreach(var b in p.Blocks){if(b is null||b.Id==Guid.Empty||b.Kind is not("text" or "image" or "table")||b.Runs is null||b.Cells is null||!double.IsFinite(b.ImageWidth)||b.ImageWidth<0||b.ImageWidth>2000)throw new InvalidDataException("Invalid page block.");ValidateRuns(b.Runs);if(b.Kind=="table")TableContent.Validate(b);if(b.Kind=="image"&&(b.FileName is null||!SafeImage(b.FileName)))throw new InvalidDataException("Invalid page image.");if(b.Kind!="image"&&b.FileName is not null)throw new InvalidDataException("Unexpected image reference.");if(b.Cells.Count>50||b.Cells.Any(row=>row is null||row.Count==0||row.Count>12)||b.Kind=="table"&&(b.Cells.Count==0||b.Cells.Any(row=>row.Count!=b.Cells[0].Count)))throw new InvalidDataException("Invalid table.");foreach(var row in b.Cells)foreach(var cell in row)ValidateRuns(cell);}
        }
    }
    private static void ValidateRuns(List<PageRun> runs)
    {if(runs is null||runs.Count>2000||runs.Any(r=>r is null||r.Text is null||r.Text.Length>100000||!double.IsFinite(r.Size)||r.Size<10||r.Size>96))throw new InvalidDataException("Invalid formatted text.");}
}
