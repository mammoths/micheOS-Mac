using Miche.Mac.Models;

namespace Miche.Mac.Services;

public sealed partial class WorkspaceStore
{
    public static EditorState EditorFor(WidgetInstance widget) => widget.CaptureScope == "local" ? widget.LocalEditor : widget.Editor;
    private static void SetEditor(WidgetInstance widget, EditorState editor)
    { if (widget.CaptureScope == "local") widget.LocalEditor = editor; else widget.Editor = editor; }

    // Origin is provenance. Ownership alone controls local views; canonical is the
    // whole physical collection. Deleted/archive rows never become a second copy.
    public IReadOnlyList<Capture> DumpEntries(Guid? owner = null, bool archived = false) => Snapshot.RootDump
        .Where(n => n.DeletedAt is null && (owner is null || n.OwnerMicheId == owner) && (n.ArchiveBatchId is not null) == archived)
        .OrderByDescending(n => n.CreatedAt).ToArray();

    public void SetDumpScope(Guid widgetId, bool local, EditorState outgoing)
    {
        var next = Snapshot; var widget = next.Widgets.Single(w => w.Id == widgetId);
        if (!next.Index.Miches.Any(m => m.Id == widget.MicheId)) throw new ArgumentException("Restore this Miche before editing its Dump.");
        SetEditor(widget,outgoing); widget.CaptureScope = local ? "local" : "canonical"; Commit(next);
    }
    public Guid FlushDump(Guid? owner = null)
    {
        var next = Snapshot;
        var title = owner.HasValue ? next.Index.Miches.SingleOrDefault(m => m.Id == owner)?.Name
            ?? next.Trash.SingleOrDefault(t => t.Miche.Id == owner)?.Miche.Name
            ?? throw new ArgumentException("That Dump scope no longer exists.") : "all Miches";
        var entries = next.RootDump.Where(n => n.DeletedAt is null && n.ArchiveBatchId is null && (owner is null || n.OwnerMicheId == owner)).ToArray();
        if (entries.Length == 0) throw new ArgumentException("Nothing active to flush in this scope.");
        var batch = new DumpArchiveBatch { ScopeMicheId = owner, ScopeTitle = title, CaptureIds = entries.Select(n => n.Id).ToList() };
        foreach (var entry in entries) entry.ArchiveBatchId = batch.Id;
        next.DumpArchives.Add(batch); Commit(next); return batch.Id;
    }
    public void RestoreDumpBatch(Guid id)
    {
        var next = Snapshot; var batch = next.DumpArchives.Single(b => b.Id == id);
        foreach (var entry in next.RootDump.Where(n => batch.CaptureIds.Contains(n.Id) && n.ArchiveBatchId == id && n.DeletedAt is null))
            entry.ArchiveBatchId = null;
        Commit(next); // old/double restores cannot steal entries from a later batch
    }
    private static bool ValidEditor(EditorState? e) => e is not null && e.Text is not null && e.Text.Length <= 20000 &&
        e.CaretIndex >= 0 && e.CaretIndex <= e.Text.Length && e.SelectionStart >= 0 && e.SelectionStart <= e.Text.Length && e.SelectionEnd >= 0 && e.SelectionEnd <= e.Text.Length;
    private static void ValidateDumpScopes(Workspace state, HashSet<Guid> micheIds)
    {
        if (state.DumpArchives is null || state.Widgets.Any(w => w.CaptureScope is not ("canonical" or "local") || !ValidEditor(w.LocalEditor)) ||
            state.Version < 4 && (state.DumpArchives.Count != 0 || state.RootDump.Any(n => n.OwnerMicheId is not null || n.ArchiveBatchId is not null) ||
                state.Widgets.Any(w => w.CaptureScope != "canonical" || w.LocalEditor.Text.Length != 0)) ||
            state.RootDump.Any(n => n.OwnerMicheId is { } owner && !micheIds.Contains(owner)) ||
            state.DumpArchives.Any(b => b is null || b.Id == Guid.Empty || b.ScopeMicheId is { } scope && !micheIds.Contains(scope) ||
                string.IsNullOrWhiteSpace(b.ScopeTitle) || b.CaptureIds is null || b.CaptureIds.Count == 0 ||
                b.CaptureIds.Distinct().Count() != b.CaptureIds.Count || b.CaptureIds.Any(id => !state.RootDump.Any(n => n.Id == id && (b.ScopeMicheId is null || n.OwnerMicheId == b.ScopeMicheId)))) ||
            state.DumpArchives.Select(b => b.Id).Distinct().Count() != state.DumpArchives.Count ||
            state.RootDump.Any(n => n.ArchiveBatchId is { } batch && !state.DumpArchives.Any(b => b.Id == batch && b.CaptureIds.Contains(n.Id))))
            throw new InvalidDataException("Invalid Dump scope, draft or archive. Saved files are unchanged.");
    }
}
