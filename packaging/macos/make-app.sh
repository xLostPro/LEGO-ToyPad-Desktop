#!/usr/bin/env bash
# Wraps a `dotnet publish` output folder into LegoToyPad.app.
# Usage: make-app.sh <publish-dir> <output-dir> <version>
set -euo pipefail

publish_dir="$1"
out_dir="$2"
version="$3"
here="$(cd "$(dirname "$0")" && pwd)"
app="$out_dir/LegoToyPad.app"

rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$publish_dir"/. "$app/Contents/MacOS/"
chmod +x "$app/Contents/MacOS/LegoToyPad"

# Build an .icns from the app PNG (sips/iconutil ship with macOS).
iconset="$(mktemp -d)/AppIcon.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  sips -z $size $size "$here/../../src/LegoToyPad.App/Assets/app.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  sips -z $((size * 2)) $((size * 2)) "$here/../../src/LegoToyPad.App/Assets/app.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/AppIcon.icns"

sed "s/__VERSION__/$version/g" "$here/Info.plist" > "$app/Contents/Info.plist"

# Ad-hoc signature so Apple Silicon will launch it (users still need right-click › Open the first time).
codesign --force --deep --sign - "$app"
