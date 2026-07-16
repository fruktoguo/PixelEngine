# PixelEngine Editor Workflows

## Scope Map

| Scope | Use |
|---|---|
| `editor.read` | Snapshots, catalogs, captures, logs, profiler, artifacts |
| `editor.control` | Selection, authoring, Play controls, transactions, Undo/Redo |
| `project.write` | Project, Scene, folder, asset, import, prefab mutations |
| `settings.write` | Preferences and Project/Player/Build settings |
| `process.build` | Build preflight, start, wait, cancel, logs |
| `process.launch` | Trusted player launch, wait, terminate |
| `automation.admin` | Administrative artifact/session operations only |

Always use the exact scope list from `help <capability-id>`.

## Authoring Sequence

1. Read `workspace.get`, `scene.get`, and the relevant hierarchy/Inspector snapshot.
2. Preserve stable IDs and the returned revision.
3. Begin a transaction when all intended writes allow it.
4. Invoke writes with expected revisions, stable idempotency keys, and the transaction ID.
5. Commit once, reread the affected resources, then test Undo and Redo if requested.
6. Save through `scene.save` only after the committed authoring state is verified.

Use payload files for nested requests so JSON quoting cannot alter values:

```powershell
& $pe --discovery-root $discovery --scopes 'editor.read,editor.control,project.write' `
  --output json call hierarchy.selection.set --payload-file selection.json `
  --expected-global 17 --expected-resource editor:selection=4 `
  --idempotency-key select-player-01
```

## Runtime Debug Sequence

1. Call `play.enter` and retain the play session ID.
2. Read runtime world/entities/components and Console/Profiler snapshots.
3. Call `play.pause`, inspect, then `play.step` and inspect the new revision.
4. Call `play.stop` and verify Edit mode.
5. Enter Play a second time and verify a new play-session-scoped runtime identity.

Never reuse runtime entity/component IDs across Play sessions.

## Event Recovery

```powershell
& $pe --discovery-root $discovery --client-instance-id ci-run-42 --output ndjson `
  events follow --subscription-key ci-observer-42 `
  --types editor.scene.changed,editor.play.changed --max-events 100
```

Persist the resume token and last ack from stderr/control output. After a Server restart, rediscover the new instance and reconnect. Treat an old resume token as invalid for the new Server; fetch fresh snapshots.

## Build And Player

```powershell
& $pe --discovery-root $discovery --scopes 'editor.read,process.build' build preflight
& $pe --discovery-root $discovery --scopes 'editor.read,process.build' --output json `
  build start --wait --idempotency-key build-ci-42
& $pe --discovery-root $discovery --scopes 'editor.read,process.build,process.launch' `
  player launch <build-id> --wait
& $pe --discovery-root $discovery --scopes 'editor.read,process.launch' `
  player terminate <player-process-id> --wait
```

Export build logs as a verified artifact. A successful query is not the same as a successful build/player terminal state; inspect state or use `wait`.

## Failure Rules

- Permission error: request the missing declared scope, never broaden to all scopes by default.
- Revision conflict: reread and merge intentionally.
- Timeout/cancel: reread state; do not assume rollback.
- Event overflow: resync authoritative snapshots.
- Artifact mismatch: do not consume the file.
- Build/player failure: keep the stable job/process ID and inspect structured status/log artifacts.
