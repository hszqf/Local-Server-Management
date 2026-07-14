# Local Service Manager Experience 2026-07-14 Background Remove Status

- A successful background-removal request can coexist with a stale or misleading manager display. Verify the actual service with `http://127.0.0.1:8765/health` and parse the returned `ok` field.
- Do not rely on normal-user `Get-CimInstance Win32_Process` command-line scans for this local service; this machine can deny CIM process access.
- Port 8765 can have another listener on `0.0.0.0` or `::` while the real background-removal service listens on `127.0.0.1`. Use the explicit loopback address for health and target only the `127.0.0.1:8765` listener when stopping the service.
