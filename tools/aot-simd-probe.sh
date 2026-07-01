#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage: tools/aot-simd-probe.sh --rid <RID> [--publish-dir <dir>]
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

rid=""
publish_dir=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rid)
      require_value "$1" "${2:-}"
      rid="$2"
      shift 2
      ;;
    --publish-dir)
      require_value "$1" "${2:-}"
      publish_dir="$2"
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
  win-x64|linux-x64|osx-x64)
    ;;
  win-arm64|linux-arm64|osx-arm64)
    echo "AOT SIMD probe skipped for non-x64 RID: $rid"
    exit 0
    ;;
  *)
    fail_usage "Unsupported RID: $rid"
    ;;
esac

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ -z "$publish_dir" ]]; then
  publish_dir="$repo_root/artifacts/publish/$rid-aot"
fi

entry_name="PixelEngine.Demo"
if [[ "$rid" == win-* ]]; then
  entry_name="PixelEngine.Demo.exe"
fi

exe_path="$publish_dir/$entry_name"
if [[ ! -f "$exe_path" ]]; then
  echo "AOT executable not found: $exe_path" >&2
  exit 1
fi

if [[ "$(uname -s)" == Darwin ]]; then
  if ! command -v otool >/dev/null 2>&1; then
    echo "otool not found for macOS AOT SIMD probe." >&2
    exit 1
  fi
  disassembly="$(otool -tvV "$exe_path")"
elif command -v llvm-objdump >/dev/null 2>&1; then
  disassembly="$(llvm-objdump -d "$exe_path")"
elif command -v objdump >/dev/null 2>&1; then
  disassembly="$(objdump -d "$exe_path")"
else
  echo "No disassembler found: install llvm-objdump or objdump." >&2
  exit 1
fi

if ! grep -Eq '\b[yz]mm[0-9]+\b' <<<"$disassembly"; then
  echo "AOT SIMD probe failed: no ymm/zmm register marker found in $rid executable." >&2
  exit 1
fi

echo "AOT SIMD probe passed for $rid: found ymm/zmm marker."
