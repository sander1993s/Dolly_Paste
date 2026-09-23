# Dolly Paste

Dolly Paste is a lightweight, fully local Windows clipboard manager built with a warm wool aesthetic and minimalist Merino sheep branding. It runs without external packages or services using the built-in .NET Framework runtime on Windows 10 and Windows 11.

Licensed under the [MIT License](LICENSE).

Website: [Smet Software Solutions](https://smetsoftwaresolutions.be).

Trusted code signing is being prepared. No SignPath Foundation application has been submitted or approved yet. See the [proposed signing policy and setup](CODE_SIGNING.md); existing downloads are not changed by this preparation.

---

## Key Features

- **Local & Offline**: Operates purely on the local machine with no network connectivity, telemetry, cloud synchronization, background daemons, or installer requirements.
- **In-Memory by Default**: Clipboard history is kept in volatile memory and discarded upon exit unless encrypted persistence is explicitly enabled.
- **Bounded Retention**: Configurable time-based retention (1 to 43,200 minutes) and capacity (10 to 1,000 clips). Pinned items remain subject to both maximum capacity count and retention age limits, as well as a 4 MiB total payload cap.
- **Predictable History**: Pasting no longer censors or discards saved clips. Automatic password-field and paste-destination detection has been removed.
- **Fast Keyboard Workflow**:
  - `Ctrl+Shift+V`: Global shortcut to toggle Dolly Paste visibility.
  - `Ctrl+F`: Instantly focus search filter.
  - `Up` / `Down`: Choose a clip while typing in search.
  - `Enter`: Copy the selected clip from search or the history list, then dismiss the popup.
  - `Ctrl+C`: Copy the selected clip from the list or search, including immediately after opening the popup. If search text is highlighted, copy that text instead.
  - `Ctrl+P`: Pin or unpin the selected clip from search or the list.
  - `Ctrl+Space`: Show or hide the full-text preview.
  - `Delete`: Remove selected clip.
  - `Esc`: Hide window to system tray.
- **Compact Native Popup**: A 420 × 520 clipboard picker with warm wool colors, sheep branding, search, All/Pinned navigation, content filters, and an optional full-text preview. The popup opens near the pointer, fits the current monitor, and stays out of the taskbar. Minimum size is 380 × 420, subject to the available screen space.

---

## System Requirements

- **Operating System**: Windows 10 or Windows 11 (64-bit or 32-bit).
- **Runtime**: .NET Framework 4.8 (pre-installed on modern Windows 10 and 11 installations).
- **Permissions**: Standard user privileges (`asInvoker`); no administrator rights or background Windows services required.
- **Display Scaling**: Uses Windows desktop DPI virtualization. Windows scales the interface at high DPI settings (e.g. 150%/200%); on high DPI displays the interface may look slightly softer.

---

## Building from Source

Clone the repository and enter its directory:

```powershell
git clone https://github.com/sander1993s/Dolly_Paste.git
cd Dolly_Paste
```

The build uses the native .NET Framework C# compiler (`csc.exe`) included with Windows:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

The script locates `csc.exe`, resolves required framework and WPF reference assemblies by full path, generates `dist\sheep.ico` directly from the vector geometry, and compiles the project into:

```text
dist\Dolly Paste.exe
```

---

## Running Dolly Paste

### Normal Mode

Launch the compiled executable:

```powershell
& "dist\Dolly Paste.exe"
```

Dolly Paste runs as a single-instance application using a named local mutex. If launched while already running, the existing instance is brought to the foreground.

Click the sheep tray icon once or press `Ctrl+Shift+V` to open the popup. Search is focused automatically; each reopening starts with all clips and a collapsed preview. Type to filter, use the arrow keys to choose, then press `Enter` or `Ctrl+C`. You can also double-click a clip or click **Copy**. The popup closes after a successful copy; use your application's usual paste shortcut to insert the text. Highlighted search text keeps normal `Ctrl+C` behavior.

Clicking elsewhere, the title-bar close button, or `Esc` hides the popup while recording continues. Settings stays available as a separate dialog. To completely terminate the application, right-click the sheep tray icon and select **Quit**.

### Demo Mode

To explore the interface without accessing or modifying your system clipboard:

```powershell
& "dist\Dolly Paste.exe" --demo
```

In demo mode:
- System clipboard monitoring and global hooks are completely disabled.
- Synthetic sample clips (notes, a link, and a code snippet) are loaded into an isolated temporary store.
- A prominent banner indicates "Preview mode — recording disabled".
- Copy actions are simulated and leave the popup open. Clicking away does not dismiss demo or screenshot previews.

### Visual QA Preview Mode

Visual verification scripts can render a screenshot of the synthetic layout directly to a PNG file:

```powershell
& "dist\Dolly Paste.exe" --preview "C:\absolute\path\to\output.png"
```

This renders the interface via `DrawToBitmap` after layout settles, writes the specified PNG file, and exits cleanly with exit code 0 on success, without reading or altering user data.

---

## Running Tests

Execute the integrated test suite:

```powershell
powershell -ExecutionPolicy Bypass -File tests/run-tests.ps1
```

The test runner compiles the CoreTests, PlatformTests, and UiTests suites into a scoped temporary directory outside the source tree, executes each suite directly, verifies exit codes, and cleans up all temporary artifacts.

---

## Storage and Privacy Model

1. **Local Storage Directory**:
   - The application directory (`%LOCALAPPDATA%\DollyPaste`) always stores application configuration in `settings.json`, even when clipboard history persistence is turned off.
   - Encrypted history storage (`history.dat`) is optional and only created when "Remember history after quitting (encrypted)" is explicitly enabled.

2. **Encrypted History Persistence**:
   - When history persistence is enabled in Settings, stored clips are encrypted using the Windows Data Protection API (DPAPI) tied to the current Windows user profile before writing to `history.dat`.

3. **Format Handling (`CF_UNICODETEXT`)**:
   - Dolly Paste exclusively reads standard plain text (`CF_UNICODETEXT` or `CF_TEXT`) from the system clipboard.
   - Other clipboard formats (HTML, RTF, images, shell file drops, custom binary streams) are ignored entirely. They are not sanitized, stripped, or converted with a security guarantee.
   - Plain text clips are capped at a maximum of 32,768 characters per item.

4. **Retention Bounds and Eviction**:
   - History retention is strictly bounded by both time (1 to 43,200 minutes) and maximum capacity (10 to 1,000 clips).
   - **Pinned Items**: Pinned items remain subject to BOTH maximum capacity limits and retention age limits, as well as the global 4 MiB store payload cap. When limits are exceeded, oldest items are pruned.

5. **Clipboard Capture Behavior**:
   - Automatic password-field and paste-destination censoring is removed. Dolly Paste no longer installs a paste-detection keyboard hook, subscribes to accessibility focus changes, or starts destination-probing workers. The clipboard listener and `Ctrl+Shift+V` shortcut remain active.
   - Source applications can still mark their clipboard content as excluded from capture through standard clipboard metadata. These flags skip capture; they do not replace existing clips with protected placeholders.
   - Restoring a clip still marks the published clipboard payload as excluded from Windows clipboard history and cloud clipboard synchronization.
   - Previously censored entries contain no recoverable text. Copy their original text again to create usable clips. Legacy protection settings in an older settings file no longer enable automatic censoring.
   - **Windows System Clipboard**: Windows clipboard history (`Win+V`) operates independently at the OS level and cannot be modified or cleared by Dolly Paste.
   - **Memory Reusability**: Dolly Paste relies on standard runtime garbage collection and does not claim forensic secure memory wiping or cryptographic zeroing of deallocated memory strings.

## Contributing

Bug reports and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for the development workflow and [VERIFICATION.md](VERIFICATION.md) for validation coverage.

## License

Copyright (c) 2026 sander1993s. Distributed under the [MIT License](LICENSE).
