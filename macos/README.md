# Dolly Paste for macOS

A native Swift/AppKit port of Dolly Paste for **macOS 13 Ventura or later**, on **Apple silicon and Intel**. It follows the Windows app's capture, search, pin, retention, preview, and copy workflow using Apple's platform APIs. No Electron, .NET installation, package downloads, server, telemetry, or account is required to run it.

## Download and install

End users do **not** need Terminal, Xcode, or build commands. Download **Dolly-Paste-macOS.dmg** from the [Mac preview release](https://github.com/sander1993s/Dolly_Paste/releases/tag/macos-v1.0.0-beta.1), open it, and drag **Dolly Paste** into **Applications**. Eject the disk image, then open the app from Applications. The ZIP download contains the same app if you prefer to extract it yourself.

**This preview is not Developer ID signed or notarized by Apple.** If macOS blocks the first launch, and you trust the download, use **System Settings → Privacy & Security → Open Anyway** after trying to open Dolly Paste. See [Apple's instructions](https://support.apple.com/102445). Managed Macs may not permit an exception. No security setting needs to be disabled globally.

The maintainer currently has no Apple Developer signing access. A future Developer ID signed and notarized release can use the same drag-to-Applications installation without that unsigned-app exception. Build commands below are for maintainers only.

## Build and package (maintainers only)

Install Apple's Xcode Command Line Tools (Xcode 15 / Swift 5.9 or newer), then run from the repository root on a Mac:

```bash
xcode-select --install  # only if development tools are not installed
bash macos/test.sh
bash macos/build.sh
bash macos/package.sh
open "dist/macos/Dolly Paste.app"
```

The build script compiles both CPU architectures, generates the sheep icon, and produces:

```text
dist/macos/Dolly Paste.app
dist/macos/Dolly-Paste-macOS.dmg
dist/macos/Dolly-Paste-macOS.zip
dist/macos/SHA256SUMS.txt
```

Copy the app to Applications if desired. The app runs in the menu bar without a Dock icon. A second normal launch opens the existing instance. No launch agent or login item is installed. To start at login, add the app using macOS Login Items yourself.

Local builds use an ad-hoc code signature. This is not a Developer ID signature or notarization; it does not establish publisher trust for downloaded copies. If Apple signing access is obtained, set `CODESIGN_IDENTITY` to an installed Developer ID Application identity and `NOTARY_PROFILE` to a notarization credential profile already stored in Keychain before running `macos/package.sh`. That optional path signs the app, submits it to Apple, requires an Accepted result, staples the app, then signs/notarizes/staples the DMG. Both final downloads contain the stapled app. Do not commit certificates, private keys, or notarization credentials. Keep a stable signing identity across updates to avoid Keychain access prompts. See [Apple's distribution guide](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution).

To publish a new downloadable preview, update `Info.plist` if the app version changes, commit the reviewed source, and push a matching `macos-vX.Y.Z-beta.N` tag. The [release workflow](../.github/workflows/release-macos.yml) compiles and tests on a Mac, generates/verifies the DMG and ZIP, checks their SHA-256 hashes, then publishes a GitHub prerelease with installation instructions and the signing limitation. It refuses to replace an existing release. The current automated path publishes without Developer ID signing, consistent with the maintainer's available access.

## Everyday use

Click the sheep menu-bar icon or press **⌘⇧V**. Copy text or take a screenshot to the clipboard while Dolly Paste is running; on macOS, **⌃⇧⌘4** copies a selected screenshot region. Screenshots saved only as files are not clipboard images. The app begins observing new changes after launch; it does not import whatever was already on the clipboard.

Search text or recognized image text, choose an item, and copy it with Return, ⌘C, a double-click, or the Copy button. The popup closes after a successful copy. Return to your destination and press **⌘V**. For images, select **Image**, **Text** (when recognized), or **Base64** before copying. The full preview has explicit OCR-text and base64 copy actions. Moving to another image selects Image again.

Each opening resets search, filters, and the expanded preview. Clicking away or pressing Escape hides the picker while capture continues. Right-click the sheep icon for Pause, Settings, and Quit. Pause stops new captures; retention continues to expire existing history.

| Windows action | Mac equivalent |
| --- | --- |
| Ctrl+Shift+V — toggle picker | ⌘⇧V |
| Ctrl+F — search | ⌘F |
| Up / Down — select clip | ↑ / ↓ |
| Left / Right — choose image representation | ← / →; in search, only at the cursor's corresponding edge |
| Enter / Ctrl+C — copy selected clip | Return / ⌘C; highlighted search/preview text keeps native Copy |
| Ctrl+P — pin/unpin | ⌘P |
| Ctrl+Space — full preview | ⌘⇧Space or Preview button; ⌘Space also works if Spotlight doesn't consume it |
| Delete selected clip | Delete / forward Delete while the history list is focused |
| Esc — hide | Escape |

If another app owns ⌘⇧V, the picker reports it and remains accessible from the menu bar. Background capture may require allowing clipboard access in macOS's per-app privacy settings on system versions that prompt for it. Dolly Paste does not request Accessibility or Screen Recording access and does not simulate a paste into another app. Apple's [pasteboard access documentation](https://developer.apple.com/documentation/appkit/nspasteboard/accessbehavior-swift.enum) describes the system-controlled Allow/Ask/Deny behavior.

## Feature parity and platform differences

| Behavior | macOS implementation |
| --- | --- |
| Text, links, code, images; All/Pinned and type filters | Same workflow, native AppKit controls |
| Local OCR | Apple Vision, automatic supported-language detection, one image at a time off the UI thread |
| Image/Base64/OCR text copy | PNG and TIFF for image paste; plain text for the other representations |
| Default history | In memory; 24-hour retention, 200 clips |
| Configurable bounds | 1–43,200 minutes, 10–1,000 clips, total 4 MiB UTF-8 payload including base64 and OCR |
| Pins | Expire normally; unpinned clips evicted first, pins still subject to count/byte caps |
| Deduplication | Promotes the same payload without renewing capture time or losing pin/OCR state |
| Optional encrypted persistence | AES-256-GCM authenticated encryption; random key in the current user's local, nonsynchronizing login Keychain |
| Clipboard notifications | Polls `NSPasteboard.changeCount` approximately every 400 ms; rapid intermediate copies can be missed between polls |
| Source exclusions | Skips concealed, transient, autogenerated, confidential, own-copy, and common password-manager markers |
| Clipboard publication | Uses Apple's `.currentHostOnly` option to keep restored content out of Universal Clipboard |
| Demo / screenshot preview | Synthetic data; no general clipboard, global hotkey, user settings/history, or Keychain access |

The app does not censor an entry based on the app or field where it is pasted. Other clipboard apps and the operating system remain independent. Universal Clipboard on the *source* content is controlled by the source app and macOS; Dolly Paste controls only its own writes. Windows history files cannot be imported directly because Windows DPAPI and Mac Keychain encryption are different.

Text is limited to 32,768 UTF-16 code units, preserving surrogate pairs for OCR truncation. PNG payloads are limited to 3 MiB (4 MiB as base64), 8,192 pixels per dimension, and 16,777,216 pixels total. TIFF clipboard images are validated before conversion to PNG. File references, RTF, HTML, and arbitrary binary formats are not imported as clips. Copy actual image pixels rather than a file in Finder.

OCR text is searchable and shares its original image's retention. Deleting, clearing, expiring, or evicting an image also removes its OCR; late results cannot recreate it. An OCR failure leaves Image and Base64 copying available. Recognition results and supported languages can differ from Windows OCR. See [Apple Vision text recognition](https://developer.apple.com/documentation/vision/vnrecognizetextrequest).

## Storage

Settings are saved to `~/Library/Application Support/Dolly Paste/settings.json`. History stays in memory unless **Save history between launches (encrypted)** is enabled. When enabled, `history.dat` contains authenticated ciphertext, including recognized text, with no plaintext-history fallback. The encryption key is a nonsynchronizing generic-password item in the local login Keychain. It uses the Mac file-based Keychain so locally built apps can access it without provisioning; Keychain can ask for user approval. These are not the iOS/device-only keychain semantics. See [Apple's Mac Keychain overview](https://developer.apple.com/documentation/technotes/tn3137-on-mac-keychains).

The data directory and files use user-only permissions (0700/0600), and writes replace the saved file atomically. Disabling persistence deletes saved history. Clearing history removes pinned and unpinned clips and saved history, but does not erase the current system clipboard or other apps' histories. If encrypted history cannot be read, Dolly Paste preserves that file and keeps the new session in memory with a warning. Clear History or disable persistence explicitly to discard the unreadable file and start again. The app does not claim forensic memory or disk wiping.

## Demo and visual QA

```bash
"dist/macos/Dolly Paste.app/Contents/MacOS/DollyPaste" --demo
"dist/macos/Dolly Paste.app/Contents/MacOS/DollyPaste" --preview /tmp/dolly-paste-preview.png
```

Preview requires an absolute PNG path with an existing parent directory and writes only that requested output. Copying in either mode is simulated, and clicking away does not dismiss the demo. These modes can run alongside a normal instance.

## Tests and release verification

`bash macos/test.sh` runs the portable history suite and native platform/UI tests on a Mac. Platform tests use isolated named pasteboards and injected encryption keys, never your general clipboard or real Keychain. On Linux with Swift 5.9 installed, the same command runs just the Foundation history suite.

The [Mac CI workflow](../.github/workflows/macos.yml) tests the core on Linux, tests native code on macOS, builds a universal application, verifies its code signature and architectures, creates a DMG and ZIP, and generates a demo PNG. Its artifact contains the installer, ZIP, checksums, and preview for maintainer review. End users download the installer from the release page instead of GitHub Actions artifacts.

Before release, inspect the generated preview and manually verify on a Mac:

1. Launch twice, toggle with menu bar and ⌘⇧V, dismiss, reopen, and quit.
2. Capture plain text, a link, code, and a clipboard screenshot; search the OCR result and paste each image representation into a suitable app.
3. Check keyboard selection, search-text editing, pin/delete, All/Pinned/type filters, full preview, and minimum-size layout at Retina and external-display scaling.
4. Pause/resume capture; lower age/count limits and confirm pins expire and eviction order matches Windows.
5. Enable encrypted persistence, approve Keychain access if asked, restart, verify restored clips, then disable persistence and confirm `history.dat` is removed.
6. Confirm the macOS clipboard permission prompt/denial behavior and that menu-bar access still works when the shortcut is already in use.

Source is split into `Sources/DollyPasteCore` (portable history rules), `Sources/DollyPaste/Platform` (Apple integrations), and `Sources/DollyPaste/UI` (AppKit windows). `AppModel.swift` owns lifecycle and main-thread coordination. The existing Windows implementation remains in `../src`.
