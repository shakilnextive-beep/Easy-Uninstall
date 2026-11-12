#!/bin/zsh
# Builds Easy Uninstall.app next to this script.
set -e
cd "$(dirname "$0")"
APP="Easy Uninstall.app"
VERSION="${VERSION:-$(cat VERSION 2>/dev/null || echo 1.0.0)}"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# Universal binary: Apple Silicon + Intel
TMP=$(mktemp -d)
for ARCH in arm64 x86_64; do
  swiftc -O -swift-version 5 -target $ARCH-apple-macos13.0 -o "$TMP/EasyUninstall-$ARCH" Sources/*.swift 2>&1 | grep -E "error" -A3 || true
  [ -f "$TMP/EasyUninstall-$ARCH" ] || { echo "Build failed ($ARCH)"; exit 1; }
done
lipo -create "$TMP/EasyUninstall-arm64" "$TMP/EasyUninstall-x86_64" -output "$APP/Contents/MacOS/EasyUninstall"
rm -rf "$TMP"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>Easy Uninstall</string>
  <key>CFBundleDisplayName</key><string>Easy Uninstall</string>
  <key>CFBundleIdentifier</key><string>local.easyuninstall</string>
  <key>CFBundleExecutable</key><string>EasyUninstall</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSHumanReadableCopyright</key><string>Creator: Md. Shakil (shakil.nextive@gmail.com)</string>
  <key>NSAppleEventsUsageDescription</key><string>Admin অনুমতি লাগে এমন ফাইল মুছতে।</string>
</dict></plist>
PLIST

[ -f AppIcon.icns ] && cp AppIcon.icns "$APP/Contents/Resources/"
# Stable identity (by bundle id) so macOS keeps folder permissions across updates
codesign --force --deep --sign - --identifier local.easyuninstall -r='designated => identifier "local.easyuninstall"' "$APP" >/dev/null 2>&1 || true
echo "Built: $(pwd)/$APP"

# Release zip for GitHub:  ./build.sh zip
if [ "$1" = "zip" ]; then
  mkdir -p dist
  rm -f "dist/Easy-Uninstall-mac.zip"
  ditto -c -k --sequesterRsrc --keepParent "$APP" "dist/Easy-Uninstall-mac.zip"
  echo "Zip: $(pwd)/dist/Easy-Uninstall-mac.zip"
fi
