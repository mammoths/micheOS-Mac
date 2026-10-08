using Miche.Mac.Models;

namespace Miche.Mac.Services;

public sealed partial class WorkspaceStore
{
    private static string ClipboardLabel(string text)
    {
        text=text.Trim();if(text.Length is <1 or >120||text.Any(char.IsControl))throw new ArgumentException("Use a name with 1–120 characters.");return text;
    }
    public static bool SafeClipboardUrl(string? url)=>Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http"&&string.IsNullOrEmpty(uri.UserInfo);
    public void ShowClipboard(Guid micheId,bool visible=true)
    {
        var next=Snapshot;if(!next.Index.Miches.Any(m=>m.Id==micheId))throw new ArgumentException("Open this Miche first.");
        next.Clipboard.VisibleIn.Remove(micheId);if(visible)next.Clipboard.VisibleIn.Add(micheId);Commit(next);
    }
    public void SetClipboardFloating(bool floating,WindowGeometry? window=null)
    {var next=Snapshot;next.Clipboard.Floating=floating;if(window is not null)next.Clipboard.Window=window;Commit(next);}
    public void RenameClipboard(string title)
    {var next=Snapshot;next.Clipboard.Title=ClipboardLabel(title);Commit(next);}
    public Guid AddClipboardLink(string label,string url)
    {
        url=url.Trim();if(!SafeClipboardUrl(url)||url.Length>4096)throw new ArgumentException("Use a full http:// or https:// link.");
        var next=Snapshot;var entry=new ClipboardEntry {Label=ClipboardLabel(label),Url=url};next.Clipboard.Entries.Add(entry);Commit(next);return entry.Id;
    }
    public Guid AddClipboardFile(string source,string? label=null)
    {
        var info=new FileInfo(source);if(!info.Exists)throw new IOException("That file is unavailable.");
        if(info.Length>50*1024*1024)throw new ArgumentException("Choose a file smaller than 50 MB.");
        var next=Snapshot;var entry=new ClipboardEntry {Label=ClipboardLabel(label??info.Name),OriginalName=info.Name};
        entry.FileName=entry.Id.ToString("N")+Path.GetExtension(info.Name);
        var shelfDirectory=Path.Combine(DirectoryPath,"clipboard-files");
        Directory.CreateDirectory(shelfDirectory);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(shelfDirectory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        File.Copy(info.FullName,ClipboardFilePath(entry.FileName),false);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(ClipboardFilePath(entry.FileName),UnixFileMode.UserRead|UnixFileMode.UserWrite);
        next.Clipboard.Entries.Add(entry);Commit(next);return entry.Id;
    }
    public string ClipboardFilePath(string name)
    {
        if(string.IsNullOrEmpty(name)||name!=Path.GetFileName(name)||name.Contains('/')||name.Contains('\\'))throw new ArgumentException("Invalid saved file.");
        return Path.Combine(DirectoryPath,"clipboard-files",name);
    }
    public void SetClipboardDeleted(Guid id,bool deleted)
    {var next=Snapshot;next.Clipboard.Entries.Single(e=>e.Id==id).DeletedAt=deleted?DateTimeOffset.UtcNow:null;Commit(next);}
    private static void ValidateClipboard(Workspace state,HashSet<Guid> micheIds)
    {
        var shelf=state.Clipboard;
        if(shelf?.Window is { } g&&(!double.IsFinite(g.Width)||!double.IsFinite(g.Height)||g.Width<240||g.Height<150||g.Width>10000||g.Height>10000))throw new InvalidDataException("Invalid Clipboard window geometry.");
        if(shelf is null||shelf.Title is null||shelf.Title.Trim().Length is <1 or >120||shelf.Title.Any(char.IsControl)||shelf.Entries is null||shelf.VisibleIn is null||
            shelf.VisibleIn.Distinct().Count()!=shelf.VisibleIn.Count||shelf.VisibleIn.Any(id=>!micheIds.Contains(id))||
            shelf.Entries.Any(e=>e is null||e.Id==Guid.Empty||string.IsNullOrWhiteSpace(e.Label)||e.Label.Length>120||e.Label.Any(char.IsControl)||
                (e.Url is null)==(e.FileName is null)||e.Url is not null&&(!SafeClipboardUrl(e.Url)||e.Url.Length>4096)||
                e.FileName is not null&&(e.FileName!=Path.GetFileName(e.FileName)||e.FileName.Contains('/')||e.FileName.Contains('\\')||string.IsNullOrWhiteSpace(e.OriginalName)))||
            shelf.Entries.Select(e=>e.Id).Distinct().Count()!=shelf.Entries.Count)
            throw new InvalidDataException("Invalid Clipboard data. Saved files are unchanged.");
    }
}
