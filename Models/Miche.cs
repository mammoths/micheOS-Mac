namespace Miche.Mac.Models;

// Adapted from Windows src/Models/Miche.cs. IDs and metadata retain their meaning.
public sealed class Miche
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Guid? ParentMicheId { get; set; }
    public Guid? CollabId { get; set; }
    public string? Purpose { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MicheIndex
{
    public int Version { get; set; } = 1;
    public Guid RootMicheId { get; set; }
    public Guid ActiveMicheId { get; set; }
    public List<Miche> Miches { get; set; } = new();
}

public sealed class Capture
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public Guid OriginMicheId { get; set; }
    public string OriginMicheName { get; set; } = "";
    public string OriginTheme { get; set; } = "Archaeology";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? OwnerMicheId { get; set; }
    public Guid? ArchiveBatchId { get; set; }
}

public sealed class DumpArchiveBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ScopeMicheId { get; set; }
    public string ScopeTitle { get; set; } = "all Miches";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Guid> CaptureIds { get; set; } = new();
}

public sealed class ArchivedMiche
{
    public Miche Miche { get; set; } = new();
    public DateTimeOffset DeletedAt { get; set; } = DateTimeOffset.UtcNow;
}

// Mac-only envelope; no compatibility or Windows import is implied.
public sealed class Workspace
{
    public int Version { get; set; } = 8;
    public ClipboardShelf Clipboard { get; set; } = new();
    public MicheIndex Index { get; set; } = new();
    public List<ArchivedMiche> Trash { get; set; } = new();
    public List<Capture> RootDump { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<Guid>? DumpSpaces { get; set; }
    public List<WidgetInstance> Widgets { get; set; } = new();
    public List<WidgetPlacement> Placements { get; set; } = new();
    public List<VisionBoard> VisionBoards { get; set; } = new();
    public List<DumpArchiveBatch> DumpArchives { get; set; } = new();
    public List<VisionArtifact> VisionArtifacts { get; set; } = new();
    public List<VisionReset> VisionResets { get; set; } = new();
}

public sealed class WidgetInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MicheId { get; set; }
    public int DefinitionId { get; set; } = 1;
    public string DataKey { get; set; } = "root-dump";
    public string Theme { get; set; } = "Archaeology";
    public EditorState Editor { get; set; } = new();
    public EditorState LocalEditor { get; set; } = new();
    public string CaptureScope { get; set; } = "canonical";
}

public sealed class EditorState
{
    public string Text { get; set; } = "";
    public int CaretIndex { get; set; }
    public int SelectionStart { get; set; }
    public int SelectionEnd { get; set; }
}

public sealed class WidgetPlacement
{
    public Guid WidgetInstanceId { get; set; }
    public string Mode { get; set; } = "docked";
    public string? BeforeRemoval { get; set; }
    public double PosX { get; set; } = -1;
    public double PosY { get; set; } = -1;
    public double WidthFraction { get; set; } = .6;
    public double Height { get; set; } = 312;
    public WindowGeometry? Window { get; set; }
}

public sealed class WindowGeometry
{
    public int X { get; set; }
    public int Y { get; set; }
    public double Width { get; set; } = 540;
    public double Height { get; set; } = 340;
}

public sealed class VisionBoard
{
    public Guid MicheId { get; set; }
    public List<VisionItem> Items { get; set; } = new();
}
public sealed class VisionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "text";
    public PageBlock? Table { get; set; }
    public bool RoundedFrame { get; set; }
    public string Text { get; set; } = "";
    public string? FileName { get; set; }
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; } = 280;
    public double Height { get; set; } = 80;
    public double Rotation { get; set; }
    public double FontSize { get; set; } = 22;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class VisionArtifact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MicheId { get; set; }
    public string Title { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
    public List<VisionItem> Items { get; set; } = new();
}
public sealed class VisionReset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MicheId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? RecoveredArtifactId { get; set; }
    public List<VisionItem> Items { get; set; } = new();
}
