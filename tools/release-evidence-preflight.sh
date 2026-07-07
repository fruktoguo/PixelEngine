#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ps_script="$script_dir/release-evidence-preflight.ps1"
pwsh_command=""

try_select_powershell() {
  local candidate
  for candidate in pwsh pwsh.exe powershell.exe; do
    if ! command -v "$candidate" >/dev/null 2>&1; then
      continue
    fi

    if "$candidate" -NoLogo -NoProfile -Command '$PSVersionTable.PSVersion.ToString() | Out-Null' >/dev/null 2>&1; then
      pwsh_command="$candidate"
      return 0
    fi
  done

  return 1
}

write_missing_manifest_report() {
  mkdir -p "$artifacts"
  cat >"$artifacts/release-evidence-preflight.md" <<EOF
# Release evidence preflight

| Key | Value |
|---|---|
| status | blocked_missing_release_manifest |
| required_rids | $active_rids |
| required_channels | r2r; aot |
| required_package_count | $expected_package_count |
| evidence_manifest | $evidence_manifest_path |
| source | release-evidence-preflight.sh portable missing-manifest fallback |
EOF
}

evidence_manifest_path=""
artifacts="artifacts/release-evidence-preflight"
active_rids=""
expected_package_count=""
allow_blocked=0

usage() {
  cat <<'EOF'
Usage: tools/release-evidence-preflight.sh [options]

Options:
  --evidence-manifest-path, --evidence-manifest <path>
  --artifacts <dir>
  --active-rids <csv>
  --expected-package-count <count>
  --allow-blocked
EOF
}

while (($#)); do
  case "$1" in
    --evidence-manifest-path|--evidence-manifest)
      [[ $# -ge 2 ]] || { echo "Missing value for $1" >&2; exit 64; }
      evidence_manifest_path="$2"
      shift 2
      ;;
    --artifacts)
      [[ $# -ge 2 ]] || { echo "Missing value for $1" >&2; exit 64; }
      artifacts="$2"
      shift 2
      ;;
    --active-rids)
      [[ $# -ge 2 ]] || { echo "Missing value for $1" >&2; exit 64; }
      active_rids="$2"
      shift 2
      ;;
    --expected-package-count)
      [[ $# -ge 2 ]] || { echo "Missing value for $1" >&2; exit 64; }
      expected_package_count="$2"
      shift 2
      ;;
    --allow-blocked)
      allow_blocked=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 64
      ;;
  esac
done

if try_select_powershell; then
  if [[ "$pwsh_command" == *.exe ]]; then
    if command -v cygpath >/dev/null 2>&1; then
      ps_script="$(cygpath -w "$ps_script")"
    elif command -v wslpath >/dev/null 2>&1; then
      ps_script="$(wslpath -w "$ps_script")"
    fi
  fi

  args=(-NoLogo -NoProfile -ExecutionPolicy Bypass -File "$ps_script" -Artifacts "$artifacts")

  if [[ -n "$evidence_manifest_path" ]]; then
    args+=(-EvidenceManifestPath "$evidence_manifest_path")
  fi

  if [[ -n "$active_rids" ]]; then
    args+=(-ActiveRids "$active_rids")
  fi

  if [[ -n "$expected_package_count" ]]; then
    args+=(-ExpectedPackageCount "$expected_package_count")
  fi

  if (( allow_blocked )); then
    args+=(-AllowBlocked)
  fi

  exec "$pwsh_command" "${args[@]}"
fi

if (( allow_blocked )) && [[ -n "$evidence_manifest_path" ]] && [[ ! -f "$evidence_manifest_path" ]]; then
  write_missing_manifest_report
  exit 0
fi

echo "release-evidence-preflight.sh requires an executable pwsh, pwsh.exe, or powershell.exe." >&2
exit 127
