namespace Miche.Mac.Models;

// One canonical object collection. A date is an assignment, not a copied page.
public sealed class CalendarBook
{
    public List<CalendarItem> Items { get; set; } = new();
    public List<Guid> VisibleIn { get; set; } = new();
    public string View { get; set; } = "month";
    public WindowGeometry? Window { get; set; }
}

public sealed class CalendarItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerMicheId { get; set; }
    public string Month { get; set; } = "";
    public string? Date { get; set; }
    public VisionItem Content { get; set; } = new();
}
