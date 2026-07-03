#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage: tools/publish-aot.sh --rid <RID> [--configuration <Config>] [--output <dir>] [--version <semver>] [--informational-version <value>] [--skip-native-build]
EOF
}

rid=""
configuration="Release"
output=""
version=""
informational_version=""
skip_native_build=0

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
    --output)
      if [[ $# -lt 2 || "$2" == --* ]]; then
        echo "Missing value for --output." >&2
        usage
        exit 2
      fi
      output="$2"
      shift 2
      ;;
    --version)
      if [[ $# -lt 2 || "$2" == --* ]]; then
        echo "Missing value for --version." >&2
        usage
        exit 2
      fi
      version="$2"
      shift 2
      ;;
    --informational-version)
      if [[ $# -lt 2 || "$2" == --* ]]; then
        echo "Missing value for --informational-version." >&2
        usage
        exit 2
      fi
      informational_version="$2"
      shift 2
      ;;
    --skip-native-build)
      skip_native_build=1
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 2
      ;;
  esac
done

if [[ -z "$rid" ]]; then
  echo "Missing required argument: --rid <RID>." >&2
  usage
  exit 2
fi

host_os="$(uname -s)"
case "$rid" in
  win-x64|win-arm64)
    if [[ "$host_os" != MINGW* && "$host_os" != MSYS* && "$host_os" != CYGWIN* ]]; then
      echo "RID $rid must be published from a Windows bash shell. Use tools/publish-aot.ps1 on Windows." >&2
      exit 2
    fi
    ;;
  linux-x64|linux-arm64)
    if [[ "$host_os" != Linux ]]; then
      echo "RID $rid must be published from Linux." >&2
      exit 2
    fi
    ;;
  osx-x64|osx-arm64)
    if [[ "$host_os" != Darwin ]]; then
      echo "RID $rid must be published from macOS." >&2
      exit 2
    fi
    ;;
  *)
    echo "Unsupported RID: $rid" >&2
    usage
    exit 2
    ;;
esac

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
demo_project="$repo_root/demo/PixelEngine.Demo/PixelEngine.Demo.csproj"

if [[ -z "$output" ]]; then
  output="$repo_root/artifacts/publish/$rid-aot"
fi

if [[ "$skip_native_build" == "0" ]]; then
  bash "$repo_root/tools/build-native.sh" --rid "$rid" --configuration "$configuration"
fi

publish_properties=("-p:Channel=AOT")
if [[ -n "$version" ]]; then
  publish_properties+=("-p:Version=$version")
fi

if [[ -n "$informational_version" ]]; then
  publish_properties+=("-p:InformationalVersion=$informational_version")
fi

target_framework="$(dotnet msbuild "$demo_project" -nologo -getProperty:TargetFramework | tr -d '\r' | xargs)"
if [[ -z "$target_framework" ]]; then
  echo "Failed to read TargetFramework from $demo_project." >&2
  exit 1
fi

rid_arch="${rid##*-}"
rm -rf "$output"
rm -rf "$repo_root/demo/PixelEngine.Demo/bin/$rid_arch/$configuration/$target_framework/$rid"
rm -rf "$repo_root/demo/PixelEngine.Demo/obj/$rid_arch/$configuration/$target_framework/$rid"
for project_dir in "$repo_root"/src/PixelEngine.*; do
  [[ -d "$project_dir" ]] || continue
  rm -rf "$project_dir/bin/$rid_arch/$configuration/$target_framework"
  rm -rf "$project_dir/obj/$rid_arch/$configuration/$target_framework"
done

dotnet publish "$demo_project" \
  -c "$configuration" \
  -r "$rid" \
  "${publish_properties[@]}" \
  -o "$output"

echo "AOT publish completed for $rid."
echo "Output: $output"
