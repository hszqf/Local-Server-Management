# Background Removal Service Start Experience 2026-07-11

## Symptom

- LocalServiceManager reports the background-removal service as stopped after Start is clicked.
- Port 8765 is not listening and the configured stdout/stderr log files are never created.

## Root Cause

- A `powershell` action is executed as `powershell -Command <full script>`.
- The start script searched all process command lines for `bg_remove_service.api` or `run_server.ps1`.
- The search therefore matched the action's own PowerShell command line and exited through the already-running branch before `Start-Process`.
- The stop script used the same process scan and could also target its own action process.

## Durable Fix

- Any PowerShell action that searches command lines for its managed process must exclude `$PID` and ignore empty command lines.
- Keep machine paths and action bodies in ignored `config.local.json`; tracked documentation contains only the reusable rule.
- Verify the action through the same `powershell -Command` execution shape used by LocalServiceManager, then prove stop and restart behavior through the health endpoint.
