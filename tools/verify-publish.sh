#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage: tools/verify-publish.sh --rid <RID> --channel <r2r|aot> [--publish-dir <dir>] [--allow-load-only] [--configuration <Config>]

Supported RID:
  win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64
EOF
}

fail_usage() {
  echo "$1" >&2
  usage
  exit 2
}

fail_verify() {
  echo "$1" >&2
  exit 1
}

require_value() {
  local name="$1"
  local value="${2:-}"
  if [[ -z "$value" || "$value" == --* ]]; then
    fail_usage "Missing value for $name."
  fi
}

normalize_path() {
  local path="$1"
  if command -v cygpath >/dev/null 2>&1 && [[ "$path" =~ ^[A-Za-z]:[\\/] ]]; then
    cygpath -u "$path"
    return
  fi

  if [[ "$path" =~ ^([A-Za-z]):[\\/](.*)$ ]] && grep -qi microsoft /proc/sys/kernel/osrelease 2>/dev/null; then
    local drive="${BASH_REMATCH[1],,}"
    local rest="${BASH_REMATCH[2]//\\//}"
    printf '/mnt/%s/%s\n' "$drive" "$rest"
    return
  fi

  printf '%s\n' "$path"
}

host_rid() {
  local host_os
  local host_arch
  host_os="$(uname -s)"
  host_arch="$(uname -m)"

  case "$host_os:$host_arch" in
    MINGW*:x86_64|MSYS*:x86_64|CYGWIN*:x86_64) echo "win-x64" ;;
    MINGW*:aarch64|MINGW*:arm64|MSYS*:aarch64|MSYS*:arm64|CYGWIN*:aarch64|CYGWIN*:arm64) echo "win-arm64" ;;
    Linux:x86_64|Linux:amd64) echo "linux-x64" ;;
    Linux:aarch64|Linux:arm64) echo "linux-arm64" ;;
    Darwin:x86_64) echo "osx-x64" ;;
    Darwin:arm64) echo "osx-arm64" ;;
    *) return 1 ;;
  esac
}

entry_name_for_rid() {
  local target_rid="$1"
  case "$target_rid" in
    win-*) echo "PixelEngine.Demo.exe" ;;
    *) echo "PixelEngine.Demo" ;;
  esac
}

box2d_dynamic_name_for_rid() {
  local target_rid="$1"
  case "$target_rid" in
    win-*) echo "box2d.dll" ;;
    linux-*) echo "libbox2d.so" ;;
    osx-*) echo "libbox2d.dylib" ;;
    *) fail_usage "Unsupported RID: $target_rid" ;;
  esac
}

find_qemu_aarch64() {
  if command -v qemu-aarch64 >/dev/null 2>&1; then
    command -v qemu-aarch64
    return
  fi

  if command -v qemu-aarch64-static >/dev/null 2>&1; then
    command -v qemu-aarch64-static
    return
  fi

  return 1
}

assert_publish_layout() {
  local directory="$1"
  local target_rid="$2"
  local target_channel="$3"
  local executable="$4"

  if [[ ! -d "$directory" ]]; then
    fail_verify "Publish directory does not exist: $directory"
  fi

  if [[ ! -f "$executable" ]]; then
    fail_verify "Published entry file not found: $executable"
  fi

  local content_dir="$directory/content"
  if [[ ! -f "$content_dir/materials.json" || ! -f "$content_dir/reactions.json" ]]; then
    fail_verify "Published content is incomplete: expected content/materials.json and content/reactions.json under $directory"
  fi

  local box2d_name
  local box2d_path
  box2d_name="$(box2d_dynamic_name_for_rid "$target_rid")"
  box2d_path="$directory/runtimes/$target_rid/native/$box2d_name"
  if [[ "$target_channel" == "r2r" ]]; then
    if [[ ! -f "$box2d_path" ]]; then
      fail_verify "R2R publish output is missing dynamic Box2D: $box2d_path"
    fi
  elif [[ -f "$box2d_path" ]]; then
    fail_verify "AOT publish output must not carry dynamic Box2D: $box2d_path"
  fi
}

run_smoke() {
  local executable="$1"
  local target_rid="$2"
  local current_host_rid="$3"
  local allow_load_only="$4"
  local executable_dir
  executable_dir="$(cd "$(dirname "$executable")" && pwd)"

  if [[ "$current_host_rid" == "$target_rid" ]]; then
    if ! (cd "$executable_dir" && "$executable" --smoke); then
      fail_verify "Demo --smoke failed for $target_rid: $executable"
    fi
    return
  fi

  if [[ "$current_host_rid" == "linux-x64" && "$target_rid" == "linux-arm64" ]]; then
    local qemu
    if qemu="$(find_qemu_aarch64)"; then
      if ! (cd "$executable_dir" && "$qemu" "$executable" --smoke); then
        fail_verify "Demo --smoke failed under QEMU for $target_rid: $executable"
      fi
      return
    fi
  fi

  if [[ "$allow_load_only" != "1" ]]; then
    fail_verify "Current host $current_host_rid cannot run $target_rid; pass --allow-load-only to downgrade to publish layout/native validation."
  fi

  echo "Cross RID verification downgraded to load-only validation: host=$current_host_rid target=$target_rid"
}

rid=""
channel=""
publish_dir=""
configuration="Release"
allow_load_only=0

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
    --publish-dir)
      require_value "$1" "${2:-}"
      publish_dir="$2"
      shift 2
      ;;
    --configuration)
      require_value "$1" "${2:-}"
      configuration="$2"
      shift 2
      ;;
    --allow-load-only)
      allow_load_only=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
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

if [[ -z "$configuration" ]]; then
  fail_usage "Missing value for --configuration."
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ -z "$publish_dir" ]]; then
  publish_dir="$repo_root/artifacts/publish/$rid-$channel"
fi

publish_dir="$(normalize_path "$publish_dir")"
if [[ -d "$publish_dir" ]]; then
  publish_dir="$(cd "$publish_dir" && pwd)"
fi

current_host_rid="$(host_rid)" || fail_verify "Cannot infer host RID from $(uname -s)/$(uname -m)."
entry_name="$(entry_name_for_rid "$rid")"
executable="$publish_dir/$entry_name"

assert_publish_layout "$publish_dir" "$rid" "$channel" "$executable"
run_smoke "$executable" "$rid" "$current_host_rid" "$allow_load_only"

echo "Publish verification completed for $rid/$channel."
echo "PublishDir: $publish_dir"
