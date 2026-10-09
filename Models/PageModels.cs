namespace Miche.Mac.Models;

// Structured content, never executable HTML. Pages are independent of legacy widgets.
public sealed class PageBook { public int Version { get; set; } = 1; public List<PageNote> Pages { get; set; } = new(); }
public sealed class PageNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MicheId { get; set; }
    public Guid? ParentId { get; set; }
    public string Title { get; set; } = "";
    public List<PageBlock> Blocks { get; set; } = new();
    public double Left { get; set; } = 24;
    public double Top { get; set; } = 24;
    public double Width { get; set; } = 440;
    public double Height { get; set; } = 360;
    public bool Floating { get; set; }
    public WindowGeometry? Window { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
public sealed class PageBlock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "text";
    public List<PageRun> Runs { get; set; } = new();
    public bool Boxed { get; set; }
    public double ImageWidth { get; set; }
    public string? FileName { get; set; }
    public Dictionary<string,string> CellColors { get; set; } = new();
    public List<double> ColumnWidths { get; set; } = new();
    public List<double> RowHeights { get; set; } = new();
    public List<List<List<PageRun>>> Cells { get; set; } = new();
}
public sealed class PageRun
{
    public string Text { get; set; } = "";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Boxed { get; set; }
    public double Size { get; set; } = 18;
}
