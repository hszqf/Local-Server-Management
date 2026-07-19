# Native Process Log Redirection TODO 2026-07-19

- [x] Replace the machine-local background-removal start action's PowerShell stream redirection with a hidden child whose log redirection does not retain the manager's capture pipes.
- [x] Poll health once per second for up to five seconds and fail early when the child process exits.
- [x] Verify the service stops cleanly, starts healthy on port 8765, writes Uvicorn startup output to the formal logs, and leaves the start action exited.
- [x] Confirm the generic manager and tracked example configuration remain unchanged.
- [x] Record the reusable Windows PowerShell native-stderr and inherited-pipe lesson.
