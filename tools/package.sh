#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage: tools/package.sh --rid <RID> --channel <r2r|aot> [--version <semver>] [--publish-dir <dir>] [--output-root <dir>] [--player-output-dir <dir>] [--content-root <dir>] [--product-name <name>] [--start-scene <scene>] [--include-scene <scene>] [--include-symbols]
EOF
}

fail_usage() {
  echo "$1" >&2
  usage
  exit 2
}

require_value() {
  local name="$1"
  local value="${2:-}"
  if [[ -z "$value" || "$value" == --* ]]; then
    fail_usage "Missing value for $name."
  fi
}

sha256_file() {
  local file="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$file" | awk '{print $1}'
    return
  fi

  if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$file" | awk '{print $1}'
    return
  fi

  if command -v powershell.exe >/dev/null 2>&1; then
    powershell.exe -NoProfile -Command "(Get-FileHash -LiteralPath '$file' -Algorithm SHA256).Hash.ToLowerInvariant()" | tr -d '\r'
    return
  fi

  echo "No SHA256 tool found. Install sha256sum, shasum, or run from Windows bash with powershell.exe available." >&2
  exit 1
}

rid=""
channel=""
version=""
publish_dir=""
output_root=""
player_output_dir=""
content_root=""
product_name=""
start_scene=""
include_symbols=0
include_scenes=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rid)
      require_value "$1" "${2:-}"
      rid="$2"
      shift 2
      ;;
    --channel)
      require_value "$1" "${2:-}"
      channel="$2"
      shift 2
      ;;
    --version)
      require_value "$1" "${2:-}"
      version="$2"
      shift 2
      ;;
    --publish-dir)
      require_value "$1" "${2:-}"
      publish_dir="$2"
      shift 2
      ;;
    --output-root)
      require_value "$1" "${2:-}"
      output_root="$2"
      shift 2
      ;;
    --player-output-dir)
      require_value "$1" "${2:-}"
      player_output_dir="$2"
      shift 2
      ;;
    --content-root)
      require_value "$1" "${2:-}"
      content_root="$2"
      shift 2
      ;;
    --product-name)
      require_value "$1" "${2:-}"
      product_name="$2"
      shift 2
      ;;
    --start-scene)
      require_value "$1" "${2:-}"
      start_scene="$2"
      shift 2
      ;;
    --include-scene)
      require_value "$1" "${2:-}"
      include_scenes+=("$2")
      shift 2
      ;;
    --include-symbols)
      include_symbols=1
      shift
      ;;
    *)
      fail_usage "Unknown argument: $1"
      ;;
  esac
done

if [[ -z "$rid" ]]; then
  fail_usage "Missing required argument: --rid <RID>."
fi

case "$rid" in
  win-x64|win-arm64|linux-x64|linux-arm64|osx-x64|osx-arm64)
    ;;
  *)
    fail_usage "Unsupported RID: $rid"
    ;;
esac

if [[ -z "$channel" ]]; then
  fail_usage "Missing required argument: --channel <r2r|aot>."
fi

case "$channel" in
  r2r|aot)
    ;;
  *)
    fail_usage "Unsupported channel: $channel"
    ;;
esac

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
demo_project="$repo_root/demo/PixelEngine.Demo/PixelEngine.Demo.csproj"

if [[ -z "$version" ]]; then
  version="$(dotnet msbuild "$demo_project" -nologo -getProperty:VersionPrefix | tr -d '\r' | xargs)"
  if [[ -z "$version" ]]; then
    echo "Failed to read VersionPrefix from $demo_project." >&2
    exit 1
  fi
fi

if [[ -z "$output_root" ]]; then
  output_root="$repo_root/artifacts/package"
fi

if [[ -z "$player_output_dir" ]]; then
  player_output_dir="$repo_root/artifacts/PixelEngine Demo"
fi

if [[ -z "$publish_dir" ]]; then
  publish_dir="$repo_root/artifacts/publish/$rid-$channel"
fi

if [[ -z "$content_root" ]]; then
  content_root="$repo_root/demo/PixelEngine.Demo/content"
fi

launcher_base="${product_name:-PixelEngine Demo}"
if [[ -n "$product_name" ]]; then
  assembly_base="PixelEngine.Demo"
else
  assembly_base="PixelEngine.Demo"
fi
windows_launcher="$launcher_base.exe"
unix_launcher="$launcher_base.sh"

if [[ ! -d "$publish_dir" ]]; then
  echo "Publish directory does not exist: $publish_dir" >&2
  exit 1
fi

if [[ ! -d "$content_root" ]]; then
  echo "Content directory does not exist: $content_root" >&2
  exit 1
fi

mkdir -p "$output_root"
output_root="$(cd "$output_root" && pwd)"
mkdir -p "$(dirname "$player_output_dir")"
player_output_dir="$(cd "$(dirname "$player_output_dir")" && pwd)/$(basename "$player_output_dir")"
publish_dir="$(cd "$publish_dir" && pwd)"
content_root="$(cd "$content_root" && pwd)"
if [[ -n "$product_name" ]]; then
  if [[ "$rid" == win-* && -f "$publish_dir/$product_name.exe" ]]; then
    assembly_base="$product_name"
  elif [[ "$rid" != win-* && -f "$publish_dir/$product_name" ]]; then
    assembly_base="$product_name"
  fi
fi

package_name="PixelEngine-Demo-$version-$rid-$channel"
staging_root="$output_root/staging"
staging_dir="$staging_root/$package_name"
package_dir="$output_root/$package_name"
app_dir="$staging_dir/app"
content_dir="$staging_dir/content"

remove_player_package_noise() {
  local directory="$1"
  if [[ "$include_symbols" == "1" ]]; then
    find "$directory" -type f \
      \( -name '*.resources.dll' -o -name 'createdump.exe' -o -name 'createdump' \) \
      -delete
  else
    find "$directory" -type f \
      \( -name '*.pdb' -o -name '*.xml' -o -name '*.resources.dll' -o -name 'createdump.exe' -o -name 'createdump' \) \
      -delete
  fi
  find "$directory" -depth -type d -empty -delete
}

patch_apphost_relative_assembly() {
  local apphost="$1"
  local assembly_name="$2"
  local relative_assembly="$3"
  if command -v python3 >/dev/null 2>&1; then
    python3 - "$apphost" "$assembly_name" "$relative_assembly" <<'PY'
import sys
from pathlib import Path
path = Path(sys.argv[1])
old = sys.argv[2].encode("utf-8")
new = sys.argv[3].encode("utf-8")
data = bytearray(path.read_bytes())
index = data.find(old)
if index < 0:
    raise SystemExit(f"unable to locate {sys.argv[2]} in apphost: {path}")
for offset in range(len(old), len(new) + 1):
    if index + offset >= len(data) or data[index + offset] != 0:
        raise SystemExit(f"apphost has no room for relative assembly path: {sys.argv[3]}")
data[index:index + len(new)] = new
data[index + len(new)] = 0
path.write_bytes(data)
PY
    return
  fi

  echo "Windows package layout needs python3 to patch the root apphost." >&2
  exit 1
}

normalize_scene_path() {
  local scene="${1//\\//}"
  scene="${scene#/}"
  if [[ "$scene" != scenes/* ]]; then
    scene="scenes/$scene"
  fi
  printf '%s\n' "$scene"
}

copy_filtered_content() {
  rm -rf "$content_dir"
  cp -a "$content_root" "$content_dir"
  local scenes_to_copy=()
  local scene
  for scene in "${include_scenes[@]}"; do
    [[ -z "$scene" ]] && continue
    scenes_to_copy+=("$(normalize_scene_path "$scene")")
  done

  if [[ -n "$start_scene" ]]; then
    local startup
    startup="$(normalize_scene_path "$start_scene")"
    cat > "$content_dir/startup.json" <<EOF
{
  "startScene": "$startup"
}
EOF
    local found=0
    for scene in "${scenes_to_copy[@]}"; do
      [[ "$scene" == "$startup" ]] && found=1
    done
    (( found )) || scenes_to_copy+=("$startup")
  fi

  if (( ${#scenes_to_copy[@]} == 0 )); then
    return
  fi

  rm -rf "$content_dir/scenes"
  for scene in "${scenes_to_copy[@]}"; do
    local relative="$scene"
    [[ "$relative" == *.scene ]] || relative="$relative.scene"
    local source="$content_root/$relative"
    if [[ ! -f "$source" ]]; then
      echo "Included scene does not exist: $source" >&2
      exit 1
    fi

    mkdir -p "$(dirname "$content_dir/$relative")"
    cp "$source" "$content_dir/$relative"
  done
}

find "$output_root" -mindepth 1 -maxdepth 1 \
  \( -name "PixelEngine-Demo-*-$rid-$channel" -o -name "PixelEngine-Demo-*-$rid-$channel.zip" -o -name "PixelEngine-Demo-*-$rid-$channel.tar.gz" \) \
  -exec rm -rf -- {} +
rm -rf "$staging_dir"
mkdir -p "$app_dir"

cp -a "$publish_dir"/. "$app_dir"/
remove_player_package_noise "$app_dir"
rm -rf "$app_dir/content" "$app_dir/_PUBLISH_INTERMEDIATE_README.txt" "$content_dir"
copy_filtered_content

if [[ "$rid" == win-* ]]; then
  cp "$publish_dir/$assembly_base.exe" "$staging_dir/$windows_launcher"
  if [[ "$channel" == "r2r" ]]; then
    patch_apphost_relative_assembly "$staging_dir/$windows_launcher" "$assembly_base.dll" "app\\$assembly_base.dll"
  fi
  rm -f "$app_dir/$assembly_base.exe"
fi

if [[ "$include_symbols" == "1" ]]; then
  symbol_line="This development layout keeps debug symbols for local debugging."
else
  symbol_line="Debug symbols, XML documentation, diagnostic dump helpers, and localized satellite resource DLLs are stripped from player packages."
fi

cat > "$staging_dir/README.txt" <<EOF
$launcher_base
================

Start the game from this folder:
  Windows: $windows_launcher
  Linux/macOS: ./$unix_launcher

Runtime dependencies are under app/. Game content is under content/.
${symbol_line}
EOF

cat > "$staging_dir/NOTICE.txt" <<'EOF'
Third-party notices
===================

PixelEngine ships dynamic/runtime dependencies in app/ and game content in content/.

- Box2D: MIT license. Used for pixel rigid body physics.
- RmlUi: MIT license. PixelEngine.UI.Native links the RmlUi core into the dynamic UI backend when the native UI library is present.
- FreeType: FreeType Project License. Used by the RmlUi native UI backend for font rasterization.
- Ultralight: optional commercial-license backend. It is not included in this package unless an activated UI profile explicitly ships its native binaries.

Full upstream license texts are kept with the vendored sources under native/.
EOF

if [[ "$rid" != win-* ]]; then
  cat > "$staging_dir/$unix_launcher" <<EOF
#!/usr/bin/env sh
set -eu
script_dir=\$(CDPATH= cd -- "\$(dirname -- "\$0")" && pwd)
cd "\$script_dir/app"
exec ./$assembly_base --content "\$script_dir/content" "\$@"
EOF
  chmod +x "$staging_dir/$unix_launcher"
fi

package_checksum_path="$staging_dir/SHA256SUMS"
: > "$package_checksum_path"
while IFS= read -r -d '' staged_file; do
  filename="$(basename "$staged_file")"
  [[ "$filename" == "SHA256SUMS" ]] && continue
  relative="${staged_file#"$staging_dir/"}"
  hash="$(sha256_file "$staged_file")"
  printf '%s  %s\n' "$hash" "$relative" >> "$package_checksum_path"
done < <(find "$staging_dir" -type f -print0 | sort -z)

if [[ "$rid" == win-* ]]; then
  archive_name="$package_name.zip"
  archive_path="$output_root/$archive_name"
  rm -f "$archive_path"
  dotnet run --project "$repo_root/tools/PixelEngine.Tools.DeterministicPackage/PixelEngine.Tools.DeterministicPackage.csproj" -c Release -- \
    --source "$staging_dir" \
    --output "$archive_path" \
    --root-name "$package_name" \
    --format zip
else
  archive_name="$package_name.tar.gz"
  archive_path="$output_root/$archive_name"
  rm -f "$archive_path"
  dotnet run --project "$repo_root/tools/PixelEngine.Tools.DeterministicPackage/PixelEngine.Tools.DeterministicPackage.csproj" -c Release -- \
    --source "$staging_dir" \
    --output "$archive_path" \
    --root-name "$package_name" \
    --format tar.gz
fi

checksum_path="$output_root/SHA256SUMS"
tmp_checksum="$checksum_path.tmp"
: > "$tmp_checksum"

while IFS= read -r -d '' archive; do
  filename="$(basename "$archive")"
  hash="$(sha256_file "$archive")"
  printf '%s  %s\n' "$hash" "$filename" >> "$tmp_checksum"
done < <(find "$output_root" -maxdepth 1 -type f \( -name 'PixelEngine-Demo-*-r2r.zip' -o -name 'PixelEngine-Demo-*-aot.zip' -o -name 'PixelEngine-Demo-*-r2r.tar.gz' -o -name 'PixelEngine-Demo-*-aot.tar.gz' \) -print0 | sort -z)

mv "$tmp_checksum" "$checksum_path"

rm -rf "$package_dir"
mv "$staging_dir" "$package_dir"
rm -rf "$player_output_dir"
cp -a "$package_dir" "$player_output_dir"
rmdir "$staging_root" 2>/dev/null || true

echo "Package completed for $rid/$channel."
echo "Archive: $archive_path"
echo "Expanded: $package_dir"
echo "PlayerOutput: $player_output_dir"
echo "Checksums: $checksum_path"
