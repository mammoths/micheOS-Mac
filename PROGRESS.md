# Development progress

## 0.10.0 · October 9, 2026

Blank pages can grow from a compact note into a collection: rich text, rounded selections/blocks, owned PNG/JPEG images, nested pages, block reordering, resizing, popout/dock and recoverable deletion. Right-click provides insertions and child-page deletion, without persistent plus buttons or placeholder text.

Tables use one validated schema throughout pages, standalone `/table` and Vision. Add/delete rows and columns, Tab through cells, select multiple cells, resize dimensions together and apply a small color palette. Vision tables retain object copy/paste and recovery.

117 store, 273 headless UI, 50 Flow and 8 Meili checks pass. Native synthetic checks verified selected text formatting and sizing, table entry/color, owned image import, nested-page deletion, minimal empty cells, menus, popout/dock and restart persistence. Workspace schema 8 keeps an exact schema-7 backup. User content, local files and provider credentials are excluded.

fal production integration is present; the Mac OpenRouter adapter remains pending. Live embeds of other widget types, export and collaboration are future scope.

## 0.9.0 · October 7, 2026

Vision adds marquee selection, additive Shift-click selection, atomic group movement, Cmd+C/V element copies, and Cmd+Shift+> / < resizing. Text bounds fit their content; selected text can become one rounded label. Paste preserves spacing and creates independent identities, including owned copies of image assets.

CLIPBOARD becomes a compact chip shelf: primary clicks copy URLs or open files, arrows open links, and paste + Enter infers link labels. Links/files can be dropped in. Its popout reuses the same view and draft, and restores floating state/geometry after restart. Saved files use owner-only permissions on Mac.

113 store, 253 headless UI, 50 Flow and 8 Meili tests pass. Schema 7 retains an exact v6 original before upgrading. Public screenshots are synthetic; reference media and generated-video URLs are excluded.

## 0.8.0 · October 7, 2026

Vision gained vector shapes and zoom controls. The view expands its working space when zoomed out while pointer operations continue to use board coordinates. Draft saves block view changes if persistence fails. Shape snapshots and recovery preserve geometry with independent identities.

CLIPBOARD adds a shared, renameable collection of links and saved file copies. URLs allow HTTP/HTTPS; file paths remain inside the managed shelf. Removed entries retain their saved files for restoration.

The Mac workspace advances from schema 5 to 6 with an exact original-file backup. Existing data is preserved. The preview still operates locally; Windows import and cross-device synchronization remain future work.

See README for build instructions, validation, screenshots, provider status and known limitations.
