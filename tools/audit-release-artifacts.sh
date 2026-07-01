#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
Usage: tools/audit-release-artifacts.sh [--publish-root <dir>] [--package-root <dir>] [--require-all]

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
    win-*) echo "PixelEngine.Demo.exe" ;;
    *) echo "PixelEngine.Demo" ;;
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
  assert_file_exists "$directory/content/materials.json" "缺少 content/materials.json"
  assert_file_exists "$directory/content/reactions.json" "缺少 content/reactions.json"
  assert_file_exists "$directory/content/scenes/lava-mine.scene" "缺少 content/scenes/lava-mine.scene"

  local box2d_path="$directory/runtimes/$rid/native/$(box2d_dynamic_name_for_rid "$rid")"
  if [[ "$channel" == "r2r" ]]; then
    assert_file_exists "$box2d_path" "R2R 产物缺少动态 Box2D"
  else
    assert_no_dynamic_box2d "$directory"
  fi

  assert_no_static_openal_or_angle "$directory"
  assert_linux_dynamic_link "$entry" "$rid"
  echo "Publish OK: $rid/$channel"
}

is_release_package_name() {
  local name="$1"
  [[ "$name" =~ ^PixelEngine-Demo-.+-(win-x64|win-arm64|linux-x64|linux-arm64|osx-x64|osx-arm64)-(r2r|aot)\.(zip|tar\.gz)$ ]]
}

collect_packages() {
  packages=()
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
}

assert_expected_packages() {
  local rid
  local channel
  local package
  local name
  local count_for_pair
  local expected_count=0

  for package in "${packages[@]}"; do
    name="$(basename "$package")"
    if ! is_release_package_name "$name"; then
      fail_audit "package 文件名不符合发行命名: $name"
    fi
  done

  if (( require_all && ${#packages[@]} != 12 )); then
    fail_audit "package 数量不完整：期望 12，实际 ${#packages[@]}。"
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
    echo "Package OK: ${#packages[@]}"
  elif (( ! require_all )); then
    echo "Package skipped: no packages under $package_root"
  fi
}

publish_root=""
package_root=""
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

rids=(win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64)
channels=(r2r aot)
packages=()

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

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
