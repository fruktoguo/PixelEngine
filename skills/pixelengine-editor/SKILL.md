---
name: pixelengine-editor
description: Drive a live PixelEngine Editor through the authenticated pixelengine-editor CLI. Use when Codex needs to inspect or modify projects, scenes, hierarchy, Inspector fields, assets, settings, runtime state, Console, Profiler, screenshots, builds, or player processes without MCP, screen coordinates, or Computer Use; also use for revision-safe transactions, event resume, artifact verification, and CI editor workflows.
---

# PixelEngine Editor

Use the versioned local automation API only through `pixelengine-editor`. Do not read credential files, call the Named Pipe directly, use `--scripted-*` probes, or substitute UI coordinates.

## Resolve The CLI

Invoke `scripts/invoke.ps1`; it resolves `PIXELENGINE_EDITOR_CLI`, an installed `pixelengine-editor`, or the Release CLI under the current PixelEngine checkout. Pass CLI arguments unchanged:

```powershell
$skillRoot = Join-Path ($(if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $HOME '.codex' })) 'skills/pixelengine-editor'
$pe = Join-Path $skillRoot 'scripts/invoke.ps1'
& $pe --discovery-root $discovery discover
```

If discovery returns multiple live instances, select one explicitly with `--instance`. Keep one stable `--client-instance-id` across retries and event reconnects.

## Establish The Contract

1. Run `discover` and `ping`.
2. Run `capabilities --matrix` before a broad workflow. The Client independently verifies sorting, bidirectional UI links, canonical SHA256, and the discovery digest.
3. Run `help <capability-id>` before constructing an unfamiliar payload. Treat request/response Schema refs, scopes, modes, revision requirements, transaction mode, phase, event types, and artifact behavior as authoritative.
4. Use compact output for inspection. Use `--output json` only when parsing a one-shot result and `--output ndjson` for event streams.

Never guess a stable ID from a display title or array index. Read it from a snapshot or capability response.

## Read And Mutate Safely

Read the narrowest snapshot first. For a write:

- request only the scopes declared by the capability;
- pass the returned global/resource revision when required;
- use one stable idempotency key per logical mutation;
- on `revision_conflict`, reread state and decide from the new snapshot instead of forcing last-write-wins;
- use a transaction only when every operation declares transaction support;
- verify the committed state, and use the shared Editor Undo/Redo API when the workflow requires history proof.

Do not treat a timeout or local cancellation as proof that an executing mutation did not commit. Reread the affected resource.

For CLI transactions, use one bounded `transaction execute --plan-file` invocation. Never split begin,
staging calls, and commit across CLI processes: each process owns one connection, and disconnect discards
uncommitted staging. Use a long-lived .NET Client only when manual transaction control is required.

## Events And Reconnect

Use `events follow` with a stable subscription key. Persist the emitted resume token and acknowledged sequence. Reconnect with the same client instance ID, subscription key, token, and `--after-sequence`. On `resync_required` or `event_overflow`, fetch authoritative snapshots before continuing.

## Screenshots And Artifacts

Use `scene.capture`, `game.capture`, `project.asset.preview`, `console.export`, `profiler.export`, or `build logs` as appropriate. Always request or perform artifact verification before consuming the path. Check media type, byte length, SHA256, source revision, and optional dimensions/encoding. Never ask the wire protocol to inline large data.

```powershell
& $pe --discovery-root $discovery --output json call scene.capture --verify-artifact
```

## Build And Run

Use the named `build` and `player` commands. Preflight first, wait for terminal state, launch only the trusted launcher returned by a successful build, verify the running process identity, and terminate through the returned stable player process ID. Do not launch arbitrary executables or inject argv.

For multi-step authoring, runtime debugging, artifact, build, and recovery examples, read [references/workflows.md](references/workflows.md).
