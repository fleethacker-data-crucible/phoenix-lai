# Development branch reconciliation

2026-10-30

The old `doc/add-readme` branch is superseded by the current README, which describes the separate server, browser client and VS Code integration. The unrelated `fork/app-lifecycle` history described a former combined server. Its Presence policy, heartbeat/leaving behavior, graceful end session, runtime records and launcher options are ported onto the current client only. The model server remains independent. The original fork tip is retained under an archive tag when its branch is pruned; unrelated snapshot history is not imported into master. Unsafe broad process scanning and force-stopping from the old branch are replaced with runtime-root, command-line and live tool/PID identity checks and graceful-only shutdown.
