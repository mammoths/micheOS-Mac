namespace Miche.Mac.Models;

public sealed class ClipboardShelf
{
    public bool Floating {get;set;}
    public WindowGeometry? Window {get;set;}
    public string Title {get;set;}="CLIPBOARD";
    public List<Guid> VisibleIn {get;set;}=new();
    public List<ClipboardEntry> Entries {get;set;}=new();
}
public sealed class ClipboardEntry
{
    public Guid Id {get;set;}=Guid.NewGuid();
    public string Label {get;set;}="";
    public string? Url {get;set;}
    public string? FileName {get;set;}
    public string? OriginalName {get;set;}
    public DateTimeOffset? DeletedAt {get;set;}
}
