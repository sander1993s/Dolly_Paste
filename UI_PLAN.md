# Dolly Paste interface design

Dolly Paste uses a compact native clipboard picker with a 420 × 520 default size and a 380 × 420 minimum size, subject to the current monitor's available working area.

## Layout and interaction

- A branded header, full-width search, All/Pinned navigation, and content filters keep common tasks within the popup.
- Copying, pinning, and deletion remain available in the action bar. Full-text preview is optional and leaves the clip list usable when expanded.
- The popup opens near the pointer, fits the current monitor, focuses search, and resets stale searches and filters.
- Arrow keys select clips from search. Enter and Ctrl+C copy the selected clip; highlighted search text keeps normal Ctrl+C behavior. Ctrl+P toggles pinning and Ctrl+Space toggles preview.
- Successful live copies, Escape, and clicking away dismiss the popup. Failed copies leave it available, and an owned Settings dialog does not trigger dismissal.
- A single tray click or Ctrl+Shift+V opens the picker. Demo and preview modes use synthetic data without accessing the system clipboard.

## Validation criteria

- Search, filters, actions, and optional preview fit at default and minimum sizes.
- Keyboard selection preserves search editing shortcuts.
- Popup positioning supports negative monitor coordinates and small working areas.
- Copying restores the selected text to the clipboard; the user pastes with the destination application's usual shortcut.
- Core, platform, and interface regression suites pass.

See [VERIFICATION.md](VERIFICATION.md) for the validation workflow and results.
