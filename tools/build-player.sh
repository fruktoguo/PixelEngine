#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage: tools/build-player.sh --rid <RID> --channel <r2r|aot> --output <dir> [options]

Options:
  --configuration <Debug|Release>
  --version <semver>
  --informational-version <value>
  --product-name <name>
  --content-root <dir>
  --icon-path|--application-icon <ico>
  --include-symbols
  --start-scene <scene>
  --window-width <pixels>
  --window-height <pixels>
  --vsync <true|false>
  --runtime-ui-backend <backend>
  --release-channel <Development|Production>
  --include-scene <scene>   repeatable
  --dev-layout
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

json_escape() {
  local text="$1"
  text="${text//\\/\\\\}"
  text="${text//\"/\\\"}"
  text="${text//$'\r'/}"
  text="${text//$'\n'/\\n}"
  printf '%s' "$text"
}

emit_event() {
  local kind="$1"
  local phase="$2"
  local percent="$3"
  local level="$4"
  local message="$5"
  local ts
  ts="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
  printf '{"schema":"pixelengine.build/v1","kind":"%s","phase":"%s","percent":%.2f,"level":"%s","message":"%s","ts":"%s"}\n' \
    "$kind" "$phase" "$percent" "$level" "$(json_escape "$message")" "$ts"
}

sha256_file() {
  local file="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$file" | awk '{print tolower($1)}'
    return
  fi

  if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$file" | awk '{print tolower($1)}'
    return
  fi

  if command -v openssl >/dev/null 2>&1; then
    openssl dgst -sha256 "$file" | awk '{print tolower($NF)}'
    return
  fi

  echo ""
}

normalize_path() {
  local path="$1"
  if [[ "$path" = /* ]]; then
    printf '%s\n' "$path"
  else
    printf '%s/%s\n' "$(pwd)" "$path"
  fi
}

host_rid() {
  local os
  local arch
  os="$(uname -s)"
  arch="$(uname -m)"
  case "$os:$arch" in
    Linux:x86_64|Linux:amd64) echo "linux-x64" ;;
    Linux:aarch64|Linux:arm64) echo "linux-arm64" ;;
    Darwin:x86_64) echo "osx-x64" ;;
    Darwin:arm64) echo "osx-arm64" ;;
    MINGW*:x86_64|MSYS*:x86_64|CYGWIN*:x86_64) echo "win-x64" ;;
    MINGW*:aarch64|MINGW*:arm64|MSYS*:aarch64|MSYS*:arm64|CYGWIN*:aarch64|CYGWIN*:arm64) echo "win-arm64" ;;
    *) echo "" ;;
  esac
}

rid_os() {
  case "$1" in
    win-*) echo "win" ;;
    linux-*) echo "linux" ;;
    osx-*) echo "osx" ;;
    *) echo "" ;;
  esac
}

rid_smoke_mode() {
  local target_rid="$1"
  local config="$repo_root/tools/release-rids.json"
  [[ -f "$config" ]] || return 0
  if command -v python3 >/dev/null 2>&1; then
    python3 - "$config" "$target_rid" <<'PY'
import json
import sys
with open(sys.argv[1], "r", encoding="utf-8") as handle:
    data = json.load(handle)
for item in data.get("rids", []):
    if item.get("rid") == sys.argv[2]:
        print(item.get("smoke", ""))
        break
PY
    return
  fi

  awk -v rid="$target_rid" '
    $0 ~ "\"rid\"[[:space:]]*:[[:space:]]*\"" rid "\"" { found=1 }
    found && $0 ~ "\"smoke\"" {
      gsub(/^.*"smoke"[[:space:]]*:[[:space:]]*"/, "")
      gsub(/".*$/, "")
      print
      exit
    }
  ' "$config"
}

phase_key() {
  case "$1" in
    native) echo "Native" ;;
    publish) echo "Publish" ;;
    verify) echo "Verify" ;;
    package) echo "Package" ;;
    audit) echo "Audit" ;;
    *) echo "$1" ;;
  esac
}

declare -A phase_timings_ms=()

run_phase() {
  local phase="$1"
  local start_percent="$2"
  local end_percent="$3"
  shift 3
  emit_event "Progress" "$phase" "$start_percent" "Info" "开始 $phase。"
  local start_ns
  local end_ns
  start_ns="$(date +%s%N)"
  set +e
  "$@" 2>&1 | while IFS= read -r line || [[ -n "$line" ]]; do
    [[ -z "$line" ]] || emit_event "Log" "$phase" "$start_percent" "Info" "$line"
  done
  local exit_code=${PIPESTATUS[0]}
  set -e
  end_ns="$(date +%s%N)"
  phase_timings_ms["$(phase_key "$phase")"]="$(( (end_ns - start_ns) / 1000000 ))"
  if (( exit_code != 0 )); then
    emit_event "Log" "$phase" "$start_percent" "Error" "命令失败($exit_code): $*"
    return "$exit_code"
  fi

  emit_event "Progress" "$phase" "$end_percent" "Info" "完成 $phase。"
}

rid=""
channel=""
configuration="Release"
output=""
version=""
informational_version=""
product_name="PixelEngine Demo"
content_root=""
icon_path=""
include_symbols=0
start_scene="scenes/playable-world.scene"
window_width="1280"
window_height="720"
vsync="true"
runtime_ui_backend="ManagedFallback"
release_channel="Development"
include_scenes=()
dev_layout=0
warnings=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rid|-Rid)
      require_value "$1" "${2:-}"
      rid="$2"
      shift 2
      ;;
    --channel|-Channel)
      require_value "$1" "${2:-}"
      channel="${2,,}"
      shift 2
      ;;
    --configuration|-Configuration)
      require_value "$1" "${2:-}"
      configuration="$2"
      shift 2
      ;;
    --output|-Output)
      require_value "$1" "${2:-}"
      output="$2"
      shift 2
      ;;
    --version|-Version)
      require_value "$1" "${2:-}"
      version="$2"
      shift 2
      ;;
    --informational-version|-InformationalVersion)
      require_value "$1" "${2:-}"
      informational_version="$2"
      shift 2
      ;;
    --product-name|-ProductName)
      require_value "$1" "${2:-}"
      product_name="$2"
      shift 2
      ;;
    --content-root|-ContentRoot)
      require_value "$1" "${2:-}"
      content_root="$2"
      shift 2
      ;;
    --icon-path|--application-icon|-IconPath|-ApplicationIcon)
      require_value "$1" "${2:-}"
      icon_path="$2"
      shift 2
      ;;
    --include-symbols|-IncludeSymbols)
      include_symbols=1
      shift
      ;;
    --start-scene|-StartScene)
      require_value "$1" "${2:-}"
      start_scene="$2"
      shift 2
      ;;
    --window-width|-WindowWidth)
      require_value "$1" "${2:-}"
      window_width="$2"
      shift 2
      ;;
    --window-height|-WindowHeight)
      require_value "$1" "${2:-}"
      window_height="$2"
      shift 2
      ;;
    --vsync|-VSync)
      require_value "$1" "${2:-}"
      vsync="$2"
      shift 2
      ;;
    --runtime-ui-backend|-RuntimeUiBackend)
      require_value "$1" "${2:-}"
      runtime_ui_backend="$2"
      shift 2
      ;;
    --release-channel|-ReleaseChannel)
      require_value "$1" "${2:-}"
      release_channel="$2"
      shift 2
      ;;
    --include-scene|-IncludeScene)
      require_value "$1" "${2:-}"
      include_scenes+=("$2")
      shift 2
      ;;
    --dev-layout|-DevLayout)
      dev_layout=1
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

[[ -n "$rid" ]] || fail_usage "Missing required argument: --rid."
[[ -n "$channel" ]] || fail_usage "Missing required argument: --channel."
[[ -n "$output" ]] || fail_usage "Missing required argument: --output."
case "$channel" in r2r|aot) ;; *) fail_usage "Unsupported channel: $channel" ;; esac
case "$release_channel" in Development|Production) ;; *) fail_usage "Unsupported release channel: $release_channel" ;; esac

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
current_host_rid="$(host_rid)"
smoke_mode="$(rid_smoke_mode "$rid")"
allow_load_only=0
if [[ -n "$current_host_rid" && "$current_host_rid" != "$rid" && "$smoke_mode" == "load-only" ]]; then
  allow_load_only=1
  warnings+=("目标 RID $rid 按 release-rids.json 使用 load-only 校验；不会伪造目标硬件 smoke。")
fi

if [[ "$channel" == "aot" && -n "$current_host_rid" && "$(rid_os "$current_host_rid")" != "$(rid_os "$rid")" ]]; then
  emit_event "Log" "native" 0 "Error" "NativeAOT 仅支持当前宿主 OS：$current_host_rid，当前选择为 $rid。"
  exit 1
fi

output_root="$(normalize_path "$output")"
publish_root="$output_root/publish"
publish_dir="$publish_root/$rid-$channel"
package_root="$output_root/package"
player_dir="$output_root/player"
result_path="$output_root/build-result.json"
mkdir -p "$output_root" "$publish_root" "$package_root"
rm -f "$result_path"

demo_project="$repo_root/demo/PixelEngine.Demo/PixelEngine.Demo.csproj"
if [[ -z "$version" ]]; then
  version="$(dotnet msbuild "$demo_project" -nologo -getProperty:VersionPrefix | tr -d '\r' | xargs)"
fi
if [[ -z "$informational_version" ]]; then
  short_sha="$(git -C "$repo_root" rev-parse --short HEAD 2>/dev/null || true)"
  if [[ -n "$short_sha" ]]; then
    informational_version="$version+$short_sha"
  else
    informational_version="$version"
  fi
fi

write_result() {
  local ok="$1"
  local exit_code="$2"
  local error_message="$3"
  local archive=""
  local package_dir=""
  local launcher=""
  local sha=""
  local size=0
  archive="$(find "$package_root" -maxdepth 1 -type f \( -name "*-$rid-$channel.zip" -o -name "*-$rid-$channel.tar.gz" \) | sort | tail -n 1 || true)"
  if [[ -n "$archive" && -f "$archive" ]]; then
    local archive_name
    archive_name="$(basename "$archive")"
    package_dir="$package_root/${archive_name%.zip}"
    package_dir="${package_dir%.tar.gz}"
    sha="$(sha256_file "$archive")"
    size="$(wc -c < "$archive" | tr -d ' ')"
  fi
  local launcher_name
  if [[ "$rid" == win-* ]]; then
    launcher_name="$product_name.exe"
  else
    launcher_name="$product_name.sh"
  fi
  [[ -f "$player_dir/$launcher_name" ]] && launcher="$player_dir/$launcher_name"

  {
    printf '{\n'
    printf '  "ok": %s,\n' "$ok"
    printf '  "rid": "%s",\n' "$(json_escape "$rid")"
    printf '  "channel": "%s",\n' "$(json_escape "$channel")"
    printf '  "releaseChannel": "%s",\n' "$(json_escape "$release_channel")"
    printf '  "configuration": "%s",\n' "$(json_escape "$configuration")"
    printf '  "runtimeUiBackend": "%s",\n' "$(json_escape "$runtime_ui_backend")"
    printf '  "version": "%s",\n' "$(json_escape "$version")"
    printf '  "informationalVersion": "%s",\n' "$(json_escape "$informational_version")"
    printf '  "packageArchive": %s,\n' "$(if [[ -n "$archive" ]]; then printf '"%s"' "$(json_escape "$archive")"; else printf 'null'; fi)"
    printf '  "packageDir": %s,\n' "$(if [[ -n "$package_dir" ]]; then printf '"%s"' "$(json_escape "$package_dir")"; else printf 'null'; fi)"
    printf '  "playerDir": %s,\n' "$(if [[ -d "$player_dir" ]]; then printf '"%s"' "$(json_escape "$player_dir")"; else printf 'null'; fi)"
    printf '  "launcherExe": %s,\n' "$(if [[ -n "$launcher" ]]; then printf '"%s"' "$(json_escape "$launcher")"; else printf 'null'; fi)"
    printf '  "sha256": %s,\n' "$(if [[ -n "$sha" ]]; then printf '"%s"' "$sha"; else printf 'null'; fi)"
    printf '  "sizeBytes": %s,\n' "$size"
    printf '  "phaseTimingsMs": {'
    local first=1
    local key
    for key in Native Publish Verify Package Audit; do
      if [[ -n "${phase_timings_ms[$key]+x}" ]]; then
        (( first )) || printf ', '
        first=0
        printf '"%s": %s' "$key" "${phase_timings_ms[$key]}"
      fi
    done
    printf '},\n'
    printf '  "warnings": ['
    local warning_first=1
    local warning
    for warning in "${warnings[@]}"; do
      (( warning_first )) || printf ', '
      warning_first=0
      printf '"%s"' "$(json_escape "$warning")"
    done
    printf '],\n'
    printf '  "error": %s,\n' "$(if [[ -n "$error_message" ]]; then printf '"%s"' "$(json_escape "$error_message")"; else printf 'null'; fi)"
    printf '  "exitCode": %s\n' "$exit_code"
    printf '}\n'
  } > "$result_path"
  emit_event "Result" "done" "$(if [[ "$ok" == "true" ]]; then echo 100; else echo 0; fi)" "$(if [[ "$ok" == "true" ]]; then echo Info; else echo Error; fi)" "$(if [[ "$ok" == "true" ]]; then echo "构建完成。"; else echo "$error_message"; fi)"
}

set +e
error_message=""
run_phase "native" 0 20 bash "$repo_root/tools/build-native.sh" --rid "$rid" --configuration "$configuration" || error_message="native phase failed"
if [[ -z "$error_message" ]]; then
  publish_args=(--rid "$rid" --configuration "$configuration" --output "$publish_dir" --version "$version" --informational-version "$informational_version" --product-name "$product_name" --skip-native-build)
  [[ -n "$icon_path" ]] && publish_args+=(--application-icon "$icon_path")
  if (( include_symbols || dev_layout )); then publish_args+=(--include-symbols); fi
  run_phase "publish" 20 45 bash "$repo_root/tools/publish-$channel.sh" "${publish_args[@]}" || error_message="publish phase failed"
fi
if [[ -z "$error_message" ]]; then
  verify_args=(--rid "$rid" --channel "$channel" --configuration "$configuration" --publish-dir "$publish_dir" --product-name "$product_name")
  if (( allow_load_only )); then verify_args+=(--allow-load-only); fi
  run_phase "verify" 45 60 bash "$repo_root/tools/verify-publish.sh" "${verify_args[@]}" || error_message="verify phase failed"
fi
if [[ -z "$error_message" ]]; then
  package_args=(--rid "$rid" --channel "$channel" --version "$version" --publish-dir "$publish_dir" --output-root "$package_root" --player-output-dir "$player_dir" --product-name "$product_name" --start-scene "$start_scene" --window-width "$window_width" --window-height "$window_height" --vsync "$vsync" --runtime-ui-backend "$runtime_ui_backend" --release-channel "$release_channel")
  if [[ -n "$content_root" ]]; then package_args+=(--content-root "$content_root"); fi
  for scene in "${include_scenes[@]}"; do package_args+=(--include-scene "$scene"); done
  if (( include_symbols || dev_layout )); then package_args+=(--include-symbols); fi
  run_phase "package" 60 82 bash "$repo_root/tools/package.sh" "${package_args[@]}" || error_message="package phase failed"
fi
if [[ -z "$error_message" ]]; then
  audit_args=(--publish-root "$publish_root" --package-root "$package_root" --product-name "$product_name" --required-scene "$start_scene")
  if [[ -n "$content_root" ]]; then audit_args+=(--skip-publish-content-audit --skip-demo-content-audit); fi
  if (( dev_layout || include_symbols )); then audit_args+=(--dev-layout); fi
  run_phase "audit" 82 100 bash "$repo_root/tools/audit-release-artifacts.sh" "${audit_args[@]}" || error_message="audit phase failed"
fi
set -e

if [[ -n "$error_message" ]]; then
  write_result false 1 "$error_message"
  exit 1
fi

write_result true 0 ""
exit 0
