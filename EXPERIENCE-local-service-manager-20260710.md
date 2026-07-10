# Local Service Manager Experience 2026-07-10

- Keep machine-specific helper services in `config.local.json`; do not add local absolute paths to the committed example config.
- For a watcher such as `codex_watchdog.ps1`, use a `process` health check with `commandLineContainsAny` and a detached `process` start action.
- Per-service auto-start is registry state under `HKCU\Software\LocalServiceManager\ServiceAutoStart`; once enabled, the existing manager starts the service on app launch and during its periodic auto-start pass.
- If a managed service is itself a PowerShell script, process health checks must exclude the temporary health-check PowerShell process; otherwise the checker's own command line can match `commandLineContainsAny` and create a false running state.
