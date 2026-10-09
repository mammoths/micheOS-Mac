# Thought connections · 0.13

Vision can connect one object to another with a smooth line and a small arrow pointing downstream. Connections belong to the objects, so moving or resizing a thought updates its lines immediately, including during a drag preview.

## Use

- Select a thought, then drag its small connection dot onto another thought.
- Alternatively, select a thought, choose **connect**, then click the downstream thought. An object's right-click menu also offers **connect to…**.
- Repeat from the same source to branch into more thoughts, or connect a downstream thought onward. Each connection has its own direction; this first version does not enforce a tree or prevent cycles.
- Click a line and press Delete or Backspace to remove it. Right-click a line and choose **Remove connection**, or use the selected line's removal button.
- Escape cancels an unfinished connection. Dropping a dragged connection on blank space cancels that drag.

The connection dot appears with selection, keeping the board quiet at rest. Curves attach to facing object boundaries, including the curved boundary of an oval around a thought. A connection remains attached when its endpoints move, resize or rotate. Fine lines have a wider click target that follows board zoom.

## Copy, delete and recover

Copying both endpoints preserves their internal connection when pasted, with new object IDs. Copying a single endpoint drops its connections to objects outside the copied selection. Pasted thoughts therefore do not unexpectedly connect back to the originals.

Moving either endpoint to Recently deleted hides its connections. Restoring the endpoint reveals those connections again when both endpoints are present. Removing a connection itself removes only that link; it does not delete either thought.

Saved past Visions retain their connections. Saving a fresh snapshot and recovering an earlier reset assign new object IDs and remap each internal connection to the corresponding new object. Existing current work remains separate from the recovered Vision.

## Persistence and extension

Each `VisionItem` stores a `DownstreamIds` list of target object IDs. The list is copied independently with an object, rejects empty/self/duplicate IDs, and is limited to 1,000 targets per source. The creation transaction requires two live objects on the same current or past Vision. Ordinary movement and content edits preserve the object IDs and their links.

Connections require workspace schema **11**. Upgrading creates an exact-byte original backup named `before-v11-<id>.json` before committing the migration. Existing thought IDs, positions, text, images and tables remain intact. Connections use the workspace's validated atomic save and backup mechanism; a failed commit leaves the saved workspace intact.

The geometry helper computes the curve from current endpoint bounds rather than storing a fixed path. This keeps persistence small and gives future automation a direct representation: identify a source and target, then add or remove their directed relationship.

## First scope

Connections are available in current Vision boards and saved past Visions. Calendar boards do not offer connection controls, and calendar copies do not retain Vision links.

Routing considers its two endpoints. It does not avoid unrelated objects or guarantee that a crowded board's lines never cross. Overlapping endpoints receive an exterior curve. Connection labels, manual rerouting, richer relationship types and obstacle avoidance remain future work.

## Verification

186 store checks, 356 headless UI checks, 104 geometry checks, 50 Flow checks and 8 Meili tests pass. The connection checks cover directed branching, internal ID remapping, deleted endpoint recovery, schema migration, dynamic drag previews, zoom-aware line selection and geometry at touching or overlapping bounds.

Native synthetic QA verified creating connections by click and dot drag, branching, moving a connected thought, removing a line and retaining the graph after restart. No personal notes or screenshots are required by these checks.
