using System.Text;
using System.Text.Json;
using Miche.Mac.Models;
using Miche.Mac.Services;

public static class ConnectionChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new Exception("Connections: " + message);
            checks++;
        }
        void RejectUnchanged(WorkspaceStore store, Action edit)
        {
            var bytes = File.ReadAllBytes(store.FilePath);
            var memory = JsonSerializer.Serialize(store.Snapshot);
            var rejected = false;
            try { edit(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "invalid connection edit was accepted");
            Check(bytes.SequenceEqual(File.ReadAllBytes(store.FilePath)) && memory == JsonSerializer.Serialize(store.Snapshot),
                "rejected connection changed durable or in-memory state");
        }
        static VisionItem Find(WorkspaceStore store, Guid micheId, Guid id) =>
            store.Snapshot.VisionBoards.Single(b => b.MicheId == micheId).Items.Single(i => i.Id == id);

        var directory = Path.Combine(root, "connection-contracts");
        Guid home, other, sourceId, targetId, spareId, artifactId, recoveryId;
        using (var store = new WorkspaceStore(directory))
        {
            home = store.Snapshot.Index.RootMicheId;
            other = store.Create("connection destination");
            var source = new VisionItem { Text = "Upstream thought", NoteShape = "ellipse" };
            var target = new VisionItem { Text = "Downstream thought", Left = 400, Top = 200, RoundedFrame = true };
            var spare = new VisionItem { Text = "Another branch", Left = 400, Top = 400 };
            var foreign = new VisionItem { Text = "Different board" };
            sourceId = source.Id; targetId = target.Id; spareId = spare.Id;
            store.SaveVisionItems(home, new[] { source, target, spare });
            store.SaveVisionItem(other, foreign);
            store.ConnectVisionItems(home, sourceId, targetId);
            Check(Find(store, home, sourceId).DownstreamIds.SequenceEqual(new[] { targetId }), "source did not retain the downstream target");
            Check(Find(store, home, targetId).DownstreamIds.Count == 0, "directed connection also changed its target");
            store.ConnectVisionItems(home, sourceId, spareId);
            Check(Find(store, home, sourceId).DownstreamIds.SequenceEqual(new[] { targetId, spareId }), "one source cannot branch to multiple objects");
            store.DisconnectVisionItems(home, sourceId, spareId);
            Check(Find(store, home, sourceId).DownstreamIds.SequenceEqual(new[] { targetId }), "disconnect removed an unrelated connection");

            var notifications = 0;
            store.Changed += () => notifications++;
            var idempotentBytes = File.ReadAllBytes(store.FilePath);
            store.ConnectVisionItems(home, sourceId, targetId);
            store.DisconnectVisionItems(home, sourceId, spareId);
            Check(notifications == 0 && idempotentBytes.SequenceEqual(File.ReadAllBytes(store.FilePath)), "idempotent edits performed a commit");
            Check(Find(store, home, sourceId).DownstreamIds.Count == 1, "duplicate connection was stored");
            RejectUnchanged(store, () => store.ConnectVisionItems(home, sourceId, sourceId));
            RejectUnchanged(store, () => store.ConnectVisionItems(home, sourceId, Guid.Empty));
            RejectUnchanged(store, () => store.ConnectVisionItems(home, sourceId, foreign.Id));
            RejectUnchanged(store, () => store.ConnectVisionItems(home, foreign.Id, targetId));

            store.SetVisionDeleted(home, targetId, true);
            RejectUnchanged(store, () => store.ConnectVisionItems(home, sourceId, targetId));
            RejectUnchanged(store, () => store.DisconnectVisionItems(home, sourceId, targetId));
            Check(Find(store, home, sourceId).DownstreamIds.SequenceEqual(new[] { targetId }), "recoverable deletion destroyed the connection");
            store.SetVisionDeleted(home, targetId, false);
            store.SetVisionDeleted(home, sourceId, true);
            RejectUnchanged(store, () => store.ConnectVisionItems(home, sourceId, spareId));
            store.SetVisionDeleted(home, sourceId, false);
            Check(Find(store, home, sourceId).DownstreamIds.SequenceEqual(new[] { targetId }), "endpoint recovery lost the connection");

            var detached = Find(store, home, sourceId);
            var copied = WorkspaceStore.CopyVision(detached);
            copied.DownstreamIds.Clear();
            Check(detached.DownstreamIds.SequenceEqual(new[] { targetId }), "CopyVision shares its mutable connection list");
            detached.DownstreamIds.Clear();
            Check(Find(store, home, sourceId).DownstreamIds.SequenceEqual(new[] { targetId }), "workspace snapshot shares its mutable connection list");

            var failedBytes = File.ReadAllBytes(store.FilePath);
            var failedMemory = JsonSerializer.Serialize(store.Snapshot);
            File.Delete(store.FilePath + ".bak");
            Directory.CreateDirectory(store.FilePath + ".bak");
            var failed = false;
            try { store.ConnectVisionItems(home, sourceId, spareId); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
            Check(failed, "blocked durable backup did not reject the connection save");
            Check(failedBytes.SequenceEqual(File.ReadAllBytes(store.FilePath)) && failedMemory == JsonSerializer.Serialize(store.Snapshot),
                "failed connection save changed disk or memory");
            Check(Directory.GetFiles(directory, "workspace.json.*.tmp").Length == 0, "failed connection save leaked a temporary file");
            Directory.Delete(store.FilePath + ".bak");

            var pair = new[] { Find(store, home, sourceId), Find(store, home, targetId) };
            var pairIds = store.PasteVisionItems(other, home, home, pair);
            Check(pairIds.Length == 2 && pairIds.All(id => id != sourceId && id != targetId), "pair paste did not create distinct object identities");
            Check(Find(store, other, pairIds[0]).DownstreamIds.SequenceEqual(new[] { pairIds[1] }), "pair paste did not remap its internal connection");
            Check(pair[0].DownstreamIds.SequenceEqual(new[] { targetId }), "paste mutated the copied source");
            var soloId = store.PasteVisionItems(other, home, home, new[] { pair[0] }).Single();
            Check(Find(store, other, soloId).DownstreamIds.Count == 0, "solo paste kept a connection to an unselected object");
            Check(Find(store, home, sourceId).DownstreamIds.SequenceEqual(new[] { targetId }), "paste changed the original graph");

            artifactId = store.StartFreshVision(home, "Saved connected thoughts")!.Value;
            var fresh = store.Snapshot;
            Check(fresh.VisionBoards.Single(b => b.MicheId == home).Items.Count == 0, "start fresh retained objects on the current board");
            var saved = fresh.VisionArtifacts.Single(a => a.Id == artifactId);
            var savedSource = saved.Items.Single(i => i.Text == source.Text);
            var savedTarget = saved.Items.Single(i => i.Text == target.Text);
            Check(savedSource.Id != sourceId && savedTarget.Id != targetId && savedSource.DownstreamIds.SequenceEqual(new[] { savedTarget.Id }),
                "saved Vision did not remap its connected object identities");
            var reset = fresh.VisionResets.Single(r => r.MicheId == home);
            Check(reset.Items.Single(i => i.Id == sourceId).DownstreamIds.SequenceEqual(new[] { targetId }), "reset recovery record lost its original graph");
            var newWork = new VisionItem { Text = "New current work" };
            store.SaveVisionItem(home, newWork);
            recoveryId = store.RecoverVisionReset(home, reset.Id);
            var recovered = store.Snapshot.VisionArtifacts.Single(a => a.Id == recoveryId);
            var recoveredSource = recovered.Items.Single(i => i.Text == source.Text);
            var recoveredTarget = recovered.Items.Single(i => i.Text == target.Text);
            Check(recoveredSource.Id != sourceId && recoveredSource.Id != savedSource.Id && recoveredTarget.Id != targetId &&
                recoveredSource.DownstreamIds.SequenceEqual(new[] { recoveredTarget.Id }), "reset recovery did not remap its independent graph");
            Check(store.Snapshot.VisionBoards.Single(b => b.MicheId == home).Items.Single().Id == newWork.Id, "recovery replaced newer current work");
            Check(store.RecoverVisionReset(home, reset.Id) == recoveryId && store.Snapshot.VisionArtifacts.Count == 2, "repeated recovery duplicated the graph");
            RejectUnchanged(store, () => store.ConnectVisionItems(home, savedSource.Id, recoveredTarget.Id, artifactId));
            var updatedBefore = store.Snapshot.VisionArtifacts.Single(a => a.Id == artifactId).UpdatedAt;
            store.DisconnectVisionItems(home, savedSource.Id, savedTarget.Id, artifactId);
            Check(store.Snapshot.VisionArtifacts.Single(a => a.Id == artifactId).UpdatedAt >= updatedBefore &&
                store.Snapshot.VisionArtifacts.Single(a => a.Id == artifactId).Items.Single(i => i.Id == savedSource.Id).DownstreamIds.Count == 0,
                "editing an artifact did not commit its connection removal");
            store.ConnectVisionItems(home, savedSource.Id, savedTarget.Id, artifactId);
        }
        using (var reopened = new WorkspaceStore(directory))
        {
            Check(reopened.Snapshot.Version == 11, "reopened workspace schema is not 11");
            foreach (var id in new[] { artifactId, recoveryId })
            {
                var artifact = reopened.Snapshot.VisionArtifacts.Single(a => a.Id == id);
                Check(artifact.Items.Single(i => i.Text == "Upstream thought").DownstreamIds.SequenceEqual(new[] {
                    artifact.Items.Single(i => i.Text == "Downstream thought").Id }), "artifact connection did not survive reopen");
            }
        }

        var migrationDirectory = Path.Combine(root, "connection-v10-migration");
        Directory.CreateDirectory(migrationDirectory);
        var legacyHome = new Miche.Mac.Models.Miche { Name = "legacy home" };
        var legacyThought = new VisionItem { Text = "Unconnected legacy thought", NoteShape = "ellipse", Left = 78, Top = 91 };
        var legacy = new Workspace { Version = 10, Index = new MicheIndex { RootMicheId = legacyHome.Id,
            ActiveMicheId = legacyHome.Id, Miches = new() { legacyHome } },
            VisionBoards = new() { new VisionBoard { MicheId = legacyHome.Id, Items = new() { legacyThought } } } };
        // V10 did not have the field. Keep intentionally different formatting to check the exact original bytes.
        var legacyText = " \n" + JsonSerializer.Serialize(legacy, new JsonSerializerOptions { WriteIndented = true })
            .Replace("\"DownstreamIds\": [],", "") + "\n ";
        var legacyBytes = Encoding.UTF8.GetBytes(legacyText);
        File.WriteAllBytes(Path.Combine(migrationDirectory, "workspace.json"), legacyBytes);
        using (var migrated = new WorkspaceStore(migrationDirectory))
        {
            var thought = migrated.Snapshot.VisionBoards.Single().Items.Single();
            Check(migrated.Snapshot.Version == 11 && thought.Id == legacyThought.Id && thought.Text == legacyThought.Text &&
                thought.NoteShape == "ellipse" && thought.Left == 78 && thought.Top == 91 && thought.DownstreamIds.Count == 0,
                "v10 migration changed prior content or added a connection");
            var backup = Directory.GetFiles(Path.Combine(migrationDirectory, "backups"), "before-v11-*.json").Single();
            Check(File.ReadAllBytes(backup).SequenceEqual(legacyBytes), "v11 migration backup did not preserve exact original bytes");
        }
        using (var reopened = new WorkspaceStore(migrationDirectory))
            Check(reopened.Snapshot.Version == 11 && reopened.Snapshot.VisionBoards.Single().Items.Single().DownstreamIds.Count == 0,
                "migrated legacy thought did not reopen without connections");
        return checks;
    }
}
