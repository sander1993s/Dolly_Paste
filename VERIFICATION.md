# Dolly Paste verification

## Blueprint popup — 11 October 2026

- Implemented the approved SSS-blue title/search area with the sheep beside the title, an inset close button, and Pause/Settings at the bottom right. The default window is 480 × 520; the minimum remains 420 × 460. Native frame sizing remains available around the custom caption.
- All clips use full-width rows in stable history order. Regular rows are 42 pixels high; image rows are 90 pixels with a thumbnail, recognized-text snippet, and Image/Text/Base64 controls. Up/Down selects clips; Left/Right changes only image formats and preserves normal search-caret editing.
- The prior UI baseline passed 30 tests. The updated UI suite passed 32 tests, including title/footer layout, close-to-tray behavior, all eight resize hit regions, minimum/default layouts, image-only horizontal navigation, stable mixed-content order, and the existing OCR/copy/privacy coverage. Desktop UI and platform tests were run separately to avoid native focus contention.
- Core: 42 tests passed. Platform: 333 assertions passed. OCR service: 9 tests passed. OCR history integration: 5 tests passed. The Windows executable compiled successfully with the existing .NET Framework toolchain.
- Inspected synthetic native screenshots at 480 × 520 and 420 × 460 with the image preview open and closed, plus Settings at default and minimum sizes. Fixed a minimum-size preview clipping issue by reserving enough list space for the image row and container padding. Screenshots and tests did not read or replace the user's clipboard.
- No new runtime packages, browser components, network calls, or Store packaging are included in this update. Earlier MSIX checks below describe historical local development artifacts, not a published package.

## Image copy targets and SSS redesign — 10 October 2026

- Image rows have independently highlighted Image, OCR text (when present), and Base64 targets. Image is the default; click or Left/Right changes the target and updates the primary copy label. Enter, Ctrl+C, double-click, and preview copy actions use the chosen representation. Selecting another row resets to Image; background OCR completion preserves a valid selection.
- Actual image copying publishes the original PNG and a standard bitmap fallback, after history/cloud exclusion metadata. Synthetic native-buffer tests check dimensions, row layout, transparent-PNG preservation, fallback pixels, contention, partial publication, and handle ownership. Platform: 333 assertions passed.
- Interface: 30 tests passed, including target navigation, mouse and double-click behavior, search caret handling, refresh preservation, copy routing, expiry/protection, and compact layout. Synthetic captures were inspected for all three selected targets and expanded previews at 480 × 560 and 420 × 460. Settings was inspected at 560 × 620 and 520 × 520; the minimum size scrolls vertically without a horizontal bar.
- The native interface uses the SSS website's blue (#1A73E8), white, cool-gray (#F8F9FA), border (#DADCE0), and system-font styling. Rounded controls retain native keyboard behavior and accessibility. Row rendering reuses fonts and bounded cached thumbnails; refreshes retain unaffected thumbnails. No new runtime packages, web view, image assets, animation loops, or network calls were added.
- A generated 1920 × 1080 image published through an injected native buffer writer in a 14.4 ms median over ten warm runs (22.4 ms maximum). Process peak working set rose approximately 23 MiB in that test. Maximum-sized images can require substantially more transient conversion memory; this is not a global memory ceiling. Tests did not access the user's clipboard.
- The executable compiled successfully, and the unsigned development MSIX passed MakeAppx schema/semantic validation and pack/unpack payload-hash checks. It remains a local development package, not a Store submission.

## Local image OCR — 9 October 2026

- Images retain their original PNG base64 while Windows OCR recognizes text locally in a bounded background queue. Extracted text is searchable and stored with the image; image history rows and expanded previews show base64 on the left and OCR on the right, with separate preview copy actions.
- Core: 42 tests passed, including encrypted OCR persistence, legacy image loading, late-result guards, linked protection, and shared payload limits. Platform: 286 assertions passed. OCR service: 9 tests passed. OCR history integration: 5 tests passed, including backlog refill, deletion/recapture identity, expiration/protection after completion, shutdown, and startup retry.
- Native Windows OCR passed against generated multiline text, an oversized image requiring downscaling, a blank image, and malformed input. This worked in the unpackaged executable on this machine; Microsoft documents MSIX package identity as the supported desktop configuration. OCR failure leaves the original base64 available.
- Synthetic split previews were visually checked at 420 × 520 and 380 × 420, including unavailable OCR. Validation used generated images and did not read or replace personal clipboard contents.
- Interface: all 27 checks passed, including OCR search refresh, separate copy actions, terminal states, and compact split layouts. Image base64 previews use horizontal scrolling: an 80 KB fixture opened in 0.051 seconds instead of 35.575 seconds spent wrapping the long value, and resizing fell from 13.812 seconds to 0.019 seconds. Ordinary text and OCR remain wrapped.
- The release executable compiled successfully. An unsigned development MSIX passed MakeAppx schema/semantic validation and pack/unpack payload-hash checks. It uses a local development identity and is not a Store submission or an installed package.

## Snipping Tool capture check — 9 October 2026

- Fixed image reads being discarded when delayed clipboard rendering advances the sequence number. The reader retries up to three complete snapshots, rechecks producer exclusions, releases rejected bitmaps, and uses the accepted sequence for dispatch and corrupt-PNG fallback.
- The release build compiled successfully. Core: 37 tests passed; platform: 286 assertions passed; interface: 25 checks passed. New injected-buffer cases cover delayed PNG/bitmap rendering, privacy changes during retries, stale notifications, bounded retries, and bitmap disposal.
- The complete test runner passed in the normal Windows environment. The sandbox run could not exercise user-bound encrypted persistence; its DPAPI-related failures did not recur outside the sandbox.
- A diagnostic read of the current Snipping Tool image converted it to PNG base64 with both the previous and rebuilt executable. The diagnostic processed the image only in memory and printed counts and metadata, without saving, printing, or replacing its contents. Dolly Paste was not running during the initial diagnosis; the rebuilt app was launched and confirmed responding afterward. A fresh user snip remains the final interactive check.

## Image update check — 7 October 2026

- The release build compiled all seven source files successfully.
- Core: 37 tests passed, including PNG alpha preservation, invalid-image rejection, large base64 values, deduplication, retention, and encrypted image/text reloads.
- Interface: 25 checks passed, including image filtering, full base64 preview, production image callback wiring, and disposal of preview images.
- Platform: 265 assertions passed. Image checks use injected native buffers and test-owned bitmaps to cover PNG/bitmap capture, corrupt-PNG fallback, producer exclusions, sequence changes, and exact base64 publication beyond the text capture limit.
- Rendered synthetic previews were inspected at 420 × 520 and 380 × 420. Image previews remain readable beside the value at minimum size with one full history row and the action toolbar visible.

The complete test runner passed; the additional platform image cases were verified separately. Validation did not read or replace the user's clipboard. The previous local executable was backed up before rebuilding `dist\Dolly Paste.exe`.

## Publication check — 20 September 2026

Validation on Windows with .NET Framework completed successfully:

| Check | Result |
|---|---|
| Release build from an isolated source copy | Exit 0; all six source files compiled |
| Core suite | 30 passed, 0 failed |
| Platform suite | 224 assertions passed, 0 failed |
| Interface suite | 24 checks passed, 0 failures |
| Complete test runner | Exit 0; no compiler or runtime warnings |

The build used a temporary copy to preserve any existing local executable. Validation used synthetic test data and did not launch normal clipboard capture.

## Reproducing validation

Run from the repository root on Windows with .NET Framework 4.8, its Developer Pack, and the Windows 10 SDK:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File build.ps1
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tests/run-tests.ps1
# Optional real Windows OCR check using synthetic images:
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tests/run-tests.ps1 -Suite Ocr -NativeOcrSmoke
```

The test runner compiles each suite in a uniquely named temporary directory and removes its generated artifacts when finished. The Windows CI workflow executes the same build and test scripts.

## Coverage

- **Core:** text and image retention, pin eviction, capacity and payload limits, bounded PNG decoding, encrypted persistence, corrupt storage handling, and legacy sensitive-entry behavior.
- **Platform:** clipboard image capture and base64 publication seams, Windows metadata handling with test-owned controls, bounded queues, and legacy protection helper regressions.
- **Interface:** image thumbnails and full base64 previews, compact and expanded layouts, search and keyboard shortcuts, settings, screen bounds, popup dismissal, guarded copying, command-line parsing, and removal of automatic protection hooks.
- **OCR:** native image decoding/downscaling, local recognition, bounded queue and text, cancellation, late-result rejection, startup retry, encrypted persistence, searchable results, separate copy actions, and split-preview layouts.

Tests use synthetic data, injected clipboard operations, and test-owned native controls. They do not read or replace the user's real clipboard.

## Visual checks

Use `--demo` for interactive inspection with synthetic data. A portable screenshot command is:

```powershell
$previewPath = Join-Path (Get-Location).Path 'dist\preview.png'
& '.\dist\Dolly Paste.exe' --preview $previewPath
```

Inspect the default and minimum popup sizes, expanded preview, and Settings dialog when changing the interface. Windows provides desktop DPI virtualization; high display scaling can make the interface appear softer.

## Limits

Automated tests do not cover every third-party application's clipboard behavior or the global shortcut on every desktop configuration. Normal clipboard capture and the Windows clipboard remain separate from the synthetic test fixtures. Dolly Paste does not claim forensic memory erasure.
## macOS port — 12 October 2026

The native macOS implementation lives in `macos/`. GitHub's Mac runner successfully compiled the app for both arm64 and x86_64 and passed all 47 native/history/UI tests; the Linux Swift 5.9 run also passed all 24 portable core tests. The initial Mac run caught an argument-order error in the build script's final `lipo -verify_arch` command after compilation; that packaging check was corrected. Installer creation and preview verification must also pass before publication. See [Mac test and manual verification instructions](macos/README.md#tests-and-release-verification).

The existing five Windows regression suites were rerun successfully during the port: Core 42, Platform 333 assertions, UI 32, OCR service 9, and OCR integration 5. The initial sandboxed attempt failed on temporary-file/DPAPI access; a normal local run passed all suites. All 18 Swift files parsed without syntax errors using tree-sitter Swift, both shell scripts passed `bash -n`, and the plist and workflow passed structural checks. These checks do not establish native Swift type correctness or successful execution.

The Mac suite includes 24 portable history tests, 20 platform tests, and three native picker tests. Platform tests use isolated named pasteboards and injected encryption keys. The UI tests render the native picker and check controls at normal and minimum size, reopen behavior, and keyboard/image-format selection. Real user Keychain access, general-clipboard behavior across third-party apps, and Gatekeeper first-launch behavior still need broader manual testing.
