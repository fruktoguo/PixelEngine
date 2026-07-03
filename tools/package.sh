#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage: tools/package.sh --rid <RID> --channel <r2r|aot> [--version <semver>] [--publish-dir <dir>] [--output-root <dir>] [--content-root <dir>]
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
content_root=""

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
    --content-root)
      require_value "$1" "${2:-}"
      content_root="$2"
      shift 2
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

if [[ -z "$publish_dir" ]]; then
  publish_dir="$repo_root/artifacts/publish/$rid-$channel"
fi

if [[ -z "$content_root" ]]; then
  content_root="$repo_root/demo/PixelEngine.Demo/content"
fi

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
publish_dir="$(cd "$publish_dir" && pwd)"
content_root="$(cd "$content_root" && pwd)"

package_name="PixelEngine-Demo-$version-$rid-$channel"
staging_root="$output_root/staging"
staging_dir="$staging_root/$package_name"
app_dir="$staging_dir/app"
rm -rf "$staging_dir"
mkdir -p "$app_dir"

cp -a "$publish_dir"/. "$app_dir"/
rm -rf "$app_dir/content"
cp -a "$content_root" "$app_dir/content"

cat > "$staging_dir/README.txt" <<'EOF'
PixelEngine Demo
================

Start the game from this folder:
  Windows: PixelEngine Demo.cmd
  Linux/macOS: ./PixelEngine Demo.sh

The actual runtime files are under app/. This keeps the package root readable.
Advanced users can also run app/PixelEngine.Demo directly.
EOF

if [[ "$rid" == win-* ]]; then
  cat > "$staging_dir/PixelEngine Demo.cmd" <<'EOF'
@echo off
setlocal
pushd "%~dp0app" >nul
".\PixelEngine.Demo.exe" %*
set "exitCode=%ERRORLEVEL%"
popd >nul
exit /b %exitCode%
EOF
else
  cat > "$staging_dir/PixelEngine Demo.sh" <<'EOF'
#!/usr/bin/env sh
set -eu
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
cd "$script_dir/app"
exec ./PixelEngine.Demo "$@"
EOF
  chmod +x "$staging_dir/PixelEngine Demo.sh"
fi

package_checksum_path="$staging_dir/SHA256SUMS"
: > "$package_checksum_path"
while IFS= read -r -d '' app_file; do
  relative="app/${app_file#"$app_dir/"}"
  hash="$(sha256_file "$app_file")"
  printf '%s  %s\n' "$hash" "$relative" >> "$package_checksum_path"
done < <(find "$app_dir" -type f -print0 | sort -z)

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

rm -rf "$staging_dir"
rmdir "$staging_root" 2>/dev/null || true

echo "Package completed for $rid/$channel."
echo "Archive: $archive_path"
echo "Checksums: $checksum_path"
