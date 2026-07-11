# Local Service Manager TODO 2026-07-11

- [x] Confirm the displayed HTTP 200 can remain stale while the manager is busy.
- [x] Make the fixed Cloudflare Tunnel launcher detached in local config.
- [x] Restart the manager and verify Demopool status refresh and auto-start.

Outcome: the non-detached tunnel start kept the manager busy, so manual refresh and
the periodic auto-start patrol were skipped. The local tunnel action now uses
`detached: true`; the manager was restarted and the Demopool page and health
endpoints returned HTTP 200 after the next patrol cycle.
