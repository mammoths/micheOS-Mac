using System.Text.Json;
using Miche.Mac.Services;
using Miche.Mac.Models;

var root = Path.Combine(Path.GetTempPath(), "miche-mac-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
void Reject<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { checks++; return; }
    throw new Exception("Expected " + typeof(T).Name);
}
try
{
    var directory = Path.Combine(root, "workspace");
    Guid home, niche, capture;
    using (var store = new WorkspaceStore(directory))
    {
        home = store.Snapshot.Index.RootMicheId;
        Check(store.Snapshot.Index.Miches.Count == 1 && store.Snapshot.RootDump.Count == 0 &&
              store.Snapshot.Widgets.Count == 0, "Fresh home must be blank");
        Reject<IOException>(() => { using var second = new WorkspaceStore(directory); });
        niche = store.Create("  studio  ");
        store.Rename(niche, "work");
        Check(store.Snapshot.Index.Miches.Single(n => n.Id == niche).Name == "work", "Rename lost identity");
        var before = File.ReadAllText(store.FilePath);
        Reject<ArgumentException>(() => store.Create("WORK"));
        Reject<ArgumentException>(() => store.Create("\n"));
        Check(File.ReadAllText(store.FilePath) == before, "Rejected name changed durable state");
        capture = store.Capture("a synthetic test thought");
        store.Activate(home);
        Check(store.Snapshot.RootDump.Single().Id == capture, "Dump isn't shared");
        Check(!store.Snapshot.Widgets.Any(w => w.MicheId == home), "New niches must start blank");
        store.Delete(niche);
        Check(store.Snapshot.Index.ActiveMicheId == home && store.Snapshot.Trash.Count == 1, "Removal must be recoverable");
        Check(store.Snapshot.RootDump.Single().OriginMicheId == niche &&
            store.Snapshot.RootDump.Single().OriginMicheName == "work", "Capture provenance lost after niche deletion");
        store.Create("work");
        store.Restore(niche);
        Check(store.Snapshot.Index.Miches.Single(n => n.Id == niche).Name == "work · restored 2", "Recovery name collision");
        store.SetCaptureDeleted(capture, true);
        Check(store.Snapshot.RootDump.Single().DeletedAt is not null, "Deleted capture missing recovery metadata");
        store.SetCaptureDeleted(capture, false);
        Reject<ArgumentException>(() => store.Delete(home));
        Check(File.Exists(store.FilePath + ".bak"), "Last-good backup missing");
        var snapshot = store.Snapshot;
        snapshot.Index.Miches.Clear();
        Check(store.Snapshot.Index.Miches.Count == 3, "Snapshot leaked mutable state");
    }
    using (var reopened = new WorkspaceStore(directory))
    {
        Check(reopened.Snapshot.Index.ActiveMicheId == niche, "Active niche didn't survive restart");
        Check(reopened.Snapshot.RootDump.Single().DeletedAt is null, "Recovered capture didn't survive restart");
        Check(reopened.Snapshot.RootDump.Single().OriginMicheId == niche, "Capture provenance didn't survive restart");
        // Deliberately block the backup path: a failed save must leave memory and disk unchanged.
        File.Delete(reopened.FilePath + ".bak");
        Directory.CreateDirectory(reopened.FilePath + ".bak");
        var before = File.ReadAllText(reopened.FilePath);
        Reject<UnauthorizedAccessException>(() => reopened.Create("cannot save"));
        Check(File.ReadAllText(reopened.FilePath) == before && reopened.Snapshot.Index.Miches.Count == 3,
            "Failed save changed state");
    }
    using (var isolated = new WorkspaceStore(Path.Combine(root, "other")))
        Check(isolated.Snapshot.RootDump.Count == 0, "Independent data roots leaked captures");
    var corrupt = Path.Combine(root, "corrupt");
    Directory.CreateDirectory(corrupt);
    var corruptFile = Path.Combine(corrupt, "workspace.json");
    File.WriteAllText(corruptFile, "{broken");
    Reject<JsonException>(() => { using var broken = new WorkspaceStore(corrupt); });
    Check(File.ReadAllText(corruptFile) == "{broken", "Corrupt data overwritten");
    File.WriteAllText(corruptFile, "{\"Version\":99}");
    Reject<InvalidDataException>(() => { using var unknown = new WorkspaceStore(corrupt); });
    Check(File.ReadAllText(corruptFile) == "{\"Version\":99}", "Future schema overwritten");
    var upgraded = Path.Combine(root, "upgrade");
    Directory.CreateDirectory(upgraded);
    var legacyHome = new Miche.Mac.Models.Miche { Name = "home" };
    var legacyRemoved = new Miche.Mac.Models.Miche { Name = "gone" };
    var legacyCapture = new Capture { Text = "kept", OriginMicheId = legacyRemoved.Id, OriginMicheName = "gone" };
    var legacy = new Workspace { Version = 1, Index = new MicheIndex { RootMicheId = legacyHome.Id,
        ActiveMicheId = legacyHome.Id, Miches = new() { legacyHome } },
        DumpSpaces = new() { legacyRemoved.Id }, Trash = new() { new ArchivedMiche { Miche = legacyRemoved } },
        RootDump = new() { legacyCapture } };
    var original = JsonSerializer.Serialize(legacy);
    var legacyPath = Path.Combine(upgraded, "workspace.json");
    File.WriteAllText(legacyPath, original);
    Guid widgetId;
    using (var store = new WorkspaceStore(upgraded))
    {
        var state = store.Snapshot;
        Check(state.Version == 8 && state.DumpSpaces is null && state.Widgets.Count == 1, "Upgrade lost widget visibility");
        Check(!state.Widgets.Any(w => w.MicheId == legacyHome.Id), "Upgrade added widget to blank home");
        Check(state.RootDump.Single().Id == legacyCapture.Id && state.Trash.Single().Miche.Id == legacyRemoved.Id,
            "Upgrade changed capture or trash identity");
        Check(File.ReadAllText(Directory.GetFiles(Path.Combine(upgraded, "backups"), "before-v2-*.json").Single()) == original,
            "Upgrade backup is not the exact original");
        widgetId = state.Widgets.Single().Id;
        Check(state.Placements.Single().Mode == "hidden" && state.Placements.Single().BeforeRemoval == "docked",
            "Archived widget migration must be recoverable");
        store.Restore(legacyRemoved.Id);
        Check(store.Snapshot.Placements.Single().Mode == "docked", "Legacy archived board wasn't restored");
        store.SetHost(widgetId, "floating", new EditorState { Text = "draft\nkept", CaretIndex = 3, SelectionStart = 1, SelectionEnd = 4 },
            new WindowGeometry { X = 50, Y = 70, Width = 600, Height = 400 });
    }
    using (var store = new WorkspaceStore(upgraded))
    {
        Check(store.Snapshot.Widgets.Single().Id == widgetId && store.Snapshot.Widgets.Single().Editor.Text == "draft\nkept",
            "Restart changed instance ID or draft");
        Check(store.Snapshot.Placements.Single().Mode == "floating" && store.Snapshot.Placements.Single().Window!.Width == 600,
            "Restart lost host or geometry");
        store.Delete(legacyRemoved.Id);
        Check(store.Snapshot.Placements.Single().Mode == "hidden", "Delete left archived widget floating");
        store.Restore(legacyRemoved.Id);
        Check(store.Snapshot.Placements.Single().Mode == "floating" && store.Snapshot.Widgets.Single().Editor.SelectionEnd == 4,
            "Restore lost host or editor");
    }
    using (var store = new WorkspaceStore(upgraded))
    {
        store.SetHost(widgetId, "docked", store.Snapshot.Widgets.Single().Editor);
        var before = File.ReadAllText(store.FilePath);
        Reject<InvalidDataException>(() => store.SetDeskPlacement(widgetId, 1.5, 0, .5, 320));
        Reject<InvalidDataException>(() => store.SetDeskPlacement(widgetId, .2, double.NaN, .5, 320));
        Check(File.ReadAllText(store.FilePath) == before, "Invalid desk geometry changed durable state");
        store.SetDeskPlacement(widgetId, .25, 160, .45, 400);
    }
    using (var store = new WorkspaceStore(upgraded))
        Check(store.Snapshot.Placements.Single().PosX == .25 && store.Snapshot.Placements.Single().PosY == 160 &&
            store.Snapshot.Placements.Single().WidthFraction == .45 && store.Snapshot.Placements.Single().Height == 400,
            "Restart lost normalized desk placement");
    var visionDirectory = Path.Combine(root, "vision-records");
    Guid visionHome, visionOther, visionText;
    using (var store = new WorkspaceStore(visionDirectory))
    {
        visionHome = store.Snapshot.Index.RootMicheId;
        visionOther = store.Create("vision other");
        var text = new VisionItem { Text = "first board", Left = 40, Top = 80, FontSize = 32, Width = 320, Height = 80 };
        visionText = text.Id; store.SaveVisionItem(visionHome,text);
        store.SaveVisionItem(visionOther,new VisionItem { Text = "second board" });
        Check(store.Snapshot.VisionBoards.Count == 2 && store.Snapshot.VisionBoards.Single(b => b.MicheId == visionHome).Items.Single().Text == "first board",
            "Vision boards aren't isolated");
        var before = File.ReadAllText(store.FilePath);
        text.Width = double.NaN; Reject<InvalidDataException>(() => store.SaveVisionItem(visionHome,text));
        Check(File.ReadAllText(store.FilePath) == before, "Invalid Vision geometry changed disk");
        Reject<InvalidDataException>(() => store.VisionAssetPath(visionHome,"../outside.png"));
        text.Width = 320; text.Rotation = -5; store.SaveVisionItem(visionHome,text);
        store.SetVisionDeleted(visionHome,visionText,true);
        Check(store.Snapshot.VisionBoards.Single(b => b.MicheId == visionHome).Items.Single().DeletedAt is not null, "Vision delete not recoverable");
        store.Delete(visionOther);
        Check(store.Snapshot.VisionBoards.Single(b => b.MicheId == visionOther).Items.Single().Text == "second board", "Niche deletion destroyed Vision");
        store.Restore(visionOther);
    }
    using (var store = new WorkspaceStore(visionDirectory))
    {
        store.SetVisionDeleted(visionHome,visionText,false);
        var item = store.Snapshot.VisionBoards.Single(b => b.MicheId == visionHome).Items.Single();
        Check(item.Id == visionText && item.FontSize == 32 && item.Width == 320 && item.Rotation == -5 && item.DeletedAt is null,
            "Vision text styling/identity didn't survive restart/recovery");
    }
    var v2Directory = Path.Combine(root,"v2-upgrade"); Directory.CreateDirectory(v2Directory);
    var v2 = JsonSerializer.Deserialize<Workspace>(File.ReadAllText(legacyPath))!; v2.Version = 2;
    var v2Text = JsonSerializer.Serialize(v2); var v2Path = Path.Combine(v2Directory,"workspace.json"); File.WriteAllText(v2Path,v2Text);
    using (var store = new WorkspaceStore(v2Directory))
    {
        Check(store.Snapshot.Version == 8 && store.Snapshot.Widgets.Single().Id == widgetId && store.Snapshot.VisionBoards.Count == 0,
            "Vision upgrade changed widgets or added fake boards");
        Check(File.ReadAllText(Directory.GetFiles(Path.Combine(v2Directory,"backups"),"before-v3-*.json").Single()) == v2Text,
            "Vision upgrade didn't preserve exact v2 bytes");
    }
    var scopes = Path.Combine(root,"dump-scopes"); Guid localOwner, firstBatch, localCapture;
    using (var store = new WorkspaceStore(scopes))
    {
        localOwner=store.Create("local owner"); store.ShowDump(true); var id=store.Snapshot.Widgets.Single().Id;
        var global=store.Capture("shared from local owner",localOwner,id);
        var rootDraft=new EditorState {Text="root draft",CaretIndex=4,SelectionStart=1,SelectionEnd=3};
        store.SetDumpScope(id,true,rootDraft);
        localCapture=store.Capture("owned locally",localOwner,id);
        Check(store.DumpEntries().Count==2 && store.DumpEntries(localOwner).Single().Id==localCapture && store.Snapshot.RootDump.Single(n=>n.Id==global).OwnerMicheId is null,
            "Ownership followed origin, canonical duplicated records or local leaked shared captures");
        var localDraft=new EditorState {Text="local draft",CaretIndex=5}; store.SetDumpScope(id,false,localDraft);
        Reject<ArgumentException>(()=>store.ShowDump(true,local:true));
        store.SetDumpScope(id,true,rootDraft); var independent=store.Capture("standalone canonical",localOwner);
        Check(store.Snapshot.RootDump.Single(n=>n.Id==independent).OwnerMicheId is null && store.Snapshot.Widgets.Single().Editor.Text=="root draft" && store.Snapshot.Widgets.Single().LocalEditor.Text=="local draft",
            "Standalone capture consumed either unsent draft or inherited local ownership");
        store.SetCaptureDeleted(independent,true); store.SetDumpScope(id,false,localDraft);
        Check(store.Snapshot.Widgets.Single().Editor.Text==rootDraft.Text && store.Snapshot.Widgets.Single().LocalEditor.Text==localDraft.Text,
            "Scope switching overwrote a separate draft");
        firstBatch=store.FlushDump(localOwner);
        Check(store.DumpEntries().Single().Id==global && store.Snapshot.DumpArchives.Single().CaptureIds.SequenceEqual(new[]{localCapture}), "Local flush included shared capture");
        store.SetCaptureDeleted(localCapture,true); store.RestoreDumpBatch(firstBatch);
        Check(store.Snapshot.RootDump.Single(n=>n.Id==localCapture).DeletedAt is not null && store.DumpEntries(localOwner).Count==0, "Archive restore resurrected an explicit deletion");
        store.SetCaptureDeleted(localCapture,false); store.RestoreDumpBatch(firstBatch);
        Check(store.DumpEntries(localOwner).Single().Id==localCapture,"Explicitly recovered capture can't be recovered from archive");
        var secondBatch=store.FlushDump(); store.RestoreDumpBatch(firstBatch); store.RestoreDumpBatch(firstBatch);
        Check(store.DumpEntries().Count==0 && store.Snapshot.RootDump.Where(n=>n.DeletedAt is null).All(n=>n.ArchiveBatchId==secondBatch),"Old/double restore stole entries re-flushed in later batch");
        store.Delete(localOwner); store.RestoreDumpBatch(secondBatch);
        Check(store.DumpEntries().Count==2 && store.DumpEntries(localOwner).Single().Id==localCapture && store.Snapshot.RootDump.Single(n=>n.Id==global).OriginMicheName=="local owner",
            "Deleting origin lost canonical/local records or archive restore");
        store.Restore(localOwner);
        var before=File.ReadAllText(store.FilePath); File.Delete(store.FilePath+".bak"); Directory.CreateDirectory(store.FilePath+".bak");
        Reject<UnauthorizedAccessException>(()=>store.FlushDump());
        Reject<UnauthorizedAccessException>(()=>store.SetDumpScope(id,true,new EditorState {Text="unsaved outgoing"}));
        Check(File.ReadAllText(store.FilePath)==before && store.DumpEntries().Count==2 && store.Snapshot.Widgets.Single().CaptureScope=="canonical" && store.Snapshot.Widgets.Single().Editor.Text=="root draft",
            "Failed flush/scope switch changed memory or disk");
        Directory.Delete(store.FilePath+".bak");
        firstBatch=store.FlushDump(); store.Delete(localOwner);
    }
    using(var store=new WorkspaceStore(scopes))
    {
        Check(store.DumpEntries().Count==0 && store.DumpEntries(archived:true).Count==2 && store.Snapshot.Widgets.Single().LocalEditor.Text=="local draft" && store.Snapshot.Trash.Single().Miche.Id==localOwner, "Restart lost archived captures, removed owner or local draft");
        store.RestoreDumpBatch(firstBatch); store.RestoreDumpBatch(firstBatch);
        Check(store.DumpEntries().Count==2 && store.Snapshot.RootDump.Select(n=>n.Id).Distinct().Count()==3, "Restart/double recovery duplicated captures");
    }
    var malformedBase=JsonSerializer.Deserialize<Workspace>(File.ReadAllText(Path.Combine(scopes,"workspace.json")))!;
    foreach(var mutate in new Action<Workspace>[] {
        s=>s.Widgets[0]=null!, s=>s.Widgets[0].LocalEditor=null!, s=>s.Widgets[0].Editor.CaretIndex=30000,
        s=>s.Widgets[0].CaptureScope="unknown",s=>s.DumpArchives[0]=null!,s=>s.RootDump[0].OwnerMicheId=Guid.NewGuid()})
    {
        var state=JsonSerializer.Deserialize<Workspace>(JsonSerializer.Serialize(malformedBase))!; mutate(state);
        var folder=Path.Combine(root,"malformed-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var bytes=JsonSerializer.Serialize(state);File.WriteAllText(Path.Combine(folder,"workspace.json"),bytes);
        Reject<InvalidDataException>(()=>{using var store=new WorkspaceStore(folder);});
        Check(File.ReadAllText(Path.Combine(folder,"workspace.json"))==bytes,"Malformed scope/widget/archive was overwritten");
    }
    var v3Directory=Path.Combine(root,"v3-upgrade"); Directory.CreateDirectory(v3Directory);
    var v3=JsonSerializer.Deserialize<Workspace>(File.ReadAllText(legacyPath))!; v3.Version=3;
    v3.RootDump.Single().DeletedAt=DateTimeOffset.UtcNow;
    var v3Bytes=JsonSerializer.Serialize(v3); File.WriteAllText(Path.Combine(v3Directory,"workspace.json"),v3Bytes);
    using(var store=new WorkspaceStore(v3Directory))
    {
        Check(File.ReadAllText(Directory.GetFiles(Path.Combine(v3Directory,"backups"),"before-v4-*.json").Single())==v3Bytes,"Dump migration didn't preserve exact v3 bytes");
        Check(store.Snapshot.RootDump.Single().OwnerMicheId is null && store.Snapshot.RootDump.Single().ArchiveBatchId is null && store.Snapshot.RootDump.Single().DeletedAt==v3.RootDump.Single().DeletedAt &&
            store.Snapshot.Widgets.Single().Editor.Text==v3.Widgets.Single().Editor.Text && store.Snapshot.Widgets.Single().LocalEditor.Text=="", "Dump migration changed ownership/identity/draft/deletion");
    }
    var visions=Path.Combine(root,"past-visions"); Guid pastOwner,artifactId,resetId,currentTextId;
    using(var store=new WorkspaceStore(visions))
    {
        pastOwner=store.Create("past owner");var item=new VisionItem {Text="first vision"}; currentTextId=item.Id; store.SaveVisionItem(pastOwner,item);
        var before=File.ReadAllText(store.FilePath);Reject<ArgumentException>(()=>store.StartFreshVision(pastOwner," "));
        Check(File.ReadAllText(store.FilePath)==before,"Invalid artifact title changed current board");
        artifactId=store.StartFreshVision(pastOwner,"first season")!.Value;resetId=store.Snapshot.VisionResets.Single().Id;
        var artifact=store.Snapshot.VisionArtifacts.Single();
        Check(store.Snapshot.VisionBoards.Single().Items.Count==0 && artifact.Items.Single().Id!=item.Id && store.Snapshot.VisionResets.Single().Items.Single().Id==item.Id,
            "Snapshot identities collided or current wasn't cleared atomically");
        var edit=artifact.Items.Single();edit.Text="edited past";edit.Rotation=15;store.SaveVisionItem(pastOwner,edit,artifactId);
        store.SaveVisionItem(pastOwner,new VisionItem {Text="new current"});
        Check(store.Snapshot.VisionBoards.Single().Items.Single().Text=="new current" && store.Snapshot.VisionResets.Single().Items.Single().Text=="first vision", "Artifact edit aliased current/reset metadata");
        var recovered=store.RecoverVisionReset(pastOwner,resetId);var again=store.RecoverVisionReset(pastOwner,resetId);
        Check(recovered==again && store.Snapshot.VisionArtifacts.Count==2 && store.Snapshot.VisionBoards.Single().Items.Single().Text=="new current", "Reset recovery overwrote newer work or duplicated artifact");
        store.RenameVisionArtifact(pastOwner,artifactId,"renamed season");store.SetVisionArtifactDeleted(pastOwner,artifactId,true);
        Reject<ArgumentException>(()=>store.SaveVisionItem(pastOwner,edit,artifactId));store.SetVisionArtifactDeleted(pastOwner,artifactId,false);
        Check(store.Snapshot.VisionArtifacts.Single(a=>a.Id==artifactId).Title=="renamed season" && store.Snapshot.VisionArtifacts.Single(a=>a.Id==artifactId).Items.Single().Text=="edited past", "Artifact rename/delete/restore changed content");
        before=File.ReadAllText(store.FilePath);File.Delete(store.FilePath+".bak");Directory.CreateDirectory(store.FilePath+".bak");
        Reject<UnauthorizedAccessException>(()=>store.StartFreshVision(pastOwner,"failed snapshot"));
        Check(File.ReadAllText(store.FilePath)==before && store.Snapshot.VisionBoards.Single().Items.Single().Text=="new current" && store.Snapshot.VisionArtifacts.Count==2, "Failed save-and-clear changed current/artifact/reset state");
        Directory.Delete(store.FilePath+".bak");store.StartFreshVision(pastOwner);store.Delete(pastOwner);
    }
    using(var store=new WorkspaceStore(visions))
    {
        Check(store.Snapshot.VisionResets.Count==2 && store.Snapshot.VisionArtifacts.Single(a=>a.Id==artifactId).Items.Single().Text=="edited past" && store.Snapshot.Trash.Single().Miche.Id==pastOwner, "Restart/removing Miche lost past visions or durable clear recovery");
        store.Restore(pastOwner); var recovered=store.RecoverVisionReset(pastOwner,store.Snapshot.VisionResets.Last().Id);
        Check(store.Snapshot.VisionArtifacts.Single(a=>a.Id==recovered).Items.Single().Text=="new current" && store.Snapshot.VisionBoards.Single().Items.Count==0, "Recovering unsaved clear replaced current or lost work");
    }
    var v4Directory=Path.Combine(root,"v4-upgrade");Directory.CreateDirectory(v4Directory);
    var v4=JsonSerializer.Deserialize<Workspace>(File.ReadAllText(Path.Combine(scopes,"workspace.json")))!;v4.Version=4;
    var v4Bytes=JsonSerializer.Serialize(v4);File.WriteAllText(Path.Combine(v4Directory,"workspace.json"),v4Bytes);
    using(var store=new WorkspaceStore(v4Directory))
        Check(store.Snapshot.Version==8 && store.Snapshot.VisionArtifacts.Count==0 && File.ReadAllText(Directory.GetFiles(Path.Combine(v4Directory,"backups"),"before-v5-*.json").Single())==v4Bytes &&
            store.Snapshot.Widgets.Single().LocalEditor.Text=="local draft", "Past visions migration didn't preserve exact v4/scoped drafts");
    // A blocked migration commit must retain the original version-1 file.
    var failedUpgrade = Path.Combine(root, "failed-upgrade");
    Directory.CreateDirectory(failedUpgrade);
    var failedPath = Path.Combine(failedUpgrade, "workspace.json");
    File.WriteAllText(failedPath, original);
    Directory.CreateDirectory(failedPath + ".bak");
    Reject<UnauthorizedAccessException>(() => { using var store = new WorkspaceStore(failedUpgrade); });
    Check(File.ReadAllText(failedPath) == original, "Failed upgrade modified the original");
    // Reject malformed v2 records without resetting them.
    var invalid = Path.Combine(root, "invalid-widget");
    Directory.CreateDirectory(invalid);
    var invalidState = JsonSerializer.Deserialize<Workspace>(File.ReadAllText(legacyPath))!;
    invalidState.Placements.Add(invalidState.Placements.Single());
    var invalidText = JsonSerializer.Serialize(invalidState);
    File.WriteAllText(Path.Combine(invalid, "workspace.json"), invalidText);
    Reject<InvalidDataException>(() => { using var store = new WorkspaceStore(invalid); });
    Check(File.ReadAllText(Path.Combine(invalid, "workspace.json")) == invalidText, "Invalid placements overwritten");
    var shapesRoot=Path.Combine(root,"shapes");Guid shapeOwner;
    using(var store=new WorkspaceStore(shapesRoot))
    {
        shapeOwner=store.Snapshot.Index.RootMicheId;
        foreach(var kind in new[]{"horizontal-line","vertical-line","rounded-rectangle","ellipse"})
            store.SaveVisionItem(shapeOwner,new VisionItem {Kind=kind,Left=70,Top=90,Width=240,Height=160,Rotation=25});
        Check(store.Snapshot.VisionBoards.Single().Items.Count==4,"Shape kinds failed persistence");
        var beforeShape=File.ReadAllText(store.FilePath);
        Reject<InvalidDataException>(()=>store.SaveVisionItem(shapeOwner,new VisionItem {Kind="ellipse",FileName="fake.png"}));
        Check(File.ReadAllText(store.FilePath)==beforeShape,"Invalid shape modified durable state");
        var artifact=store.StartFreshVision(shapeOwner,"shape snapshot")!.Value;
        Check(store.Snapshot.VisionArtifacts.Single().Items.All(i=>WorkspaceStore.IsVisionShape(i.Kind)&&i.Rotation==25),"Past Vision lost shapes");
        var recovery=store.RecoverVisionReset(shapeOwner,store.Snapshot.VisionResets.Single().Id);
        Check(store.Snapshot.VisionArtifacts.Single(a=>a.Id==recovery).Items.All(i=>!store.Snapshot.VisionArtifacts.Single(a=>a.Id==artifact).Items.Any(j=>j.Id==i.Id)),"Recovered shapes reused snapshot identities");
    }
    using(var store=new WorkspaceStore(shapesRoot))Check(store.Snapshot.VisionArtifacts.Count==2,"Shape snapshot reopen failed");
    var migrationRoot=Path.Combine(root,"v5-to-v6");Directory.CreateDirectory(migrationRoot);
    var v5=new Workspace();v5.Index.RootMicheId=Guid.NewGuid();v5.Index.ActiveMicheId=v5.Index.RootMicheId;
    v5.Index.Miches.Add(new Miche.Mac.Models.Miche {Id=v5.Index.RootMicheId,Name="home"});v5.Version=5;
    var exact=JsonSerializer.Serialize(v5);File.WriteAllText(Path.Combine(migrationRoot,"workspace.json"),exact);
    using(var store=new WorkspaceStore(migrationRoot))Check(store.Snapshot.Version==8&&File.ReadAllText(Directory.GetFiles(Path.Combine(migrationRoot,"backups"),"before-v6-*.json").Single())==exact,"Schema 6 migration lost exact original backup");
    var clipRoot=Path.Combine(root,"clipboard");Guid resumeId;
    var originalFile=Path.Combine(root,"synthetic-resume.pdf");File.WriteAllText(originalFile,"synthetic PDF fixture");
    using(var store=new WorkspaceStore(clipRoot))
    {
        store.ShowClipboard(store.Snapshot.Index.RootMicheId);store.RenameClipboard("career kit");
        var link=store.AddClipboardLink("profile","https://example.com/profile");resumeId=store.AddClipboardFile(originalFile,"resume");
        Check(store.Snapshot.Clipboard.Title=="career kit"&&store.Snapshot.Clipboard.Entries.Count==2,"Clipboard name/items failed");
        Check(File.ReadAllText(store.ClipboardFilePath(store.Snapshot.Clipboard.Entries.Single(e=>e.Id==resumeId).FileName!))==File.ReadAllText(originalFile),"Clipboard changed file bytes");
        Reject<ArgumentException>(()=>store.AddClipboardLink("bad","javascript:alert(1)"));Reject<ArgumentException>(()=>store.ClipboardFilePath("../resume.pdf"));
        store.SetClipboardDeleted(link,true);store.SetClipboardDeleted(link,false);
        Check(store.Snapshot.Clipboard.Entries.Single(e=>e.Id==link).DeletedAt is null,"Clipboard restore failed");
    }
    File.Delete(originalFile);
    using(var store=new WorkspaceStore(clipRoot))Check(store.Snapshot.Clipboard.Title=="career kit"&&File.Exists(store.ClipboardFilePath(store.Snapshot.Clipboard.Entries.Single(e=>e.Id==resumeId).FileName!)),"Clipboard reopen depends on original file");
    var multiRoot=Path.Combine(root,"multi-atomic");
    using(var store=new WorkspaceStore(multiRoot))
    {
        var owner=store.Snapshot.Index.RootMicheId;
        var one=new VisionItem {Kind="text",Text="short",Left=40,Top=60,Width=70,Height=30,RoundedFrame=true};
        var two=new VisionItem {Kind="ellipse",Left=160,Top=60,Width=90,Height=90};store.SaveVisionItems(owner,new[]{one,two});
        var saved=File.ReadAllText(store.FilePath);var edit1=WorkspaceStore.CopyVision(one);var edit2=WorkspaceStore.CopyVision(two);edit1.Left+=25;edit2.Left=double.NaN;
        Reject<InvalidDataException>(()=>store.SaveVisionItems(owner,new[]{edit1,edit2}));Check(File.ReadAllText(store.FilePath)==saved&&store.Snapshot.VisionBoards.Single().Items.First().Left==40,"Invalid group partially committed");
        var copied=store.PasteVisionItems(owner,owner,owner,new[]{one,two});Check(copied.Length==2&&store.Snapshot.VisionBoards.Single().Items.Count==4&&copied.All(id=>id!=one.Id&&id!=two.Id),"Paste didn't create independent identities");
        var before=store.Snapshot.VisionBoards.Single().Items.Count;
        Reject<ArgumentException>(()=>store.PasteVisionItems(owner,Guid.NewGuid(),owner,new[]{new VisionItem {Kind="image",FileName="invalid"}}));
        Check(store.Snapshot.VisionBoards.Single().Items.Count==before,"Rejected image paste changed board");
        Directory.CreateDirectory(store.FilePath+".bak.blocked");
        File.Delete(store.FilePath+".bak");Directory.CreateDirectory(store.FilePath+".bak");
        edit1.Left=70;edit2.Left=190;saved=File.ReadAllText(store.FilePath);
        Reject<UnauthorizedAccessException>(()=>store.SaveVisionItems(owner,new[]{edit1,edit2}));Check(File.ReadAllText(store.FilePath)==saved,"Failed group save modified durable state");
    }
    var v6Root=Path.Combine(root,"v6-to-v7");Directory.CreateDirectory(v6Root);
    var v6=new Workspace {Version=6};v6.Index.RootMicheId=Guid.NewGuid();v6.Index.ActiveMicheId=v6.Index.RootMicheId;v6.Index.Miches.Add(new Miche.Mac.Models.Miche {Id=v6.Index.RootMicheId,Name="home"});
    var v6Bytes=JsonSerializer.Serialize(v6);File.WriteAllText(Path.Combine(v6Root,"workspace.json"),v6Bytes);
    using(var store=new WorkspaceStore(v6Root))Check(store.Snapshot.Version==8&&File.ReadAllText(Directory.GetFiles(Path.Combine(v6Root,"backups"),"before-v7-*.json").Single())==v6Bytes,"Schema 7 lost exact original backup");
    var tableRoot=Path.Combine(root,"table-v8");Directory.CreateDirectory(tableRoot);
    var v7=new Workspace{Version=7};v7.Index.RootMicheId=Guid.NewGuid();v7.Index.ActiveMicheId=v7.Index.RootMicheId;v7.Index.Miches.Add(new Miche.Mac.Models.Miche{Id=v7.Index.RootMicheId,Name="home"});
    var v7Original=JsonSerializer.Serialize(v7);File.WriteAllText(Path.Combine(tableRoot,"workspace.json"),v7Original);
    using(var store=new WorkspaceStore(tableRoot))
    {
        Check(store.Snapshot.Version==8&&File.ReadAllText(Directory.GetFiles(Path.Combine(tableRoot,"backups"),"before-v8-*.json").Single())==v7Original,"Table migration lost original backup");
        var table=TableContent.Create();table.Cells[0][0].Add(new PageRun{Text="Monday"});var item=new VisionItem{Kind="table",Table=table,Width=360,Height=188};store.SaveVisionItem(v7.Index.RootMicheId,item);
        var copy=WorkspaceStore.CopyVision(item);copy.Table!.Cells[0][0][0].Text="changed";Check(item.Table.Cells[0][0][0].Text=="Monday","Table clone shared mutable cells");
        var bytes=File.ReadAllBytes(store.FilePath);copy.Table.ColumnWidths=new(){double.NaN,120,120};Reject<InvalidDataException>(()=>store.SaveVisionItem(v7.Index.RootMicheId,copy));Check(File.ReadAllBytes(store.FilePath).SequenceEqual(bytes),"Invalid table overwrote saved state");
    }
    Console.WriteLine($"PASS: {checks} persistence, recovery, isolation and failure checks. Synthetic data only.");
}
finally { Directory.Delete(root, true); }
