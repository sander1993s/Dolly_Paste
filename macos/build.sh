#!/bin/bash
set -euo pipefail

if [[ "$(uname -s)" != Darwin ]]; then
    echo "The macOS app must be built on a Mac with Xcode Command Line Tools." >&2
    exit 1
fi
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
package="$root/macos"
output="$root/dist/macos"
app="$output/Dolly Paste.app"
mkdir -p "$output"
work="$(mktemp -d "$output/.build-XXXXXXXX")"
trap 'rm -rf -- "$work"' EXIT

# Separate architecture builds make the universal bundle independent of the host CPU.
for arch in arm64 x86_64; do
    xcrun swift build --package-path "$package" --scratch-path "$package/.build/$arch" \
        -c release --arch "$arch" --product DollyPaste
    binary_dir="$(xcrun swift build --package-path "$package" --scratch-path "$package/.build/$arch" \
        -c release --arch "$arch" --show-bin-path)"
    cp "$binary_dir/DollyPaste" "$work/DollyPaste-$arch"
done

staged="$work/Dolly Paste.app"
mkdir -p "$staged/Contents/MacOS" "$staged/Contents/Resources"
cp "$package/Info.plist" "$staged/Contents/Info.plist"
cp "$root/LICENSE" "$staged/Contents/Resources/LICENSE.txt"
xcrun lipo -create "$work/DollyPaste-arm64" "$work/DollyPaste-x86_64" \
    -output "$staged/Contents/MacOS/DollyPaste"
chmod 755 "$staged/Contents/MacOS/DollyPaste"
xcrun swift "$package/scripts/make-icon.swift" "$work/DollyPaste.iconset"
xcrun iconutil -c icns "$work/DollyPaste.iconset" -o "$staged/Contents/Resources/DollyPaste.icns"
plutil -lint "$staged/Contents/Info.plist"

# Local builds use an ad-hoc signature. A Developer ID identity can be supplied
# for distribution; signing does not upload, notarize, or publish anything.
if [[ -n "${CODESIGN_IDENTITY:-}" ]]; then
    codesign --force --options runtime --timestamp --sign "$CODESIGN_IDENTITY" "$staged"
else
    codesign --force --sign - "$staged"
fi
codesign --verify --deep --strict "$staged"
xcrun lipo -verify_arch arm64 x86_64 "$staged/Contents/MacOS/DollyPaste"

# Only replace the known build output after compilation and signing succeed.
if [[ -e "$app" ]]; then mv "$app" "$work/previous.app"; fi
mv "$staged" "$app"
ditto -c -k --sequesterRsrc --keepParent "$app" "$output/Dolly-Paste-macOS.zip"
echo "Built: $app"
echo "Archive: $output/Dolly-Paste-macOS.zip"
