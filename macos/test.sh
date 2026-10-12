#!/bin/bash
set -euo pipefail
package="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ "$(uname -s)" == Darwin ]]; then
    xcrun swift test --package-path "$package"
else
    # Package.swift exposes only the Foundation history core off macOS.
    swift test --package-path "$package"
fi
