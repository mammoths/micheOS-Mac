using Miche.Mac.Models;

namespace Miche.Mac.Services;

public sealed partial class WorkspaceStore
{
    public Guid[] PasteVisionItems(Guid micheId,Guid workspaceId,Guid sourceMicheId,IEnumerable<VisionItem> copied,Guid? artifactId=null)
    {
        var originals=copied.ToArray();if(originals.Length is <1 or >1000||originals.Any(i=>i is null))throw new ArgumentException("Select up to 1000 Vision objects.");
        var next=Snapshot;var target=VisionItemsFor(next,micheId,artifactId);
        var pasted=originals.Select(CopyVision).ToArray();
        var dx=Math.Min(24,10000-pasted.Max(i=>i.Left));var dy=Math.Min(24,10000-pasted.Max(i=>i.Top));
        var owned=next.VisionBoards.Where(b=>b.MicheId==sourceMicheId).SelectMany(b=>b.Items)
            .Concat(next.VisionArtifacts.Where(a=>a.MicheId==sourceMicheId).SelectMany(a=>a.Items))
            .Concat(next.VisionResets.Where(r=>r.MicheId==sourceMicheId).SelectMany(r=>r.Items)).ToArray();
        var assetCopies=new List<(string Source,string Target)>();
        foreach(var item in pasted)
        {
            item.Id=Guid.NewGuid();item.DeletedAt=null;item.CreatedAt=DateTimeOffset.UtcNow;item.Left+=dx;item.Top+=dy;
            if(item.Kind=="image")
            {
                if(workspaceId!=next.Index.RootMicheId||!owned.Any(i=>i.Kind=="image"&&i.FileName==item.FileName))throw new ArgumentException("Copy images from a Vision in this Mac workspace.");
                var source=VisionAssetPath(sourceMicheId,item.FileName!);
                item.FileName=micheId.ToString("N")+"/"+Guid.NewGuid().ToString("N")+Path.GetExtension(source);
                assetCopies.Add((source,VisionAssetPath(micheId,item.FileName)));
            }
            target.Add(item);
        }
        Validate(next); // Reject the entire paste before copying any assets.
        var created=new List<string>();
        try
        {
            foreach(var pair in assetCopies){Directory.CreateDirectory(Path.GetDirectoryName(pair.Target)!);File.Copy(pair.Source,pair.Target,false);created.Add(pair.Target);}
            TouchArtifact(next,artifactId);Commit(next);return pasted.Select(i=>i.Id).ToArray();
        }
        catch
        {
            foreach(var path in created)
                if(!AllVisionItems(_state).Any(i=>i.FileName is not null&&Path.Combine(DirectoryPath,"vision",i.FileName)==path)&&File.Exists(path))File.Delete(path);
            throw;
        }
    }
}
