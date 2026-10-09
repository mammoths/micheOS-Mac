using System.Text.Json;
using System.Text.RegularExpressions;
using Miche.Mac.Models;

namespace Miche.Mac.Services;

public sealed partial class WorkspaceStore
{
    public static bool IsVisionShape(string kind) => kind is "horizontal-line" or "vertical-line" or "rounded-rectangle" or "ellipse";
    public static string VisionObjectName(VisionItem item) => item.Kind switch {
        "table" => "table", "image" => "image", "horizontal-line" => "horizontal line", "vertical-line" => "vertical line",
        "rounded-rectangle" => "rounded rectangle", "ellipse" => "oval / circle", _ => item.Text[..Math.Min(30,item.Text.Length)]
    };
    public static VisionItem CopyVision(VisionItem item) => new() {
        Id = item.Id, Kind = item.Kind, Table=item.Table is null?null:TableContent.Copy(item.Table), RoundedFrame=item.RoundedFrame, Text = item.Text, FileName = item.FileName,
        Left = item.Left, Top = item.Top, Width = item.Width, Height = item.Height,
        Rotation = item.Rotation, FontSize = item.FontSize, CreatedAt = item.CreatedAt, DeletedAt = item.DeletedAt
    };
    private static VisionBoard BoardFor(Workspace next, Guid micheId)
    {
        if (!next.Index.Miches.Any(m => m.Id == micheId)) throw new ArgumentException("Restore this niche before editing its Vision.");
        var board = next.VisionBoards.SingleOrDefault(b => b.MicheId == micheId);
        if (board is null) { board = new VisionBoard { MicheId = micheId }; next.VisionBoards.Add(board); }
        return board;
    }
    public void SaveVisionItem(Guid micheId,VisionItem item,Guid? artifactId=null)=>SaveVisionItems(micheId,new[]{item},artifactId);
    public void SaveVisionItems(Guid micheId,IEnumerable<VisionItem> changes,Guid? artifactId=null)
    {
        var next=Snapshot;var items=VisionItemsFor(next,micheId,artifactId);var changed=changes.ToArray();
        if(changed.Select(i=>i.Id).Distinct().Count()!=changed.Length)throw new ArgumentException("An object can be changed only once per edit.");
        foreach(var item in changed)
        {
            var existing=items.SingleOrDefault(i=>i.Id==item.Id);
            if(existing is null&&item.Kind is not ("text" or "table")&&!IsVisionShape(item.Kind)||existing is not null&&(item.Kind!=existing.Kind||item.FileName!=existing.FileName))
                throw new ArgumentException("Object type and image ownership cannot change during a canvas edit.");
            if(existing is not null)items.Remove(existing);items.Add(CopyVision(item));
        }
        TouchArtifact(next,artifactId);Commit(next);
    }
    public void SetVisionDeleted(Guid micheId, Guid itemId, bool deleted, Guid? artifactId=null)
    {
        var next = Snapshot; var items = VisionItemsFor(next,micheId,artifactId);
        items.Single(i => i.Id == itemId).DeletedAt = deleted ? DateTimeOffset.UtcNow : null; TouchArtifact(next,artifactId);
        Commit(next); // assets stay owned while the item is recoverable
    }
    internal Guid AddVisionImage(Guid micheId, byte[] validatedBytes, string extension, int pixelWidth, int pixelHeight, double left, double top, Guid? artifactId=null)
    {
        var next = Snapshot; var items = VisionItemsFor(next,micheId,artifactId);
        if (extension is not ("png" or "jpg") || validatedBytes.Length is < 8 or > 20971520 || pixelWidth <= 0 || pixelHeight <= 0)
            throw new ArgumentException("Use a valid PNG or JPEG image under 20 MB.");
        var fileName = micheId.ToString("N") + "/" + Guid.NewGuid().ToString("N") + "." + extension;
        var path = VisionAssetPath(micheId, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Recheck after directory creation and before publishing a unique owned file.
        path = VisionAssetPath(micheId, fileName);
        double width = Math.Min(360d, pixelWidth); double height = width * pixelHeight / pixelWidth;
        if (height > 1200) { width *= 1200 / height; height = 1200; }
        items.Add(new VisionItem { Kind = "image", FileName = fileName, Left = left, Top = top,
            Width = width, Height = height });
        TouchArtifact(next,artifactId);
        var created = false;
        try
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { created = true; file.Write(validatedBytes); file.Flush(true); }
            Commit(next);
            return items.Last().Id;
        }
        catch
        {
            // A view notification can fail after the durable commit. Its owned asset
            // must survive whenever the published metadata already references it.
            var published = AllVisionItems(_state).Any(i=>i.FileName==fileName);
            if (created && !published && File.Exists(path)) File.Delete(path);
            throw;
        }
    }
    public string VisionAssetPath(Guid micheId, string name)
    {
        if (!Regex.IsMatch(name, "^[a-f0-9]{32}/[a-f0-9]{32}\\.(png|jpg)$") || !name.StartsWith(micheId.ToString("N") + "/", StringComparison.Ordinal))
            throw new InvalidDataException("Unsafe Vision asset reference.");
        var folder = Path.Combine(DirectoryPath, "vision");
        var subfolder = Path.Combine(folder, micheId.ToString("N"));
        var path = Path.Combine(folder, name);
        foreach (var part in new[] { folder, subfolder, path })
            if (new FileInfo(part).LinkTarget is not null || new DirectoryInfo(part).LinkTarget is not null)
                throw new InvalidDataException("Vision assets must stay within this workspace.");
        return path;
    }
    private static void ValidateVision(Workspace state, HashSet<Guid> micheIds)
    {
        if (state.VisionBoards is null || state.VisionArtifacts is null || state.VisionResets is null || state.Version < 3 && state.VisionBoards.Count != 0 ||
            state.Version < 5 && (state.VisionArtifacts.Count!=0 || state.VisionResets.Count!=0) ||
            state.VisionBoards.Any(b => b is null || !micheIds.Contains(b.MicheId) || b.Items is null) ||
            state.VisionBoards.Select(b => b.MicheId).Distinct().Count() != state.VisionBoards.Count ||
            state.VisionArtifacts.Any(a=>a is null || a.Id==Guid.Empty || !micheIds.Contains(a.MicheId) || a.Items is null || string.IsNullOrWhiteSpace(a.Title) || a.Title.Length>80 || a.Title.Any(char.IsControl)) ||
            state.VisionResets.Any(r=>r is null || r.Id==Guid.Empty || !micheIds.Contains(r.MicheId) || r.Items is null || r.RecoveredArtifactId is { } id && !state.VisionArtifacts.Any(a=>a.Id==id && a.MicheId==r.MicheId)) ||
            state.VisionArtifacts.Select(a=>a.Id).Concat(state.VisionResets.Select(r=>r.Id)).Distinct().Count()!=state.VisionArtifacts.Count+state.VisionResets.Count)
            throw new InvalidDataException("Invalid Vision board identity. Saved files are unchanged.");
        var ids = new HashSet<Guid>();
        var collections=state.VisionBoards.Select(b=>(b.MicheId,b.Items)).Concat(state.VisionArtifacts.Select(a=>(a.MicheId,a.Items))).Concat(state.VisionResets.Select(r=>(r.MicheId,r.Items)));
        foreach (var board in collections)
        foreach (var item in board.Items)
        {
            if(item?.Kind=="table"){if(state.Version<8||item.Table is null||item.FileName is not null)throw new InvalidDataException("Invalid table object.");TableContent.Validate(item.Table);}else if(item?.Table is not null)throw new InvalidDataException("Unexpected table content.");
            if (item is null || item.RoundedFrame&&(item.Kind!="text"||state.Version<7) || item.Id == Guid.Empty || !ids.Add(item.Id) || item.Kind is not ("text" or "image" or "table") && !IsVisionShape(item.Kind) ||
                IsVisionShape(item.Kind) && (state.Version < 6 || item.FileName is not null || item.Text != "") ||
                item.Text is null || item.Text.Length > 20000 || item.Kind == "text" && (string.IsNullOrWhiteSpace(item.Text) || item.FileName is not null) ||
                item.Kind == "image" && (item.FileName is null || !Regex.IsMatch(item.FileName,
                    "^" + board.MicheId.ToString("N") + "/[a-f0-9]{32}\\.(png|jpg)$")) ||
                !double.IsFinite(item.Left) || item.Left < 0 || item.Left > 10000 || !double.IsFinite(item.Top) || item.Top < 0 || item.Top > 10000 ||
                !double.IsFinite(item.Width) || item.Width <= 0 || item.Width > 4000 || !double.IsFinite(item.Height) || item.Height <= 0 || item.Height > 4000 ||
                !double.IsFinite(item.Rotation) || Math.Abs(item.Rotation) > 360 || !double.IsFinite(item.FontSize) || item.FontSize < 8 || item.FontSize > 200)
                throw new InvalidDataException("Invalid Vision object, dimensions or asset path. Saved files are unchanged.");
        }
    }
}
