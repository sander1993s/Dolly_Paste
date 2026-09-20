# Contributing to Dolly Paste

Use Windows 10 or Windows 11 with .NET Framework 4.8 and Windows PowerShell 5.1. The project builds with the framework compiler included with Windows; no package restore is required.

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

## Project layout

- `src/`: clipboard storage, Windows integration, and the native interface.
- `tests/`: core, platform, and interface regression suites.
- `assets/`: Dolly Paste's sheep artwork.
- `build.ps1`: compiler setup and executable/icon generation.

Tests should use synthetic text, injected clipboard operations, and test-owned controls. Preserve the clipboard-free behavior of demo and preview modes. Do not include real clipboard history, credentials, personal paths, or unrelated project material in issues, screenshots, fixtures, or contributions.

Build output and local scratch work are ignored by Git. The Windows CI workflow builds the application and runs all three test suites on pushes and pull requests.

## Reporting bugs

Include your Windows version, display scaling when relevant, reproduction steps, expected behavior, and actual behavior. Use synthetic examples and redact personal information from screenshots.

## License

Contributions are distributed under the repository's [MIT License](LICENSE).
