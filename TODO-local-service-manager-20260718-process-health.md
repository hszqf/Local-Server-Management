# Local service manager process health cleanup

- [x] Capture the live LocalServiceManager descendant process tree.
- [x] Confirm the seven-second process health refresh creates temporary PowerShell and Conhost pairs.
- [x] Replace PowerShell-based process health checks with an in-process implementation.
- [x] Build the published manager successfully.
- [x] Verify process health checks no longer spawn temporary PowerShell processes.
- [x] Record the reusable process-lifecycle lesson.
- [x] Commit and push the scoped change.
