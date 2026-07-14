# Local Service Manager Experience 2026-07-14 Stale Background Remove UI

- If a user sends an older screenshot, verify current state with live health and process data before changing manager code.
- `http://127.0.0.1:8765/` returns 404 for the background-removal service, while `http://127.0.0.1:8765/health` returns the real service health.
- Restarting LocalServiceManager can clear stale UI/action state after local config edits; the generic manager code did not need service-specific changes.

- Manual refresh should not be silently blocked by a long-running start/stop action; keep timer refresh conservative, but allow explicit user refresh to update service health while actions are busy.
