#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage: tools/codesign-macos.sh --path <publish-dir-or-bundle> [--identity <Developer ID>] [--notary-profile <profile>] [--staple-target <path>]

Environment fallbacks:
  APPLE_CODESIGN_IDENTITY
  APPLE_NOTARY_PROFILE
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

target_path=""
identity="${APPLE_CODESIGN_IDENTITY:-}"
notary_profile="${APPLE_NOTARY_PROFILE:-}"
staple_target=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --path)
      require_value "$1" "${2:-}"
      target_path="$2"
      shift 2
      ;;
    --identity)
      require_value "$1" "${2:-}"
      identity="$2"
      shift 2
      ;;
    --notary-profile)
      require_value "$1" "${2:-}"
      notary_profile="$2"
      shift 2
      ;;
    --staple-target)
      require_value "$1" "${2:-}"
      staple_target="$2"
      shift 2
      ;;
    *)
      fail_usage "Unknown argument: $1"
      ;;
  esac
done

if [[ "$(uname -s)" != Darwin ]]; then
  echo "macOS codesign/notarization must run on macOS." >&2
  exit 2
fi

if [[ -z "$target_path" ]]; then
  fail_usage "Missing required argument: --path <publish-dir-or-bundle>."
fi

if [[ -z "$identity" ]]; then
  echo "Missing Developer ID signing identity. Pass --identity or set APPLE_CODESIGN_IDENTITY." >&2
  exit 2
fi

if [[ -z "$notary_profile" ]]; then
  echo "Missing notarytool keychain profile. Pass --notary-profile or set APPLE_NOTARY_PROFILE." >&2
  exit 2
fi

for tool in codesign xcrun ditto spctl; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "Required macOS signing tool not found: $tool" >&2
    exit 1
  fi
done

target_path="$(cd "$(dirname "$target_path")" && pwd)/$(basename "$target_path")"
if [[ ! -e "$target_path" ]]; then
  echo "Target path does not exist: $target_path" >&2
  exit 1
fi

if [[ -z "$staple_target" ]]; then
  staple_target="$target_path"
else
  staple_target="$(cd "$(dirname "$staple_target")" && pwd)/$(basename "$staple_target")"
fi

sign_file() {
  local file="$1"
  codesign --force --options runtime --timestamp --sign "$identity" "$file"
  codesign --verify --strict --verbose=2 "$file"
}

if [[ -d "$target_path" ]]; then
  while IFS= read -r -d '' file; do
    sign_file "$file"
  done < <(find "$target_path" -type f \( -name '*.dylib' -o -name '*.so' -o -perm -111 \) -print0 | sort -z)
else
  sign_file "$target_path"
fi

notary_archive="$(mktemp -t pixelengine-notary).zip"
rm -f "$notary_archive"
trap 'rm -f "$notary_archive"' EXIT
if [[ -d "$target_path" ]]; then
  ditto -c -k --keepParent "$target_path" "$notary_archive"
else
  ditto -c -k --keepParent "$target_path" "$notary_archive"
fi

xcrun notarytool submit "$notary_archive" --keychain-profile "$notary_profile" --wait
xcrun stapler staple "$staple_target"
spctl --assess --type execute --verbose=4 "$staple_target"

echo "macOS codesign and notarization completed."
echo "Signed target: $target_path"
echo "Stapled target: $staple_target"
