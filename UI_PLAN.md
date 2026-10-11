# Dolly Paste interface design

Dolly Paste uses the Blueprint design: a compact native clipboard picker with a 480 × 520 default size and a 420 × 460 minimum size, subject to the current monitor's available working area.

## Layout and interaction

- One SSS-blue title bar contains the sheep, app name, and a close button inset from the edges. The title bar supports dragging and the window retains resize handles.
- White search and All/Pinned controls sit in the blue header area, above a compact row of content filters.
- Clips remain full-width in one vertical list. Selecting a clip never reorders the history. Image rows offer small Image, Text, and Base64 targets; text, links, and code use compact rows.
- Pause and Settings sit at the bottom right. The extra branding subtitle is omitted.
- Copying, pinning, and deletion remain available in the action bar. Full-text preview is optional and leaves the clip list usable when expanded.
- The popup opens near the pointer, fits the current monitor, focuses search, and resets stale searches and filters.
- Up/Down selects clips from search or the list. Left/Right changes the copy format only for images, preserving ordinary search caret movement away from the text edges. Enter and Ctrl+C copy the selected clip; highlighted search text keeps normal Ctrl+C behavior. Ctrl+P toggles pinning and Ctrl+Space toggles preview.
- Successful live copies, Escape, and clicking away dismiss the popup. Failed copies leave it available, and an owned Settings dialog does not trigger dismissal.
- A single tray click or Ctrl+Shift+V opens the picker. Demo and preview modes use synthetic data without accessing the system clipboard.

## Validation criteria

- Search, filters, actions, and optional preview fit at default and minimum sizes.
- Close-button padding, title dragging, edge/corner resizing, full-width rows, and footer controls remain usable at both sizes.
- Keyboard selection preserves search editing shortcuts.
- Popup positioning supports negative monitor coordinates and small working areas.
- Copying restores the selected text to the clipboard; the user pastes with the destination application's usual shortcut.
- Core, platform, and interface regression suites pass.

See [VERIFICATION.md](VERIFICATION.md) for the validation workflow and results.
