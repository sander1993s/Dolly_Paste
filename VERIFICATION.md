# Dolly Paste verification

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

Run from the repository root on Windows with .NET Framework 4.8:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File build.ps1
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tests/run-tests.ps1
```

The test runner compiles each suite in a uniquely named temporary directory and removes its generated artifacts when finished. The Windows CI workflow executes the same build and test scripts.

## Coverage

- **Core:** retention, pin eviction, capacity and payload limits, encrypted persistence, corrupt storage handling, and legacy sensitive-entry behavior.
- **Platform:** clipboard format/publication seams, Windows metadata handling with test-owned controls, bounded queues, and legacy protection helper regressions.
- **Interface:** compact and expanded layouts, search and keyboard shortcuts, settings, screen bounds, popup dismissal, guarded copying, command-line parsing, and removal of automatic protection hooks.

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
