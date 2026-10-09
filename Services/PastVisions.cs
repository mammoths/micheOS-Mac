using Miche.Mac.Models;

namespace Miche.Mac.Services;

public sealed partial class WorkspaceStore
{
    private static string VisionTitle(string title)
    {
        title=title.Trim();
        if(title.Length is <1 or >80 || title.Any(char.IsControl)) throw new ArgumentException("Use a title with 1–80 characters.");
        return title;
    }
    private static List<VisionItem> SnapshotObjects(IEnumerable<VisionItem> items)
    {
        var originals=items.ToArray();var ids=NewVisionIds(originals);
        return originals.Select(i=>{var copy=CopyVision(i);copy.Id=ids[i.Id];RemapConnections(copy,ids);return copy;}).ToList();
    }
    private static VisionArtifact ArtifactFor(Workspace next,Guid micheId,Guid id)
    {
        if(!next.Index.Miches.Any(m=>m.Id==micheId)) throw new ArgumentException("Restore this Miche before editing its past Vision.");
        return next.VisionArtifacts.SingleOrDefault(a=>a.Id==id && a.MicheId==micheId && a.DeletedAt is null)
            ?? throw new ArgumentException("Restore this past Vision before editing it.");
    }
    private static List<VisionItem> VisionItemsFor(Workspace next,Guid micheId,Guid? artifactId) => artifactId is { } id
        ? ArtifactFor(next,micheId,id).Items : BoardFor(next,micheId).Items;
    private static void TouchArtifact(Workspace next,Guid? id)
    { if(id is { } artifact) next.VisionArtifacts.Single(a=>a.Id==artifact).UpdatedAt=DateTimeOffset.UtcNow; }

    // Text must be committed before calling this transaction. A commit failure in
    // the view blocks reset; this save publishes snapshot+recovery+clear together.
    public Guid? StartFreshVision(Guid micheId,string? savedTitle=null)
    {
        var title=savedTitle is null ? null : VisionTitle(savedTitle);
        var next=Snapshot; var board=BoardFor(next,micheId);
        if(board.Items.Count==0) throw new ArgumentException("This Vision is already empty.");
        VisionArtifact? artifact=null;
        if(title is not null)
        {
            artifact=new VisionArtifact {MicheId=micheId,Title=title,Items=SnapshotObjects(board.Items.Where(i=>i.DeletedAt is null))};
            next.VisionArtifacts.Add(artifact);
        }
        next.VisionResets.Add(new VisionReset {MicheId=micheId,Items=board.Items});
        board.Items=new(); Commit(next); return artifact?.Id;
    }
    public void RenameVisionArtifact(Guid micheId,Guid id,string title)
    { var next=Snapshot; var artifact=ArtifactFor(next,micheId,id); artifact.Title=VisionTitle(title); artifact.UpdatedAt=DateTimeOffset.UtcNow; Commit(next); }
    public void SetVisionArtifactDeleted(Guid micheId,Guid id,bool deleted)
    {
        var next=Snapshot;
        if(!next.Index.Miches.Any(m=>m.Id==micheId)) throw new ArgumentException("Restore this Miche first.");
        var artifact=next.VisionArtifacts.Single(a=>a.Id==id && a.MicheId==micheId);
        artifact.DeletedAt=deleted ? DateTimeOffset.UtcNow : null; artifact.UpdatedAt=DateTimeOffset.UtcNow; Commit(next);
    }
    public Guid RecoverVisionReset(Guid micheId,Guid resetId)
    {
        var next=Snapshot;
        if(!next.Index.Miches.Any(m=>m.Id==micheId)) throw new ArgumentException("Restore this Miche first.");
        var reset=next.VisionResets.Single(r=>r.Id==resetId && r.MicheId==micheId);
        if(reset.RecoveredArtifactId is { } existing)
        {
            next.VisionArtifacts.Single(a=>a.Id==existing).DeletedAt=null; Commit(next); return existing;
        }
        // Recovery opens a distinct artifact. Newer current work is never replaced.
        var artifact=new VisionArtifact {MicheId=micheId,Title="recovered · "+reset.CreatedAt.ToLocalTime().ToString("MMM d, h:mm tt"),Items=SnapshotObjects(reset.Items)};
        next.VisionArtifacts.Add(artifact); reset.RecoveredArtifactId=artifact.Id; Commit(next); return artifact.Id;
    }
    private static IEnumerable<VisionItem> AllVisionItems(Workspace next) => next.VisionBoards.SelectMany(b=>b.Items)
        .Concat(next.VisionArtifacts.SelectMany(a=>a.Items)).Concat(next.VisionResets.SelectMany(r=>r.Items));
}
