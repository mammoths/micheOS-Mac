# Blank pages · 0.10 scope

Miche's central unit is now a small, blank page that can grow into a note, blog, collection or calendar. Start without choosing a specialized widget type.

## Included

- Double-click blank space in a Miche, or use `/page` / `/note`, to create a docked page.
- Name it in its header. Use the left grip to drag, the corner grip to resize, the arrow to pop out/dock, and the menu to expand.
- Write immediately, with bold and italic on selected text (Command or Control B/I). Command–Shift–> / < changes selected text size by 10%, including repeated presses.
- Selection reveals a small formatting bar. Right-click selected text to format or put a rounded inline box around it. Whole text, image and table blocks can also be boxed.
- Right-click the page or a block to insert text, PNG/JPEG images, tables and smaller pages. Empty paragraphs and cells have no placeholder text, and no persistent + or footer hint is shown. Image files can be pasted/dropped; imports become owned local copies. Images can be resized horizontally.
- Drag block grips to reorder. Command/Control Z and Shift-Z undo/redo content edits within the live editor. Deletions of entire pages remain recoverable through Recently deleted.
- Collect one page inside another by dropping its desk grip onto the other page's header. Add a new child using right-click → Little page inside. A child opens independently, keeps its own ID, and can be moved out using its menu. Right-click a child page to move it to Recently deleted.
- `/table` opens a standalone page containing a table. Vision's `table +` / right-click → table creates a board table.

## Shared tables

All three surfaces use `PageBlock` table data: rich-text cell runs, row heights, column widths and cell colors. Limits are 50 rows and 12 columns. There is no database, formula or cloud dependency.

Tab advances cells and adds a row after the last cell; Shift-Tab moves backwards. Right-click adds rows/columns before or after the cell, deletes selected rows/columns, or sets a color. At least one row and column remain. Choose Ink, Rose, Lavender, Sage, Sand or Sky. Shift-click selects a rectangle of cells; Command/Control-click toggles individual cells. Drag the right or bottom cell boundary to resize the corresponding selected columns/rows together. Equal-width/height commands match the selected dimensions to the context cell.

Page table cells support the page editor's inline formatting. Vision table cells currently use plain text; they preserve rich cell runs until that cell's text is edited. Vision tables participate in object selection, movement, resizing, copy/paste, recovery and saved past Visions.

## Persistence and agent extension

`pages.json` is a separate version-1 local document under the workspace writer lock. It has stable page/block IDs, Miche ownership, parent IDs, placement, floating state and window geometry. It saves through a flushed temporary file, previous-save backup, and atomic replace. Validation rejects bad dimensions, malformed tables, cycles and cross-Miche nesting before changing memory. Images are relative UUID filenames under `page-images`, retained for recovery. The editor receives structured runs through the native bridge and renders text as text; it does not save arbitrary HTML or load remote scripts. Document content changes do not rebuild the desk or steal title focus.

Vision tables require workspace schema 8. Upgrading retains an exact-byte before-v8 original. Existing schema 7 content is preserved. Page windows flush before transfer and quit; a failed flush keeps the editor open.

## Intentional next steps

Embedding live Flow/Clipboard/Meili widgets, cross-Miche page moves, page export, collaborative editing, databases/formulas, additional block types and persisted undo history are outside this first scope. The content-calendar concept can now be prototyped with a standalone table; calendar-specific planning and automation remain a later design step.

## Verification

117 store checks, 273 headless UI checks, 50 Flow checks and 8 Meili tests pass. Native synthetic QA verified title focus, selected bold/italic and repeated sizing, inline boxing, table entry/Tab/color, owned image import, nested page creation and recoverable deletion, blank-space insertion menus, placeholder-free cells, popout/dock and restart retention. Multi-monitor and trackpad variations, native multi-cell boundary resizing and external Finder drops remain unverified. The previously recorded Vision image-picker limitation remains separate from the new native page image picker.
