#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage: tools/build-native.sh [--rid <RID>] [--configuration <Config>] [--clean]

Supported RID:
  win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64
EOF
}

rid=""
configuration="Release"
clean=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rid)
      if [[ $# -lt 2 || "$2" == --* ]]; then
        echo "Missing value for --rid." >&2
        usage
        exit 2
      fi
      rid="$2"
      shift 2
      ;;
    --configuration)
      if [[ $# -lt 2 || "$2" == --* ]]; then
        echo "Missing value for --configuration." >&2
        usage
        exit 2
      fi
      configuration="$2"
      shift 2
      ;;
    --clean)
      clean=1
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 2
      ;;
  esac
done

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
native_root="$repo_root/native"
host_os="$(uname -s)"
host_arch="$(uname -m)"

if [[ -z "$rid" ]]; then
  case "$host_os:$host_arch" in
    MINGW*:x86_64|MSYS*:x86_64|CYGWIN*:x86_64) rid="win-x64" ;;
    MINGW*:aarch64|MINGW*:arm64|MSYS*:aarch64|MSYS*:arm64|CYGWIN*:aarch64|CYGWIN*:arm64) rid="win-arm64" ;;
    Linux:x86_64) rid="linux-x64" ;;
    Linux:aarch64|Linux:arm64) rid="linux-arm64" ;;
    Darwin:x86_64) rid="osx-x64" ;;
    Darwin:arm64) rid="osx-arm64" ;;
    *)
      echo "Cannot infer RID for $host_os/$host_arch; pass --rid explicitly." >&2
      usage
      exit 2
      ;;
  esac
fi

case "$rid" in
  win-x64|win-arm64)
    if [[ "$host_os" != MINGW* && "$host_os" != MSYS* && "$host_os" != CYGWIN* ]]; then
      echo "RID $rid must be built from a Windows bash shell. Use tools/build-native.ps1 on Windows." >&2
      exit 2
    fi
    ;;
  linux-x64|linux-arm64)
    if [[ "$host_os" != Linux ]]; then
      echo "RID $rid must be built from Linux." >&2
      exit 2
    fi
    ;;
  osx-x64|osx-arm64)
    if [[ "$host_os" != Darwin ]]; then
      echo "RID $rid must be built from macOS." >&2
      exit 2
    fi
    ;;
  *)
    echo "Unsupported RID: $rid" >&2
    usage
    exit 2
    ;;
esac

if [[ "$clean" == "1" ]]; then
  rm -rf "$native_root/out/build/$rid"
fi

cmake --preset "$rid" -S "$native_root"
cmake --build "$native_root/out/build/$rid" --target box2d_shared box2d_static pixelengine_ui_native --config "$configuration"

shared_dir="$native_root/out/$rid/shared"
static_dir="$native_root/out/$rid/static"
runtime_dir="$repo_root/runtimes/$rid/native"
mkdir -p "$runtime_dir"
mkdir -p "$shared_dir"

shared_build_bin="$native_root/out/build/$rid/box2d-shared/bin"
shared_libraries=()
while IFS= read -r library; do
  shared_libraries+=("$library")
# Box2D on ELF platforms emits libbox2d.so as a symlink to its versioned payload.
# Follow command-line symlinks so the canonical P/Invoke name is copied as a real
# file into the RID runtime directory instead of being silently skipped.
done < <(find -L "$shared_dir" "$shared_build_bin" -type f \( -name '*.so' -o -name '*.dylib' -o -name '*.dll' \) 2>/dev/null)

if [[ "${#shared_libraries[@]}" -eq 0 ]]; then
  echo "No shared library output found." >&2
  exit 1
fi

static_libraries=()
while IFS= read -r library; do
  static_libraries+=("$library")
done < <(find "$static_dir" -type f \( -name '*.a' -o -name '*.lib' \) 2>/dev/null)

if [[ "${#static_libraries[@]}" -eq 0 ]]; then
  echo "No static library output found in $static_dir." >&2
  exit 1
fi

for library in "${shared_libraries[@]}"; do
  if [[ "$(cd "$(dirname "$library")" && pwd)" != "$(cd "$shared_dir" && pwd)" ]]; then
    cp -f "$library" "$shared_dir/"
  fi
  cp -f "$library" "$runtime_dir/"
done

echo "Native build completed for $rid."
echo "Shared runtime: $runtime_dir"
echo "Static output:  $static_dir"
