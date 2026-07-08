#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage: tools/audit-release-artifacts.sh [--publish-root <dir>] [--package-root <dir>] [--product-name <name>] [--required-scene <scene>] [--active-rids <csv>] [--dev-layout] [--skip-publish-content-audit] [--skip-demo-content-audit] [--require-all]

Defaults:
  --publish-root artifacts/publish
  --package-root artifacts/package
EOF
}

fail_usage() {
  echo "参数错误: $1" >&2
  usage
  exit 2
}

fail_audit() {
  echo "审计失败: $1" >&2
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

entry_name_for_rid() {
  local rid="$1"
  case "$rid" in
    win-*) echo "$assembly_base.exe" ;;
    *) echo "$assembly_base" ;;
  esac
}

box2d_dynamic_name_for_rid() {
  local rid="$1"
  case "$rid" in
    win-*) echo "box2d.dll" ;;
    linux-*) echo "libbox2d.so" ;;
    osx-*) echo "libbox2d.dylib" ;;
    *) fail_usage "Unsupported RID: $rid" ;;
  esac
}

ui_native_name_for_rid() {
  local rid="$1"
  case "$rid" in
    win-*) echo "PixelEngine.UI.Native.dll" ;;
    linux-*) echo "libPixelEngine.UI.Native.so" ;;
    osx-*) echo "libPixelEngine.UI.Native.dylib" ;;
    *) fail_usage "Unsupported RID: $rid" ;;
  esac
}

is_ultralight_native_file_name() {
  local name="${1,,}"
  case "$name" in
    ultralight*.dll|libultralight*.so|libultralight*.dylib|webcore*.dll|libwebcore*.so|libwebcore*.dylib|appcore*.dll|libappcore*.so|libappcore*.dylib)
      return 0
      ;;
  esac

  return 1
}

assert_no_inactive_ultralight_native() {
  local directory="$1"
  local found=()
  local file
  while IFS= read -r -d '' file; do
    if is_ultralight_native_file_name "$(basename "$file")"; then
      found+=("$file")
    fi
  done < <(find "$directory" -type f -print0)

  if (( ${#found[@]} > 0 )); then
    fail_audit "Ultralight optional profile inactive: disallowed Ultralight native; missing SDK provenance, commercial redistribution license, SHA256/NOTICE, codesign/notarize, and release artifact evidence: ${found[*]}"
  fi
}

assert_file_exists() {
  local path="$1"
  local message="$2"
  if [[ ! -f "$path" ]]; then
    fail_audit "$message: $path"
  fi
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

  fail_audit "找不到 SHA256 工具：需要 sha256sum、shasum 或 openssl。"
}

read_active_rids_from_json() {
  local config="$1"
  if [[ ! -f "$config" ]]; then
    printf '%s\n' "win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64"
    return
  fi

  if command -v python3 >/dev/null 2>&1; then
    python3 - "$config" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    data = json.load(handle)
rids = [item["rid"] for item in data.get("rids", []) if item.get("active")]
if not rids:
    raise SystemExit("release-rids.json active RID set is empty")
print(" ".join(rids))
PY
    return
  fi

  fail_audit "读取 release-rids.json 需要 python3，或通过 --active-rids 显式传入激活 RID。"
}

resolve_active_rids() {
  local value="$1"
  local valid=" win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64 "
  local resolved=()

  if [[ -n "$value" ]]; then
    local item
    IFS=',' read -r -a resolved <<< "$value"
    for item in "${resolved[@]}"; do
      item="${item//[[:space:]]/}"
      if [[ -z "$item" ]]; then
        fail_usage "--active-rids 不能为空。"
      fi

      if [[ "$valid" != *" $item "* ]]; then
        fail_usage "Unknown active RID: $item"
      fi
    done
    printf '%s\n' "${resolved[@]}"
    return
  fi

  local rid_text
  rid_text="$(read_active_rids_from_json "$repo_root/tools/release-rids.json")"
  local rid
  for rid in $rid_text; do
    if [[ "$valid" != *" $rid "* ]]; then
      fail_usage "release-rids.json contains unknown active RID: $rid"
    fi
    resolved+=("$rid")
  done

  if (( ${#resolved[@]} == 0 )); then
    fail_usage "active RID set is empty."
  fi

  printf '%s\n' "${resolved[@]}"
}

assert_no_static_openal_or_angle() {
  local directory="$1"
  local found=()
  local file
  while IFS= read -r -d '' file; do
    found+=("$file")
  done < <(
    find "$directory" -type f \
      \( -iname '*openal*.a' -o -iname '*openal*.lib' -o -iname '*angle*.a' -o -iname '*angle*.lib' \) \
      -print0
  )

  if (( ${#found[@]} > 0 )); then
    fail_audit "发行产物包含 OpenAL/ANGLE 静态库，违反 native fan-out 收敛要求: ${found[*]}"
  fi
}

assert_no_dynamic_box2d() {
  local directory="$1"
  local found=()
  local file
  while IFS= read -r -d '' file; do
    found+=("$file")
  done < <(
    find "$directory" -type f \
      \( -iname 'box2d.dll' -o -iname 'libbox2d.so' -o -iname 'libbox2d.dylib' \) \
      -print0
  )

  if (( ${#found[@]} > 0 )); then
    fail_audit "AOT 产物不应携带动态 Box2D: ${found[*]}"
  fi
}

assert_no_dynamic_ui_native() {
  local directory="$1"
  local found=()
  local file
  while IFS= read -r -d '' file; do
    found+=("$file")
  done < <(
    find "$directory" -type f \
      \( -iname 'PixelEngine.UI.Native.dll' -o -iname 'libPixelEngine.UI.Native.so' -o -iname 'libPixelEngine.UI.Native.dylib' \) \
      -print0
  )

  if (( ${#found[@]} > 0 )); then
    fail_audit "AOT 产物不应携带动态 UI native，应回退 ManagedFallback: ${found[*]}"
  fi
}

assert_linux_dynamic_link() {
  local entry_path="$1"
  local rid="$2"

  if [[ "$(uname -s)" != "Linux" || "$rid" != linux-* ]]; then
    return
  fi

  if ! command -v ldd >/dev/null 2>&1; then
    fail_audit "Linux 产物动态链接审计需要 ldd: $entry_path"
  fi

  local output
  if ! output="$(ldd "$entry_path" 2>&1)"; then
    fail_audit "ldd 审计失败: $entry_path"$'\n'"$output"
  fi

  local lower
  lower="$(printf '%s' "$output" | tr '[:upper:]' '[:lower:]')"
  if [[ "$lower" == *"statically linked"* || "$lower" == *"not a dynamic executable"* ]]; then
    fail_audit "Linux 入口疑似静态链接: $entry_path"$'\n'"$output"
  fi

  if [[ "$lower" == *"libc.a"* || "$lower" == *"static-pie"* ]]; then
    fail_audit "Linux 入口出现静态 libc 迹象: $entry_path"$'\n'"$output"
  fi

  if [[ "$lower" != *"libc.so"* ]]; then
    fail_audit "Linux 入口未显示动态 glibc/libc.so 依赖: $entry_path"$'\n'"$output"
  fi
}

list_package_entries() {
  local package="$1"
  case "$package" in
    *.zip)
      if command -v python3 >/dev/null 2>&1; then
        python3 - "$package" <<'PY'
import sys
import zipfile
with zipfile.ZipFile(sys.argv[1]) as archive:
    for name in archive.namelist():
        print(name)
PY
        return
      fi

      if command -v unzip >/dev/null 2>&1; then
        unzip -Z1 "$package"
        return
      fi

      fail_audit "检查 zip package 布局需要 python3 或 unzip: $package"
      ;;
    *.tar.gz)
      tar -tzf "$package"
      ;;
    *)
      fail_audit "未知 package 格式: $package"
      ;;
  esac
}

read_package_text_entry() {
  local package="$1"
  local entry="$2"
  case "$package" in
    *.zip)
      if command -v python3 >/dev/null 2>&1; then
        python3 - "$package" "$entry" <<'PY'
import sys
import zipfile
with zipfile.ZipFile(sys.argv[1]) as archive:
    try:
        data = archive.read(sys.argv[2])
    except KeyError:
        sys.exit(3)
sys.stdout.buffer.write(data)
PY
        return
      fi

      if command -v unzip >/dev/null 2>&1; then
        unzip -p "$package" "$entry"
        return
      fi

      fail_audit "读取 zip package 条目需要 python3 或 unzip: $package"
      ;;
    *.tar.gz)
      tar -xOf "$package" "$entry"
      ;;
    *)
      fail_audit "未知 package 格式: $package"
      ;;
  esac
}

is_disallowed_runtime_root_file() {
  local relative="$1"
  local name="${relative##*/}"
  [[ "$name" =~ \.(dll|pdb|xml)$ || "$name" =~ \.deps\.json$ || "$name" =~ \.runtimeconfig\.json$ ]]
}

is_disallowed_player_package_file() {
  local relative="$1"
  local name="${relative##*/}"
  if (( dev_layout )) && [[ "$name" == *.pdb || "$name" == *.xml ]]; then
    return 1
  fi

  [[ "$name" == *.pdb || "$name" == *.xml || "$name" == *.resources.dll || "$name" == "createdump.exe" || "$name" == "createdump" ]]
}

is_disallowed_player_only_file() {
  local relative="$1"
  local name="${relative##*/}"
  [[ "${name,,}" == "pixelengine.editor.dll" || "$name" == ImGuizmo* || "$name" == Hexa.NET.ImGuizmo* || "$name" == ImPlot* || "$name" == Hexa.NET.ImPlot* ]]
}

assert_no_duplicate_content_under_app() {
  local relative="$1"
  local package_name="$2"
  local prefix="$3"
  if [[ "$relative" == "app/content" || "$relative" == app/content/* ]]; then
    fail_audit "$prefix 不应在 app/ 下重复打包 content；content 只能位于包根 content/: $package_name -> $relative"
  fi
}

assert_no_duplicate_windows_launcher_under_app() {
  local relative="$1"
  local rid="$2"
  local package_name="$3"
  local prefix="$4"
  if [[ "$rid" == win-* && "$relative" == "app/$assembly_base.exe" ]]; then
    fail_audit "$prefix 不应在 app/ 下重复保留原始启动 exe；根目录只允许 $launcher_base.exe: $package_name -> $relative"
  fi
}

contains_item() {
  local needle="$1"
  shift
  local item
  for item in "$@"; do
    [[ "$item" == "$needle" ]] && return 0
  done
  return 1
}

assert_friendly_package_layout() {
  local package="$1"
  local name
  name="$(basename "$package")"
  local rid=""
  local channel=""
  if [[ "$name" =~ ^PixelEngine-Demo-.+-(win-x64|win-arm64|linux-x64|linux-arm64|osx-x64|osx-arm64)-(r2r|aot)\.(zip|tar\.gz)$ ]]; then
    rid="${BASH_REMATCH[1]}"
    channel="${BASH_REMATCH[2]}"
  else
    fail_audit "package 文件名不符合发行命名: $name"
  fi

  local root_name="$name"
  case "$root_name" in
    *.zip) root_name="${root_name%.zip}" ;;
    *.tar.gz) root_name="${root_name%.tar.gz}" ;;
  esac

  local launcher
  local entry=""
  if [[ "$rid" == win-* ]]; then
    launcher="$launcher_base.exe"
    [[ "$channel" == "r2r" ]] && entry="app/$assembly_base.dll"
  else
    launcher="$launcher_base.sh"
    entry="app/$assembly_base"
  fi

  local has_readme=0
  local has_notice=0
  local has_launcher=0
  local has_entry=0
  local has_materials=0
  local has_reactions=0
  local has_weapons=0
  local has_gravel_texture=0
  local has_boundary_texture=0
  local has_scene=0
  local has_ui_native=0
  local app_files=()
  local archive_entry
  while IFS= read -r archive_entry || [[ -n "$archive_entry" ]]; do
    local raw="$archive_entry"
    local is_directory=0
    [[ "$raw" == */ ]] && is_directory=1
    archive_entry="${raw%/}"
    [[ -z "$archive_entry" ]] && continue
    if [[ "$archive_entry" != */* ]]; then
      [[ "$archive_entry" == "$root_name" ]] || fail_audit "package 内根目录名称不符合包名: $name -> $archive_entry"
      continue
    fi

    local root="${archive_entry%%/*}"
    [[ "$root" == "$root_name" ]] || fail_audit "package 内根目录名称不符合包名: $name -> $root"
    local relative="${archive_entry#*/}"
    [[ -z "$relative" ]] && continue
    assert_no_duplicate_content_under_app "$relative" "$name" "package"
    assert_no_duplicate_windows_launcher_under_app "$relative" "$rid" "$name" "package"

    case "$relative" in
      README.txt) has_readme=1 ;;
      NOTICE.txt) has_notice=1 ;;
      SHA256SUMS) ;;
      "$launcher") has_launcher=1 ;;
      "$entry") has_entry=1 ;;
      content/materials.json) has_materials=1 ;;
      content/reactions.json) has_reactions=1 ;;
      content/weapons.json) has_weapons=1 ;;
      content/textures/17_gravel.png) has_gravel_texture=1 ;;
      content/textures/18_boundary_stone.png) has_boundary_texture=1 ;;
      "content/$required_scene") has_scene=1 ;;
      "app/runtimes/$rid/native/$(ui_native_name_for_rid "$rid")") has_ui_native=1 ;;
    esac

    if is_disallowed_runtime_root_file "$relative" && [[ "$relative" != app/* ]]; then
      fail_audit "package 根目录不应包含运行时依赖，请放入 app/: $name -> $relative"
    fi

    if [[ "$relative" != app/* && "$relative" != content/* && "$relative" != app && "$relative" != content && "$relative" != "README.txt" && "$relative" != "SHA256SUMS" && "$relative" != "$launcher" && ! "$relative" =~ ^(LICENSE|NOTICE)(\..+)?$ ]]; then
      fail_audit "package 根目录只允许启动入口/README/SHA256SUMS/许可文件与 app/content 目录: $name -> $relative"
    fi

    if (( ! is_directory )); then
      if is_disallowed_player_package_file "$relative"; then
        fail_audit "package 不应包含玩家无关的调试、文档、诊断辅助或本地化卫星资源文件: $name -> $relative"
      fi

      if [[ "$relative" == app/* ]] && is_disallowed_player_only_file "$relative"; then
        fail_audit "package 不应包含编辑器专属闭包: $name -> $relative"
      fi

      if is_ultralight_native_file_name "${relative##*/}"; then
        fail_audit "package contains inactive Ultralight native; missing SDK provenance, commercial redistribution license, SHA256/NOTICE, and release artifact evidence: $name -> $relative"
      fi

      if [[ "$channel" != "r2r" ]]; then
        case "${relative##*/}" in
          PixelEngine.UI.Native.dll|libPixelEngine.UI.Native.so|libPixelEngine.UI.Native.dylib)
            fail_audit "package AOT 通道不应携带动态 UI native，应回退 ManagedFallback: $name -> $relative"
            ;;
        esac
      fi

      app_files+=("$relative")
    fi
  done < <(list_package_entries "$package")

  (( has_readme )) || fail_audit "package 缺少 README.txt: $name"
  (( has_notice )) || fail_audit "package 缺少 NOTICE.txt: $name"
  (( has_launcher )) || fail_audit "package 缺少 launcher: $name -> $launcher"
  [[ -z "$entry" || "$has_entry" -eq 1 ]] || fail_audit "package 缺少 app 依赖入口: $name -> $entry"
  if (( ! skip_demo_content_audit )); then
    (( has_materials )) || fail_audit "package 缺少 content/materials.json: $name"
    (( has_reactions )) || fail_audit "package 缺少 content/reactions.json: $name"
    (( has_weapons )) || fail_audit "package 缺少 content/weapons.json: $name"
    (( has_gravel_texture )) || fail_audit "package 缺少 content/textures/17_gravel.png: $name"
    (( has_boundary_texture )) || fail_audit "package 缺少 content/textures/18_boundary_stone.png: $name"
  fi
  (( has_scene )) || fail_audit "package 缺少 content/$required_scene: $name"
  [[ "$channel" != "r2r" || "$has_ui_native" -eq 1 ]] || fail_audit "package 缺少动态 UI native: $name -> app/runtimes/$rid/native/$(ui_native_name_for_rid "$rid")"

  declare -A declared_app_files=()
  local checksum_entry="$root_name/SHA256SUMS"
  local checksum_line
  local checksum_read=0
  while IFS= read -r checksum_line || [[ -n "$checksum_line" ]]; do
    checksum_line="${checksum_line%$'\r'}"
    checksum_read=1
    [[ -z "${checksum_line//[[:space:]]/}" ]] && continue
    if [[ ! "$checksum_line" =~ ^([0-9a-fA-F]{64})[[:space:]]+\*?(.+)$ ]]; then
      fail_audit "package 内 SHA256SUMS 行格式无效: $name -> $checksum_line"
    fi

    local checksum_name="${BASH_REMATCH[2]}"
    checksum_name="${checksum_name#./}"
    [[ "$checksum_name" != "SHA256SUMS" ]] || fail_audit "package 内 SHA256SUMS 不应覆盖自身: $name"
    contains_item "$checksum_name" "${app_files[@]}" || fail_audit "package 内 SHA256SUMS 指向不存在的文件: $name -> $checksum_name"
    [[ -z "${declared_app_files[$checksum_name]+x}" ]] || fail_audit "package 内 SHA256SUMS 重复条目: $name -> $checksum_name"
    declared_app_files["$checksum_name"]=1
  done < <(read_package_text_entry "$package" "$checksum_entry")

  (( checksum_read )) || fail_audit "package 内 SHA256SUMS 为空或不可读: $name"
  local app_file
  for app_file in "${app_files[@]}"; do
    [[ "$app_file" == "SHA256SUMS" ]] && continue
    [[ -n "${declared_app_files[$app_file]+x}" ]] || fail_audit "package 内 SHA256SUMS 未覆盖文件: $name -> $app_file"
  done
}

assert_friendly_expanded_package_layout() {
  local directory="$1"
  local name
  name="$(basename "$directory")"
  local rid=""
  local channel=""
  if [[ "$name" =~ ^PixelEngine-Demo-.+-(win-x64|win-arm64|linux-x64|linux-arm64|osx-x64|osx-arm64)-(r2r|aot)$ ]]; then
    rid="${BASH_REMATCH[1]}"
    channel="${BASH_REMATCH[2]}"
  else
    fail_audit "展开 package 目录名不符合发行命名: $name"
  fi

  local launcher
  local entry=""
  if [[ "$rid" == win-* ]]; then
    launcher="$launcher_base.exe"
    [[ "$channel" == "r2r" ]] && entry="app/$assembly_base.dll"
  else
    launcher="$launcher_base.sh"
    entry="app/$assembly_base"
  fi

  local has_readme=0
  local has_notice=0
  local has_launcher=0
  local has_entry=0
  local has_materials=0
  local has_reactions=0
  local has_weapons=0
  local has_gravel_texture=0
  local has_boundary_texture=0
  local has_scene=0
  local has_ui_native=0
  local expanded_files=()
  local expanded_entries=()
  local path
  while IFS= read -r -d '' path; do
    local relative="${path#"$directory"/}"
    relative="${relative//\\//}"
    expanded_entries+=("$relative")
    assert_no_duplicate_content_under_app "$relative" "$name" "展开 package"
    assert_no_duplicate_windows_launcher_under_app "$relative" "$rid" "$name" "展开 package"

    case "$relative" in
      README.txt) has_readme=1 ;;
      NOTICE.txt) has_notice=1 ;;
      SHA256SUMS) ;;
      "$launcher") has_launcher=1 ;;
      "$entry") has_entry=1 ;;
      content/materials.json) has_materials=1 ;;
      content/reactions.json) has_reactions=1 ;;
      content/weapons.json) has_weapons=1 ;;
      content/textures/17_gravel.png) has_gravel_texture=1 ;;
      content/textures/18_boundary_stone.png) has_boundary_texture=1 ;;
      "content/$required_scene") has_scene=1 ;;
      "app/runtimes/$rid/native/$(ui_native_name_for_rid "$rid")") has_ui_native=1 ;;
    esac

    if is_disallowed_runtime_root_file "$relative" && [[ "$relative" != app/* ]]; then
      fail_audit "展开 package 根目录不应包含运行时依赖，请放入 app/: $name -> $relative"
    fi

    if [[ "$relative" != app/* && "$relative" != content/* && "$relative" != app && "$relative" != content && "$relative" != "README.txt" && "$relative" != "SHA256SUMS" && "$relative" != "$launcher" && ! "$relative" =~ ^(LICENSE|NOTICE)(\..+)?$ ]]; then
      fail_audit "展开 package 根目录只允许启动入口/README/SHA256SUMS/许可文件与 app/content 目录: $name -> $relative"
    fi

    if [[ -f "$path" ]]; then
      if is_disallowed_player_package_file "$relative"; then
        fail_audit "展开 package 不应包含玩家无关的调试、文档、诊断辅助或本地化卫星资源文件: $name -> $relative"
      fi

      if [[ "$relative" == app/* ]] && is_disallowed_player_only_file "$relative"; then
        fail_audit "展开 package 不应包含编辑器专属闭包: $name -> $relative"
      fi

      if is_ultralight_native_file_name "${relative##*/}"; then
        fail_audit "expanded package contains inactive Ultralight native; missing SDK provenance, commercial redistribution license, SHA256/NOTICE, and release artifact evidence: $name -> $relative"
      fi

      if [[ "$channel" != "r2r" ]]; then
        case "${relative##*/}" in
          PixelEngine.UI.Native.dll|libPixelEngine.UI.Native.so|libPixelEngine.UI.Native.dylib)
            fail_audit "展开 package AOT 通道不应携带动态 UI native，应回退 ManagedFallback: $name -> $relative"
            ;;
        esac
      fi

      expanded_files+=("$relative")
    fi
  done < <(find "$directory" -mindepth 1 -print0 | sort -z)

  (( has_readme )) || fail_audit "展开 package 缺少 README.txt: $name"
  (( has_notice )) || fail_audit "展开 package 缺少 NOTICE.txt: $name"
  (( has_launcher )) || fail_audit "展开 package 缺少 launcher: $name -> $launcher"
  [[ -z "$entry" || "$has_entry" -eq 1 ]] || fail_audit "展开 package 缺少 app 依赖入口: $name -> $entry"
  if (( ! skip_demo_content_audit )); then
    (( has_materials )) || fail_audit "展开 package 缺少 content/materials.json: $name"
    (( has_reactions )) || fail_audit "展开 package 缺少 content/reactions.json: $name"
    (( has_weapons )) || fail_audit "展开 package 缺少 content/weapons.json: $name"
    (( has_gravel_texture )) || fail_audit "展开 package 缺少 content/textures/17_gravel.png: $name"
    (( has_boundary_texture )) || fail_audit "展开 package 缺少 content/textures/18_boundary_stone.png: $name"
  fi
  (( has_scene )) || fail_audit "展开 package 缺少 content/$required_scene: $name"
  [[ "$channel" != "r2r" || "$has_ui_native" -eq 1 ]] || fail_audit "展开 package 缺少动态 UI native: $name -> app/runtimes/$rid/native/$(ui_native_name_for_rid "$rid")"

  local checksum_path="$directory/SHA256SUMS"
  assert_file_exists "$checksum_path" "展开 package 缺少 SHA256SUMS"
  declare -A declared_expanded_files=()
  local checksum_line
  while IFS= read -r checksum_line || [[ -n "$checksum_line" ]]; do
    checksum_line="${checksum_line%$'\r'}"
    [[ -z "${checksum_line//[[:space:]]/}" ]] && continue
    if [[ ! "$checksum_line" =~ ^([0-9a-fA-F]{64})[[:space:]]+\*?(.+)$ ]]; then
      fail_audit "展开 package 内 SHA256SUMS 行格式无效: $name -> $checksum_line"
    fi

    local checksum_name="${BASH_REMATCH[2]}"
    checksum_name="${checksum_name#./}"
    [[ "$checksum_name" != "SHA256SUMS" ]] || fail_audit "展开 package 内 SHA256SUMS 不应覆盖自身: $name"
    contains_item "$checksum_name" "${expanded_files[@]}" || fail_audit "展开 package 内 SHA256SUMS 指向不存在的文件: $name -> $checksum_name"
    [[ -z "${declared_expanded_files[$checksum_name]+x}" ]] || fail_audit "展开 package 内 SHA256SUMS 重复条目: $name -> $checksum_name"
    declared_expanded_files["$checksum_name"]=1
  done < "$checksum_path"

  local expanded_file
  for expanded_file in "${expanded_files[@]}"; do
    [[ "$expanded_file" == "SHA256SUMS" ]] && continue
    [[ -n "${declared_expanded_files[$expanded_file]+x}" ]] || fail_audit "展开 package 内 SHA256SUMS 未覆盖文件: $name -> $expanded_file"
  done
}

audit_publish_directory() {
  local rid="$1"
  local channel="$2"
  local directory="$publish_root/$rid-$channel"

  if [[ ! -d "$directory" ]]; then
    if (( require_all )); then
      fail_audit "缺少发布目录: $directory"
    fi
    return 0
  fi

  local entry="$directory/$(entry_name_for_rid "$rid")"
  assert_file_exists "$entry" "缺少发布入口"
  if (( ! skip_publish_content_audit )); then
    assert_file_exists "$directory/content/materials.json" "缺少 content/materials.json"
    assert_file_exists "$directory/content/reactions.json" "缺少 content/reactions.json"
    assert_file_exists "$directory/content/weapons.json" "缺少 content/weapons.json"
    assert_file_exists "$directory/content/textures/17_gravel.png" "缺少 content/textures/17_gravel.png"
    assert_file_exists "$directory/content/textures/18_boundary_stone.png" "缺少 content/textures/18_boundary_stone.png"
    assert_file_exists "$directory/content/$required_scene" "缺少 content/$required_scene"
  fi

  local box2d_path="$directory/runtimes/$rid/native/$(box2d_dynamic_name_for_rid "$rid")"
  local ui_native_path="$directory/runtimes/$rid/native/$(ui_native_name_for_rid "$rid")"
  if [[ "$channel" == "r2r" ]]; then
    assert_file_exists "$box2d_path" "R2R 产物缺少动态 Box2D"
    assert_file_exists "$ui_native_path" "R2R 产物缺少动态 UI native"
  else
    assert_no_dynamic_box2d "$directory"
    assert_no_dynamic_ui_native "$directory"
  fi

  assert_no_static_openal_or_angle "$directory"
  assert_no_inactive_ultralight_native "$directory"
  assert_linux_dynamic_link "$entry" "$rid"
  echo "Publish OK: $rid/$channel"
}

is_release_package_name() {
  local name="$1"
  [[ "$name" =~ ^PixelEngine-Demo-.+-(win-x64|win-arm64|linux-x64|linux-arm64|osx-x64|osx-arm64)-(r2r|aot)\.(zip|tar\.gz)$ ]]
}

collect_packages() {
  packages=()
  package_dirs=()
  if [[ ! -d "$package_root" ]]; then
    if (( require_all )); then
      fail_audit "缺少 package 目录: $package_root"
    fi
    return 0
  fi

  local package
  while IFS= read -r -d '' package; do
    packages+=("$package")
  done < <(
    find "$package_root" -maxdepth 1 -type f \
      \( -name 'PixelEngine-Demo-*.zip' -o -name 'PixelEngine-Demo-*.tar.gz' \) \
      -print0 | sort -z
  )

  local package_dir
  while IFS= read -r -d '' package_dir; do
    package_dirs+=("$package_dir")
  done < <(
    find "$package_root" -maxdepth 1 -type d \
      -name 'PixelEngine-Demo-*' \
      -print0 | sort -z
  )
}

assert_expected_packages() {
  local rid
  local channel
  local package
  local name
  local count_for_pair
  local expected_count=0
  declare -A package_stems=()
  declare -A expanded_stems=()

  for package in "${packages[@]}"; do
    name="$(basename "$package")"
    if ! is_release_package_name "$name"; then
      fail_audit "package 文件名不符合发行命名: $name"
    fi

    assert_friendly_package_layout "$package"
    local stem="$name"
    case "$stem" in
      *.zip) stem="${stem%.zip}" ;;
      *.tar.gz) stem="${stem%.tar.gz}" ;;
    esac
    package_stems["$stem"]=1
  done

  for package_dir in "${package_dirs[@]}"; do
    name="$(basename "$package_dir")"
    if [[ ! "$name" =~ ^PixelEngine-Demo-.+-(win-x64|win-arm64|linux-x64|linux-arm64|osx-x64|osx-arm64)-(r2r|aot)$ ]]; then
      fail_audit "展开 package 目录名不符合发行命名: $name"
    fi

    if [[ -z "${package_stems[$name]+x}" ]]; then
      fail_audit "展开 package 目录缺少对应归档: $name"
    fi

    expanded_stems["$name"]=1
    assert_friendly_expanded_package_layout "$package_dir"
  done

  for name in "${!package_stems[@]}"; do
    if [[ -z "${expanded_stems[$name]+x}" ]]; then
      fail_audit "package 缺少同名展开目录: $name"
    fi
  done

  local expected_package_count=$((${#rids[@]} * ${#channels[@]}))
  if (( require_all && ${#packages[@]} != expected_package_count )); then
    fail_audit "package 数量不完整：期望 $expected_package_count，实际 ${#packages[@]}。"
  fi

  for rid in "${rids[@]}"; do
    for channel in "${channels[@]}"; do
      count_for_pair=0
      for package in "${packages[@]}"; do
        name="$(basename "$package")"
        if [[ "$name" =~ -$rid-$channel\.(zip|tar\.gz)$ ]]; then
          ((count_for_pair += 1))
        fi
      done

      if (( require_all && count_for_pair == 0 )); then
        fail_audit "缺少 package: $rid/$channel"
      fi

      if (( count_for_pair > 1 )); then
        fail_audit "同一 RID/channel 存在多个 package: $rid/$channel"
      fi

      expected_count=$((expected_count + count_for_pair))
    done
  done

  if (( expected_count != ${#packages[@]} )); then
    fail_audit "package 集合包含无法归属到 RID/channel 的文件。"
  fi
}

assert_checksums() {
  if (( ${#packages[@]} == 0 )); then
    return
  fi

  local checksum_path="$package_root/SHA256SUMS"
  assert_file_exists "$checksum_path" "缺少 SHA256SUMS"

  declare -A expected_hash_by_name=()
  declare -A package_by_name=()
  local package
  local package_name
  for package in "${packages[@]}"; do
    package_name="$(basename "$package")"
    package_by_name["$package_name"]="$package"
  done

  local line
  local hash
  local checksum_name
  while IFS= read -r line || [[ -n "$line" ]]; do
    line="${line%$'\r'}"
    [[ -z "${line//[[:space:]]/}" ]] && continue
    if [[ ! "$line" =~ ^([0-9a-fA-F]{64})[[:space:]]+\*?(.+)$ ]]; then
      fail_audit "SHA256SUMS 行格式无效: $line"
    fi

    hash="${BASH_REMATCH[1],,}"
    checksum_name="${BASH_REMATCH[2]}"
    checksum_name="${checksum_name#./}"
    if [[ "$checksum_name" == */* || "$checksum_name" == *\\* ]]; then
      fail_audit "SHA256SUMS 只允许 package root 下的文件名: $checksum_name"
    fi

    if [[ -z "${package_by_name[$checksum_name]+x}" ]]; then
      fail_audit "SHA256SUMS 包含 package root 下不存在或非发行包的条目: $checksum_name"
    fi

    if [[ -n "${expected_hash_by_name[$checksum_name]+x}" ]]; then
      fail_audit "SHA256SUMS 重复条目: $checksum_name"
    fi

    expected_hash_by_name["$checksum_name"]="$hash"
  done < "$checksum_path"

  for package in "${packages[@]}"; do
    package_name="$(basename "$package")"
    if [[ -z "${expected_hash_by_name[$package_name]+x}" ]]; then
      fail_audit "SHA256SUMS 未覆盖 package: $package_name"
    fi

    local actual
    actual="$(sha256_file "$package")"
    if [[ "$actual" != "${expected_hash_by_name[$package_name]}" ]]; then
      fail_audit "SHA256 mismatch: $package_name"
    fi
  done

  if (( ${#expected_hash_by_name[@]} != ${#packages[@]} )); then
    fail_audit "SHA256SUMS 条目数与 package 数不一致。"
  fi
}

audit_packages() {
  collect_packages
  assert_expected_packages
  assert_checksums
  if (( ${#packages[@]} > 0 )); then
    echo "Package OK: ${#packages[@]} expanded=${#package_dirs[@]}"
  elif (( ! require_all )); then
    echo "Package skipped: no packages under $package_root"
  fi
}

publish_root=""
package_root=""
product_name=""
assembly_name=""
required_scene="scenes/lava-mine.scene"
active_rids_arg=""
dev_layout=0
skip_publish_content_audit=0
skip_demo_content_audit=0
require_all=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --publish-root)
      require_value "$1" "${2:-}"
      publish_root="$2"
      shift 2
      ;;
    --package-root)
      require_value "$1" "${2:-}"
      package_root="$2"
      shift 2
      ;;
    --product-name)
      require_value "$1" "${2:-}"
      product_name="$2"
      shift 2
      ;;
    --assembly-name)
      require_value "$1" "${2:-}"
      assembly_name="$2"
      shift 2
      ;;
    --required-scene|--start-scene)
      require_value "$1" "${2:-}"
      required_scene="${2//\\//}"
      required_scene="${required_scene#/}"
      [[ "$required_scene" == scenes/* ]] || required_scene="scenes/$required_scene"
      [[ "$required_scene" == *.scene ]] || required_scene="$required_scene.scene"
      shift 2
      ;;
    --active-rids)
      require_value "$1" "${2:-}"
      active_rids_arg="$2"
      shift 2
      ;;
    --dev-layout)
      dev_layout=1
      shift
      ;;
    --skip-publish-content-audit)
      skip_publish_content_audit=1
      shift
      ;;
    --skip-demo-content-audit)
      skip_demo_content_audit=1
      shift
      ;;
    --require-all)
      require_all=1
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

channels=(r2r aot)
packages=()
package_dirs=()
[[ "$required_scene" == *.scene ]] || required_scene="$required_scene.scene"
launcher_base="${product_name:-PixelEngine Demo}"
if [[ -n "$assembly_name" ]]; then
  assembly_base="$assembly_name"
else
  assembly_base="PixelEngine.Demo"
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
rids=()
while IFS= read -r rid; do
  [[ -n "$rid" ]] && rids+=("$rid")
done < <(resolve_active_rids "$active_rids_arg")

if [[ -z "$publish_root" ]]; then
  publish_root="$repo_root/artifacts/publish"
else
  publish_root="$(normalize_path "$publish_root")"
fi

if [[ -z "$package_root" ]]; then
  package_root="$repo_root/artifacts/package"
else
  package_root="$(normalize_path "$package_root")"
fi

if [[ -d "$publish_root" ]]; then
  publish_root="$(cd "$publish_root" && pwd)"
fi

if [[ -d "$package_root" ]]; then
  package_root="$(cd "$package_root" && pwd)"
fi

for rid in "${rids[@]}"; do
  for channel in "${channels[@]}"; do
    audit_publish_directory "$rid" "$channel"
  done
done

audit_packages
echo "Release artifact audit completed."
