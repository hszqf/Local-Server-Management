# Local Service Manager Experience 2026-07-08

- Loopback HTTP health checks should bypass system proxy settings. A local `127.0.0.1` health probe timing out is an unhealthy service state, not proof that the service is stopped.
- Long-running service start commands should use `detached: true` on `process` actions in `config.local.json`, otherwise the manager waits for the command to exit and can hit the action timeout.
- While an action keeps the manager busy, manual status refresh and the periodic auto-start pass are skipped; stale HTTP 200 text in the grid is not evidence that the origin is still running.
- Keep machine-specific service paths and private commands in ignored `config.local.json`; commit only generic schema/docs changes.
