# Temporary sticky note editing

A plain left click on an expanded note's header makes levels 2–10 temporarily opaque and editable for five seconds. Click the text area during that interval to keep editing until an outside click, window deactivation, or another plain header click. The saved transparency is never changed. Level 1 is always editable. Collapsed notes remain collapsed. Dragging the header moves the note without toggling editing; header buttons keep their own actions.

While editing is held, a temporary low-level mouse hook observes button-down events to detect clicks on other windows, including windows that do not take activation. It does not block, modify, persist or transmit input. It is removed when editing ends. If Windows rejects or removes the hook, deactivation remains a fallback; clicks on nonactivating surfaces may then not end editing. Desktop changes, collapse, hide, minimize, transparency changes and closure also end the temporary state.

## Manual Windows checks

- Use a note at level 8 over a disposable window. Confirm body clicks normally reach the window below.
- Click the plain header and verify the note is fully opaque, editable, and returns to level 8 after five seconds if the text is not clicked.
- Repeat and click the text before five seconds; type after ten seconds and verify the note remains opaque. Click another app and verify the original opacity and click-through body return without swallowing the click. Repeat with a nonactivating surface.
- Repeat with a second header click, both during the initial preview and after holding editing. Verify immediate return to the saved level.
- Drag the header, use the collapse, options, new and close buttons, resize the note, switch desktops and change the saved transparency while temporarily editing. Verify no unexpected activation, stranded opaque body or lost edits.
- Verify a level 1 note remains editable and the saved level persists after closing/restarting BranchWatch. Test multiple notes independently.

Automated tests cover the editing state and WPF presentation without physical mouse interaction. Native hook behavior and passive window transitions require interactive Windows qualification.
