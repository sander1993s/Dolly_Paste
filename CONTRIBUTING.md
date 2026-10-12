# Contributing to Dolly Paste

For the native macOS implementation, use the [Mac development guide](macos/README.md). Run `bash macos/test.sh` and `bash macos/build.sh` on macOS 13+ with Xcode 15 / Swift 5.9 or later, then inspect demo mode. Portable history tests can also run on Linux. The instructions below cover the Windows implementation; changes to shared product behavior should consider both versions.

Use Windows 10 or Windows 11 with .NET Framework 4.8 and Windows PowerShell 5.1. Install the Windows 10 SDK and .NET Framework 4.8 Developer Pack for the Windows Runtime compile-time references used by OCR. The project builds with the framework compiler included with Windows; no package restore is required. End users only need the Windows runtime and installed OCR language capabilities, not the SDK or Developer Pack.

## Development workflow

1. Fork the repository and create a branch for your change.
2. Keep changes focused and update documentation when behavior changes.
3. Build and run the existing test suites from the repository root:

   ```powershell
   powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File build.ps1
   powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tests/run-tests.ps1
   ```

4. For interface changes, inspect the application with synthetic clipboard data:

   ```powershell
   & '.\dist\Dolly Paste.exe' --demo
   ```

5. Open a pull request explaining the change and the validation you performed.

The test runner accepts `-Suite Core`, `Platform`, `Ui`, `Ocr`, or `OcrIntegration` for a focused run. To test the installed Windows OCR engine with a generated local image, run:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tests/run-tests.ps1 -Suite Ocr -NativeOcrSmoke
```

This optional native smoke test requires an installed OCR language matching the Windows user language preferences and leaves the clipboard untouched. Ordinary OCR tests use injected engines. Run encrypted persistence tests with access to the normal Windows user's DPAPI profile.

Microsoft documents desktop Windows OCR support for MSIX apps with package identity; see [Windows OCR requirements](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr). The native smoke test also passed in the unpackaged development executable on the development machine. Keep the unavailable/failure paths working so portable users can still capture images and copy either their image data or base64 when OCR cannot run.

## Interface and image copy behavior

Use the Smet Software Solutions website's white, cool gray, and blue palette with Dolly Paste's sheep branding. Keep the interface in native Windows Forms: the visual refresh adds no runtime, external package, browser component, or network dependency. The default popup is 480 × 520, with a 420 × 460 minimum, constrained by available monitor space. The blue title bar contains the sheep, app name, and padded close button; Pause and Settings belong at the bottom right. Clip rows span the list width and preserve history order during selection. Only image formats use horizontal navigation.

An image row exposes three copy targets: **Image** (the default), recognized **Text** when OCR has completed with a usable result, and a **Base64** icon. Clicking a target or pressing Left/Right selects it; keyboard navigation skips unavailable OCR text. Up/Down or another row selection resets the target to Image. Search-field arrow handling must preserve ordinary cursor movement and only switch targets at the corresponding cursor edge.

The main copy button must describe the selected action: **Copy image**, **Copy text**, or **Copy Base64**. Enter, Ctrl+C, and the main button copy the selected representation. The expanded preview's explicit Copy text and Copy Base64 actions select and copy their named format. Preserve the current target when an OCR update refreshes the same selected row, and fetch the chosen payload from the live history store before publication so expired or deleted data cannot be copied from a stale row snapshot.

Actual image publication uses the original registered PNG format plus a standard 24-bit `CF_DIB` fallback; PNG retains alpha, while the DIB fallback composites onto white. Allocate and prepare image buffers before opening or emptying the clipboard, publish the existing history/cloud exclusion metadata before either image format, and preserve Windows ownership of transferred native handles. Base64 and OCR remain Unicode text copy paths. Platform tests exercise publication through an injected writer with synthetic images, including pixel orientation, transparency, failure cleanup, and bounded clipboard-open retries; tests must not replace the real clipboard.

## Project layout

- `src/`: clipboard storage, Windows integration, and the native interface.
- `tests/`: core, platform, interface, OCR engine, and OCR history integration regression suites.
- `assets/`: Dolly Paste's sheep artwork.
- `build.ps1`: compiler setup and executable/icon generation.

Tests should use synthetic text, injected clipboard operations, and test-owned controls. Preserve the clipboard-free behavior of demo and preview modes. Do not include real clipboard history, credentials, personal paths, or unrelated project material in issues, screenshots, fixtures, or contributions.

Build output and local scratch work are ignored by Git. The Windows CI workflow builds the application and runs all five test suites on pushes and pull requests. The native OCR smoke test remains an explicit local check.

## Reporting bugs

Include your Windows version, display scaling when relevant, reproduction steps, expected behavior, and actual behavior. Use synthetic examples and redact personal information from screenshots.

## License

Contributions are distributed under the repository's [MIT License](LICENSE).
