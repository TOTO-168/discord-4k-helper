#!/bin/zsh
set -euo pipefail

repo_dir=${0:A:h:h}
stage_dir=$(mktemp -d "${TMPDIR:-/tmp}/discord-4k-helper.XXXXXX")
trap 'rm -rf "$stage_dir"' EXIT

cd "$repo_dir"
scratch_path="${SWIFT_SCRATCH_PATH:-.build}"
swift test --scratch-path "$scratch_path"
swift build --scratch-path "$scratch_path" -c release --arch arm64

app_path="$stage_dir/Discord 4K Helper.app"
mkdir -p "$app_path/Contents/MacOS"
mkdir -p "$app_path/Contents/Resources"
cp "$scratch_path/arm64-apple-macosx/release/Discord4KHelper" "$app_path/Contents/MacOS/Discord4KHelper"
cp "Info.plist" "$app_path/Contents/Info.plist"
cp "Assets/AppIcon.icns" "$app_path/Contents/Resources/AppIcon.icns"
chmod 755 "$app_path/Contents/MacOS/Discord4KHelper"
codesign --force --deep --sign - "$app_path"

mkdir -p "$repo_dir/dist"
archive_path="$stage_dir/Discord-4K-Helper-macOS-arm64.zip"
ditto -c -k --sequesterRsrc --keepParent "$app_path" "$archive_path"
mv -f "$archive_path" "$repo_dir/dist/Discord-4K-Helper-macOS-arm64.zip"

echo "Built: $repo_dir/dist/Discord-4K-Helper-macOS-arm64.zip"
