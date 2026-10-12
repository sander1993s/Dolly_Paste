# Dolly Paste

Dolly Paste is a lightweight, fully local clipboard manager with native versions for Windows and macOS, a white, cool gray, and blue interface, and Merino sheep branding. The Windows application uses Windows Forms and the built-in .NET Framework runtime. The macOS implementation uses Swift and AppKit with Apple's built-in clipboard, Vision OCR, and Keychain APIs; neither application requires third-party runtime packages.

**macOS:** See [the Mac build and usage guide](macos/README.md) for macOS 13+, Intel and Apple silicon. Build with `bash macos/build.sh` on a Mac to produce a universal `.app` and ZIP. The Mac implementation has been added from a Windows development environment; native compilation, tests, and visual QA still need to pass on a Mac before a release is advertised. The [macOS CI workflow](.github/workflows/macos.yml) performs those build/test steps and saves a synthetic preview. The remaining commands and platform details on this page describe the Windows version.

Licensed under the [MIT License](LICENSE).

Website: [Smet Software Solutions](https://smetsoftwaresolutions.be).

The [SignPath integration](CODE_SIGNING.md) remains inactive: the Foundation has not approved the application. Existing direct downloads remain unsigned.

---

## Key Features

- **Local & Offline**: Operates purely on the local machine with no network connectivity, telemetry, cloud synchronization, background daemons, or installer requirements.
- **In-Memory by Default**: Clipboard history is kept in volatile memory and discarded upon exit unless encrypted persistence is explicitly enabled.
- **Bounded Retention**: Configurable time-based retention (1 to 43,200 minutes) and capacity (10 to 1,000 clips). Pinned items remain subject to both maximum capacity count and retention age limits, as well as a 4 MiB total payload cap.
- **Predictable History**: Pasting no longer censors or discards saved clips. Automatic password-field and paste-destination detection has been removed.
- **Images and Offline OCR**: Copied images are saved immediately as PNG base64, then Windows recognizes their text in the background on the device. Search finds recognized text as well as ordinary clips. Each image row offers **Image** (selected by default), recognized **Text** when available, and a **Base64** icon. Choose the representation to copy directly from the row; expand the preview for the full values and explicit text/base64 copy actions.
- **Fast Keyboard Workflow**:
  - `Ctrl+Shift+V`: Global shortcut to toggle Dolly Paste visibility.
  - `Ctrl+F`: Instantly focus search filter.
  - `Up` / `Down`: Choose a clip while typing in search. Selecting a different image row resets its copy target to **Image**.
  - `Left` / `Right`: Switch between Image, recognized Text, and Base64 for the selected image. Text is skipped when OCR has no available result. In search, this works at the corresponding edge of the text cursor so ordinary cursor movement stays available.
  - `Enter`: Copy the selected clip or image representation from search or the history list, then dismiss the popup.
  - `Ctrl+C`: Copy the selected clip or image representation from the list or search, including immediately after opening the popup. If search text is highlighted, copy that text instead.
  - `Ctrl+P`: Pin or unpin the selected clip from search or the list.
  - `Ctrl+Space`: Show or hide the full-value preview, including an image preview for image clips.
  - `Delete`: Remove selected clip.
  - `Esc`: Hide window to system tray.
- **Compact Native Popup**: A 480 × 520 clipboard picker with the SSS blue title bar, sheep branding, a padded close button, white full-width clip rows, and compact image-format controls. Search, All/Pinned navigation, content filters, and optional preview stay within the popup; Pause and Settings sit at the bottom right. Up/Down moves through clips in their existing order, while Left/Right switches formats only for images. The popup opens near the pointer, fits the current monitor, and stays out of the taskbar. Minimum size is 420 × 460, subject to the available screen space. The visual refresh adds no runtime, package, or network dependency.

---

## System Requirements

- **Operating System**: Windows 10 or Windows 11 (64-bit or 32-bit).
- **Runtime**: .NET Framework 4.8 (pre-installed on modern Windows 10 and 11 installations).
- **Text Recognition**: Uses the built-in Windows OCR engine and installed OCR language capabilities matching your Windows user language preferences. Microsoft documents desktop OCR support for apps installed with MSIX package identity; see [Windows OCR requirements](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr). Unpackaged OCR also passed the native smoke test on the development machine, but availability in a portable build depends on the Windows environment. If OCR is unavailable, image capture, image copying, and base64 copying still work.
- **Permissions**: Standard user privileges (`asInvoker`); no administrator rights or background Windows services required.
- **Display Scaling**: Uses Windows desktop DPI virtualization. Windows scales the interface at high DPI settings (e.g. 150%/200%); on high DPI displays the interface may look slightly softer.

---

## Building from Source

Clone the repository and enter its directory:

```powershell
git clone https://github.com/sander1993s/Dolly_Paste.git
cd Dolly_Paste
```

Building requires the **Windows 10 SDK** and **.NET Framework 4.8 Developer Pack** for compile-time Windows Runtime reference assemblies. The application uses Windows' built-in OCR engine at runtime; it does not bundle an OCR engine, download a model, or require those development tools on end-user machines.

The build uses the native .NET Framework C# compiler (`csc.exe`) included with Windows:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

The script locates `csc.exe`, resolves required framework, WPF, and Windows Runtime reference assemblies by full path, generates `dist\sheep.ico` directly from the vector geometry, and compiles the project into:

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

Click the sheep tray icon once or press `Ctrl+Shift+V` to open the popup. Search is focused automatically; each reopening starts with all clips and a collapsed preview. Type to filter, use the arrow keys to choose, then press `Enter` or `Ctrl+C`. You can also double-click a clip or use the main copy button. For image rows, click **Image**, **Text**, or **Base64** to select the representation first; the button reads **Copy image**, **Copy text**, or **Copy Base64** to match. The popup closes after a successful copy; use your application's usual paste shortcut to insert the image or text. Highlighted search text keeps normal `Ctrl+C` behavior.

Clicking elsewhere, the title-bar close button, or `Esc` hides the popup while recording continues. Settings stays available as a separate dialog. To completely terminate the application, right-click the sheep tray icon and select **Quit**.

Dolly Paste must be running when you copy or take a snip to record it. If a Snipping Tool image is missing, launch Dolly Paste and take a new snip (or copy the image again), then open Dolly Paste with `Ctrl+Shift+V` and check the Images filter.

### Text from Screenshots

Text recognition starts automatically after an image is captured, and does not change the source clipboard. An image row has an **Image** target, an optional recognized **Text** target, and a **Base64** icon. Image is selected by default and copies actual image data, ready to paste into an application that accepts pictures. Click a target or use `Left` / `Right` to choose it; the main copy button, `Enter`, and `Ctrl+C` copy that selected representation. Moving to another row resets image copying to Image. If recognition has no text available, the row shows its recognition status and keyboard navigation skips Text.

Expand the selected image's preview with `Ctrl+Space` to inspect its image, full base64 value, and recognized text. The preview's **Copy Base64** and **Copy text** buttons explicitly select and copy that format. **Copy text** becomes available when recognition returns text.

Recognized text is searchable and stays attached to its original image. Recognition does not refresh the image's capture time or extend its retention. An image with no readable text, unavailable OCR, or a failed recognition attempt remains available for image and base64 copying. If Windows reports OCR unavailable, check that an OCR language capability matching your user language preferences is installed through Windows language settings. OCR availability in the portable executable depends on the Windows environment; image capture and copying remain available when recognition cannot run.

### Demo Mode

To explore the interface without accessing or modifying your system clipboard:

```powershell
& "dist\Dolly Paste.exe" --demo
```

In demo mode:
- System clipboard monitoring and global hooks are completely disabled.
- Synthetic sample clips (notes, a link, a code snippet, and an image) are loaded into an isolated temporary store.
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

The test runner compiles five suites—CoreTests, PlatformTests, UiTests, OcrTests, and OcrIntegrationTests—into a scoped temporary directory outside the source tree, executes each suite directly, verifies exit codes, and cleans up temporary artifacts. OCR tests use synthetic inputs and injected engines to cover background scheduling, failure handling, retention, persistence, and copy behavior without reading personal clipboard data.

Select a suite with `-Suite Core`, `Platform`, `Ui`, `Ocr`, or `OcrIntegration`. To additionally exercise the installed Windows OCR engine against a generated local image:

```powershell
powershell -ExecutionPolicy Bypass -File tests/run-tests.ps1 -Suite Ocr -NativeOcrSmoke
```

The native smoke test requires an available Windows OCR language. It does not read or write the system clipboard.

---

## Storage and Privacy Model

1. **Local Storage Directory**:
   - The application directory (`%LOCALAPPDATA%\DollyPaste`) always stores application configuration in `settings.json`, even when clipboard history persistence is turned off.
   - Encrypted history storage (`history.dat`) is optional and only created when "Remember history after quitting (encrypted)" is explicitly enabled.

2. **Encrypted History Persistence**:
   - When history persistence is enabled in Settings, stored clips are encrypted using the Windows Data Protection API (DPAPI) tied to the current Windows user profile before writing to `history.dat`.
   - Recognized image text and OCR status are included in the same encrypted history record. With persistence disabled, extracted text remains in memory along with the image.

3. **Format Handling (Text and Images)**:
   - Dolly Paste reads standard plain text (`CF_UNICODETEXT`, including Windows conversions from `CF_TEXT`) and images (registered PNG format, with Windows bitmap fallback).
   - Images take priority when the source also supplies text. Image entries store raw PNG base64 without a `data:image/png;base64,` prefix. Capturing an image leaves the source clipboard intact. The Image copy target publishes the original PNG plus a standard bitmap (`CF_DIB`) fallback; PNG preserves transparency, while the fallback composites transparent pixels onto white for older applications. The Base64 and Text targets publish plain text instead.
   - Each PNG is limited to 3 MiB encoded bytes (4 MiB base64), 8,192 pixels per dimension, and 16,777,216 total pixels. Unsupported, malformed, or oversized images are skipped. Images share the existing 4 MiB total history payload cap and can cause older entries to be evicted.
   - OCR runs locally through Windows using installed language capabilities. Neither image bytes nor extracted text are sent to a server. Extracted text is capped at 32,768 characters and its UTF-8 bytes count toward the shared 4 MiB history limit. For an image already near that limit, extracted text is shortened to fit without changing its base64. If no recognized character fits, the image remains available and OCR reports a failure.
   - Deleting, clearing, expiring, or evicting an image also removes its recognized text. A late OCR result cannot restore the removed entry. Duplicate image captures retain the existing OCR result and original capture age.
   - HTML, RTF, shell file drops, and other custom binary streams are not captured directly. Copy image pixels from the source application; copying an image file in Explorer is a file drop, not an image payload.
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
