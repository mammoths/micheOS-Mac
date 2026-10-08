using System.Text.Json;
using Miche.Mac.Models;

namespace Miche.Mac.Services;

public sealed partial class WorkspaceStore : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly FileStream _lock;
    private Workspace _state;
    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "workspace.json");
    public Workspace Snapshot => Clone(_state);
    public event Action? Changed;
    public string? LastNotificationWarning { get; private set; }
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "Application Support", "Miche.Mac");

    public WorkspaceStore(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
        Directory.CreateDirectory(DirectoryPath);
        _lock = new FileStream(Path.Combine(DirectoryPath, "workspace.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        try
        {
            if (File.Exists(FilePath))
            {
                _state = JsonSerializer.Deserialize<Workspace>(File.ReadAllText(FilePath), Json)
                    ?? throw new InvalidDataException("The saved workspace is empty.");
                Validate(_state);
                if (_state.Version == 1)
                {
                    var next = Clone(_state);
                    foreach (var id in next.DumpSpaces!)
                    {
                        var widget = EnsureDump(next, id);
                        if (next.Trash.Any(t => t.Miche.Id == id))
                        {
                            var placement = next.Placements.Single(p => p.WidgetInstanceId == widget.Id);
                            placement.Mode = "hidden";
                            placement.BeforeRemoval = "docked";
                        }
                    }
                    next.Version = 2;
                    next.DumpSpaces = null;
                    var backups = Path.Combine(DirectoryPath, "backups");
                    Directory.CreateDirectory(backups);
                    File.Copy(FilePath, Path.Combine(backups, "before-v2-" + Guid.NewGuid().ToString("N") + ".json"));
                    Commit(next);
                }
                if (_state.Version == 2)
                {
                    var next = Clone(_state); next.Version = 3;
                    var backups = Path.Combine(DirectoryPath, "backups");
                    Directory.CreateDirectory(backups);
                    File.Copy(FilePath, Path.Combine(backups, "before-v3-" + Guid.NewGuid().ToString("N") + ".json"));
                    Commit(next);
                }
                if (_state.Version == 3)
                {
                    var next = Clone(_state); next.Version = 4;
                    var backups = Path.Combine(DirectoryPath, "backups"); Directory.CreateDirectory(backups);
                    File.Copy(FilePath, Path.Combine(backups, "before-v4-" + Guid.NewGuid().ToString("N") + ".json"));
                    Commit(next);
                }
                if (_state.Version == 4)
                {
                    var next = Clone(_state); next.Version = 5;
                    var backups = Path.Combine(DirectoryPath,"backups"); Directory.CreateDirectory(backups);
                    File.Copy(FilePath,Path.Combine(backups,"before-v5-"+Guid.NewGuid().ToString("N")+".json"));
                    Commit(next);
                }
                if (_state.Version == 5)
                {
                    var next = Clone(_state); next.Version = 6;
                    var backups = Path.Combine(DirectoryPath,"backups"); Directory.CreateDirectory(backups);
                    File.Copy(FilePath,Path.Combine(backups,"before-v6-"+Guid.NewGuid().ToString("N")+".json"));
                    Commit(next);
                }
                if (_state.Version == 6)
                {
                    var next=Clone(_state);next.Version=7;
                    var backups=Path.Combine(DirectoryPath,"backups");Directory.CreateDirectory(backups);
                    File.Copy(FilePath,Path.Combine(backups,"before-v7-"+Guid.NewGuid().ToString("N")+".json"));Commit(next,false);
                }

            }
            else
            {
                var home = new Models.Miche { Name = "home" };
                _state = new Workspace { Index = new MicheIndex {
                    RootMicheId = home.Id, ActiveMicheId = home.Id, Miches = new() { home } } };
                Commit(_state);
            }
        }
        catch { _lock.Dispose(); throw; }
    }

    public Guid Create(string name)
    {
        var next = Snapshot;
        var miche = new Models.Miche { Name = NameFor(next, name) };
        next.Index.Miches.Add(miche);
        next.Index.ActiveMicheId = miche.Id;
        Commit(next);
        return miche.Id;
    }

    public void Activate(Guid id)
    {
        var next = Snapshot;
        if (!next.Index.Miches.Any(m => m.Id == id)) throw new ArgumentException("That niche no longer exists.");
        next.Index.ActiveMicheId = id;
        Commit(next);
    }

    public void Rename(Guid id, string name)
    {
        var next = Snapshot;
        var miche = next.Index.Miches.Single(m => m.Id == id);
        miche.Name = NameFor(next, name, id);
        miche.UpdatedAt = DateTimeOffset.UtcNow;
        Commit(next);
    }

    public void Delete(Guid id)
    {
        var next = Snapshot;
        if (id == next.Index.RootMicheId) throw new ArgumentException("Home stays as your root niche.");
        var miche = next.Index.Miches.Single(m => m.Id == id);
        next.Index.Miches.Remove(miche);
        next.Trash.Add(new ArchivedMiche { Miche = miche });
        foreach (var widget in next.Widgets.Where(w => w.MicheId == id))
        {
            var placement = next.Placements.Single(p => p.WidgetInstanceId == widget.Id);
            placement.BeforeRemoval = placement.Mode;
            placement.Mode = "hidden";
        }
        if (next.Index.ActiveMicheId == id) next.Index.ActiveMicheId = next.Index.RootMicheId;
        Commit(next);
    }

    public void Restore(Guid id)
    {
        var next = Snapshot;
        var archived = next.Trash.Single(t => t.Miche.Id == id);
        // A new niche may have taken its former name while it was in Recently deleted.
        var name = archived.Miche.Name;
        for (var suffix = 2; next.Index.Miches.Any(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); suffix++)
            name = archived.Miche.Name[..Math.Min(archived.Miche.Name.Length, 65)] + " · restored " + suffix;
        archived.Miche.Name = name;
        archived.Miche.UpdatedAt = DateTimeOffset.UtcNow;
        next.Trash.Remove(archived);
        next.Index.Miches.Add(archived.Miche);
        next.Index.ActiveMicheId = id;
        foreach (var widget in next.Widgets.Where(w => w.MicheId == id))
        {
            var placement = next.Placements.Single(p => p.WidgetInstanceId == widget.Id);
            placement.Mode = placement.BeforeRemoval ?? "hidden";
            placement.BeforeRemoval = null;
        }
        Commit(next);
    }

    public void ShowDump(bool show, bool? local = null, EditorState? outgoing = null)
    {
        var next = Snapshot;
        var existed = next.Widgets.Any(w => w.MicheId == next.Index.ActiveMicheId && w.DefinitionId == 1);
        var widget = EnsureDump(next, next.Index.ActiveMicheId);
        if (local.HasValue)
        {
            if (existed && outgoing is null) throw new ArgumentException("Save the outgoing Dump editor before choosing a scope.");
            if (outgoing is not null) SetEditor(widget, outgoing);
            widget.CaptureScope = local.Value ? "local" : "canonical";
        }
        var placement = next.Placements.Single(p => p.WidgetInstanceId == widget.Id);
        if (!show) placement.Mode = "hidden";
        else if (placement.Mode != "floating") placement.Mode = "docked";
        Commit(next);
    }

    private static WidgetInstance EnsureDump(Workspace next, Guid micheId)
    {
        var widget = next.Widgets.SingleOrDefault(w => w.MicheId == micheId && w.DefinitionId == 1);
        if (widget is not null) return widget;
        widget = new WidgetInstance { MicheId = micheId };
        next.Widgets.Add(widget);
        next.Placements.Add(new WidgetPlacement { WidgetInstanceId = widget.Id });
        return widget;
    }

    public void SetHost(Guid widgetId, string mode, EditorState editor, WindowGeometry? geometry = null, bool activateOrigin = false)
    {
        var next = Snapshot;
        var widget = next.Widgets.Single(w => w.Id == widgetId);
        if (!next.Index.Miches.Any(m => m.Id == widget.MicheId)) throw new ArgumentException("Restore this niche before opening its widget.");
        var placement = next.Placements.Single(p => p.WidgetInstanceId == widgetId);
        SetEditor(widget, editor);
        placement.Mode = mode;
        if (activateOrigin) next.Index.ActiveMicheId = widget.MicheId;
        if (geometry is not null) placement.Window = geometry;
        Commit(next);
    }

    public void SaveEditors(IReadOnlyDictionary<Guid, EditorState> editors,
        IReadOnlyDictionary<Guid, WindowGeometry>? windows = null)
    {
        var next = Snapshot;
        foreach (var widget in next.Widgets)
            if (editors.TryGetValue(widget.Id, out var editor)) SetEditor(widget, editor);
        if (windows is not null)
            foreach (var placement in next.Placements)
                if (windows.TryGetValue(placement.WidgetInstanceId, out var window)) placement.Window = window;
        Commit(next, notify: false);
    }

    public void SetDeskPlacement(Guid widgetId, double x, double y, double widthFraction, double height)
    {
        var next = Snapshot;
        var placement = next.Placements.Single(p => p.WidgetInstanceId == widgetId);
        if (placement.Mode != "docked") throw new ArgumentException("Return this widget to its desk before moving it.");
        placement.PosX = x; placement.PosY = y;
        placement.WidthFraction = widthFraction; placement.Height = height;
        Commit(next);
    }

    public Guid Capture(string text, Guid? originMicheId = null, Guid? widgetId = null)
    {
        text = text.Trim();
        if (text.Length == 0) throw new ArgumentException("Write something to capture first.");
        if (text.Length > 20000) throw new ArgumentException("Keep each capture under 20,000 characters.");
        var next = Snapshot;
        var origin = next.Index.Miches.Single(m => m.Id == (originMicheId ?? next.Index.ActiveMicheId));
        var widget = widgetId.HasValue ? next.Widgets.Single(w => w.Id == widgetId && w.MicheId == origin.Id) : EnsureDump(next, origin.Id);
        var capture = new Capture { Text = text, OriginMicheId = origin.Id, OriginMicheName = origin.Name,
            OwnerMicheId = widgetId.HasValue && widget.CaptureScope == "local" ? origin.Id : null };
        next.RootDump.Add(capture);
        // Standalone capture doesn't consume an editor draft. Only a submission
        // from an explicit widget clears that widget's currently selected draft.
        if (widgetId.HasValue) SetEditor(widget, new EditorState());
        var placement = next.Placements.Single(p => p.WidgetInstanceId == widget.Id);
        if (placement.Mode == "hidden") placement.Mode = "docked";
        Commit(next);
        return capture.Id;
    }

    public void SetCaptureDeleted(Guid id, bool deleted)
    {
        var next = Snapshot;
        next.RootDump.Single(n => n.Id == id).DeletedAt = deleted ? DateTimeOffset.UtcNow : null;
        Commit(next);
    }

    private static string NameFor(Workspace state, string name, Guid? except = null)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
            throw new ArgumentException("Use a name with 1–80 characters.");
        if (state.Index.Miches.Any(m => m.Id != except && m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A niche already has that name.");
        return name;
    }

    private void Commit(Workspace next, bool notify = true)
    {
        Validate(next);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, next, Json);
                file.Flush(true);
            }
            if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bak", true);
            File.Move(temp, FilePath, true);
            _state = Clone(next);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        LastNotificationWarning=null;
        if (notify && Changed is { } observers)
            foreach(Action observer in observers.GetInvocationList())
                try {observer();}
                catch(Exception) {LastNotificationWarning="Saved on this Mac. A view could not refresh; reopen it to see the saved workspace.";}
    }

    private static Workspace Clone(Workspace state) =>
        JsonSerializer.Deserialize<Workspace>(JsonSerializer.Serialize(state, Json), Json)!;

    private static void Validate(Workspace state)
    {
        var index = state.Index;
        if (state.Version is not (1 or 2 or 3 or 4 or 5 or 6 or 7) || index is null || index.Version != 1 || index.Miches is null ||
            index.Miches.Count == 0 || state.Trash is null || state.RootDump is null ||
            index.Miches.Any(m => m is null || m.Id == Guid.Empty || string.IsNullOrWhiteSpace(m.Name) ||
                m.Name.Length > 80 || m.Name.Any(char.IsControl)) ||
            index.Miches.Select(m => m.Id).Distinct().Count() != index.Miches.Count ||
            index.Miches.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != index.Miches.Count ||
            !index.Miches.Any(m => m.Id == index.RootMicheId) || !index.Miches.Any(m => m.Id == index.ActiveMicheId) ||
            state.Trash.Any(t => t is null || t.Miche is null || t.Miche.Id == Guid.Empty || string.IsNullOrWhiteSpace(t.Miche.Name)) ||
            index.Miches.Select(m => m.Id).Concat(state.Trash.Select(t => t.Miche.Id)).Distinct().Count() != index.Miches.Count + state.Trash.Count ||
            state.RootDump.Any(n => n is null || n.Id == Guid.Empty || string.IsNullOrWhiteSpace(n.Text) ||
                n.OriginMicheId == Guid.Empty || string.IsNullOrWhiteSpace(n.OriginMicheName)) ||
            state.RootDump.Select(n => n.Id).Distinct().Count() != state.RootDump.Count)
            throw new InvalidDataException("Miche could not safely read this workspace. The saved file has not been replaced.");
        var micheIds = index.Miches.Select(m => m.Id).Concat(state.Trash.Select(t => t.Miche.Id)).ToHashSet();
        ValidateClipboard(state,micheIds);
        if (state.Version == 1)
        {
            if (state.DumpSpaces is null || state.DumpSpaces.Distinct().Count() != state.DumpSpaces.Count || state.DumpSpaces.Any(id => !micheIds.Contains(id)))
                throw new InvalidDataException("Invalid version-1 Dump placements. Saved files are unchanged.");
            return;
        }
        if (state.Widgets is null || state.Placements is null || state.DumpSpaces is not null ||
            state.Widgets.Any(w => w is null || w.Id == Guid.Empty || !micheIds.Contains(w.MicheId) || w.DefinitionId != 1 ||
                w.DataKey != "root-dump" || w.Theme != "Archaeology" || w.Editor is null || w.Editor.Text is null ||
                w.Editor.Text.Length > 20000 || w.Editor.CaretIndex < 0 || w.Editor.CaretIndex > w.Editor.Text.Length ||
                w.Editor.SelectionStart < 0 || w.Editor.SelectionStart > w.Editor.Text.Length || w.Editor.SelectionEnd < 0 || w.Editor.SelectionEnd > w.Editor.Text.Length) ||
            state.Widgets.Select(w => w.Id).Distinct().Count() != state.Widgets.Count ||
            state.Widgets.Select(w => w.MicheId).Distinct().Count() != state.Widgets.Count ||
            state.Placements.Count != state.Widgets.Count ||
            state.Placements.Select(p => p?.WidgetInstanceId).Distinct().Count() != state.Placements.Count ||
            state.Placements.Any(p => p is null || !state.Widgets.Any(w => w.Id == p.WidgetInstanceId) ||
                p.Mode is not ("hidden" or "docked" or "floating") || p.BeforeRemoval is not (null or "hidden" or "docked" or "floating") ||
                !double.IsFinite(p.PosX) || p.PosX < -1 || p.PosX > 1 || !double.IsFinite(p.PosY) || p.PosY < -1 || p.PosY > 10000 ||
                !double.IsFinite(p.WidthFraction) || p.WidthFraction <= 0 || p.WidthFraction > 1 ||
                !double.IsFinite(p.Height) || p.Height < 220 || p.Height > 3000 ||
                p.Window is not null && (!double.IsFinite(p.Window.Width) || !double.IsFinite(p.Window.Height) || p.Window.Width <= 0 || p.Window.Height <= 0)))
            throw new InvalidDataException("Invalid widget identity, editor or placement. Saved files are unchanged.");
        ValidateVision(state, micheIds);
        ValidateDumpScopes(state, micheIds);
    }

    public void Dispose() => _lock.Dispose();
}
