# Development progress

## 0.9.0 · October 7, 2026

Vision adds marquee selection, additive Shift-click selection, atomic group movement, Cmd+C/V element copies, and Cmd+Shift+> / < resizing. Text bounds fit their content; selected text can become one rounded label. Paste preserves spacing and creates independent identities, including owned copies of image assets.

CLIPBOARD becomes a compact chip shelf: primary clicks copy URLs or open files, arrows open links, and paste + Enter infers link labels. Links/files can be dropped in. Its popout reuses the same view and draft, and restores floating state/geometry after restart. Saved files use owner-only permissions on Mac.

113 store, 253 headless UI, 50 Flow and 8 Meili tests pass. Schema 7 retains an exact v6 original before upgrading. Public screenshots are synthetic; reference media and generated-video URLs are excluded.

## 0.8.0 · October 7, 2026

Vision gained vector shapes and zoom controls. The view expands its working space when zoomed out while pointer operations continue to use board coordinates. Draft saves block view changes if persistence fails. Shape snapshots and recovery preserve geometry with independent identities.

CLIPBOARD adds a shared, renameable collection of links and saved file copies. URLs allow HTTP/HTTPS; file paths remain inside the managed shelf. Removed entries retain their saved files for restoration.

The Mac workspace advances from schema 5 to 6 with an exact original-file backup. Existing data is preserved. The preview still operates locally; Windows import and cross-device synchronization remain future work.

See README for build instructions, validation, screenshots, provider status and known limitations.
