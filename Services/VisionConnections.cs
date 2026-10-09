using Miche.Mac.Models;

namespace Miche.Mac.Services;

public sealed partial class WorkspaceStore
{
    private static void ValidateConnections(VisionItem? item,int version)
    {
        if(item is null||item.DownstreamIds is null||item.DownstreamIds.Count>1000||item.DownstreamIds.Distinct().Count()!=item.DownstreamIds.Count||
           item.DownstreamIds.Any(id=>id==Guid.Empty||id==item.Id)||version<11&&item.DownstreamIds.Count!=0)
            throw new InvalidDataException("Invalid thought connections. Your saved file is intact.");
    }
    private static Dictionary<Guid,Guid> NewVisionIds(IEnumerable<VisionItem> items)=>items.ToDictionary(i=>i.Id,_=>Guid.NewGuid());
    private static void RemapConnections(VisionItem item,IReadOnlyDictionary<Guid,Guid> ids)
    {item.DownstreamIds=item.DownstreamIds.Where(ids.ContainsKey).Select(id=>ids[id]).Distinct().ToList();}
    public void ConnectVisionItems(Guid micheId, Guid sourceId, Guid targetId, Guid? artifactId = null) =>
        SetVisionConnection(micheId, sourceId, targetId, true, artifactId);

    public void DisconnectVisionItems(Guid micheId, Guid sourceId, Guid targetId, Guid? artifactId = null) =>
        SetVisionConnection(micheId, sourceId, targetId, false, artifactId);

    public void SetVisionConnection(Guid micheId, Guid sourceId, Guid targetId, bool connected, Guid? artifactId = null)
    {
        var next = Snapshot;
        var items = VisionItemsFor(next, micheId, artifactId);
        if (!ChangeConnection(items, sourceId, targetId, connected)) return;
        TouchArtifact(next, artifactId);
        Commit(next);
    }

    private static bool ChangeConnection(IEnumerable<VisionItem> items, Guid sourceId, Guid targetId, bool connected)
    {
        if (sourceId == Guid.Empty || targetId == Guid.Empty || sourceId == targetId)
            throw new ArgumentException("Connect two different objects.");
        var live = items.Where(i => i.DeletedAt is null).ToArray();
        var source = live.SingleOrDefault(i => i.Id == sourceId);
        if (source is null || !live.Any(i => i.Id == targetId))
            throw new ArgumentException("Connect two objects on this canvas. Restore a deleted object first.");
        if (connected)
        {
            if (source.DownstreamIds.Contains(targetId)) return false;
            source.DownstreamIds.Add(targetId);
            return true;
        }
        return source.DownstreamIds.Remove(targetId);
    }
}
