# 2026-07-10 CI-001 workflow validation

taskIds: CI-001
commit: b7fcf5321a4daf5d5a282350fd61e4886cc0bd13
runSessionId: local-20260710-ci001-actionlint
hardware: Microsoft Windows 11 专业版 build 26100; local static validation; no GitHub runner run
commands: `python -c "import yaml, pathlib; yaml.safe_load(pathlib.Path('.github/workflows/ci.yml').read_text(encoding='utf-8'))"`; `$env:GOSUMDB='off'; go run github.com/rhysd/actionlint/cmd/actionlint@v1.7.12 .github/workflows/ci.yml`
reportPath: `docs/evidence-2026-07-10-ci-001-workflow-validation.md`

## Result

```text
PyYAML parse ok
actionlint v1.7.12: exit 0
build_test_matrix_count=6
build_test_rids=win-x64,win-arm64,linux-x64,linux-arm64,osx-x64,osx-arm64
win_arm64_build_only=true
verify_publish_matrix_count=4
```

The invalid dynamic `shell: ${{ runner.os == 'Windows' && 'pwsh' || 'bash' }}` entries were replaced with explicit Windows and non-Windows steps. The matrix was not reduced. A push and the first remote workflow run were intentionally not performed; those are the external conditions tracked by `CI-002`.
