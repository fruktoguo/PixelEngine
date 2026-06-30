#!/usr/bin/env bash
set -euo pipefail

rid=""
configuration="Release"
clean=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rid)
      rid="$2"
      shift 2
      ;;
    --configuration)
      configuration="$2"
      shift 2
      ;;
    --clean)
      clean=1
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
native_root="$repo_root/native"

if [[ -z "$rid" ]]; then
  os="$(uname -s)"
  arch="$(uname -m)"
  case "$os:$arch" in
    Linux:x86_64) rid="linux-x64" ;;
    Linux:aarch64|Linux:arm64) rid="linux-arm64" ;;
    Darwin:x86_64) rid="osx-x64" ;;
    Darwin:arm64) rid="osx-arm64" ;;
    *)
      echo "Cannot infer RID for $os/$arch; pass --rid explicitly." >&2
      exit 2
      ;;
  esac
fi

if [[ "$clean" == "1" ]]; then
  rm -rf "$native_root/out/build/$rid"
fi

cmake --preset "$rid" -S "$native_root"
cmake --build "$native_root/out/build/$rid" --target box2d_shared box2d_static --config "$configuration"

shared_dir="$native_root/out/$rid/shared"
runtime_dir="$repo_root/runtimes/$rid/native"
mkdir -p "$runtime_dir"
mkdir -p "$shared_dir"

shared_build_bin="$native_root/out/build/$rid/box2d-shared/bin"
mapfile -t shared_libraries < <(
  find "$shared_dir" "$shared_build_bin" -type f \( -name '*.so' -o -name '*.dylib' -o -name '*.dll' \) 2>/dev/null
)

if [[ "${#shared_libraries[@]}" -eq 0 ]]; then
  echo "No shared library output found." >&2
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
echo "Static output:  $native_root/out/$rid/static"
