# Native Process Log Redirection Experience 2026-07-19

## Failure Modes

- Windows PowerShell can turn a native process's normal stderr into `NativeCommandError` when `2>` is applied in a script running with `$ErrorActionPreference = "Stop"`.
- `Start-Process -RedirectStandardOutput/-RedirectStandardError` avoids that error, but a long-running descendant can still retain a manager's captured stream handles. A parent action may exit while the manager remains blocked in `ReadToEnd()`.

## Durable Pattern

- Keep machine paths and service definitions in ignored local configuration.
- Launch the long-running child through `Start-Process` without `RedirectStandard*`, and apply stdout/stderr file redirection inside the hidden child command so ShellExecute does not pass the manager's capture pipes into the service tree.
- Poll the real health endpoint for a bounded interval, return immediately on success, and report an early child exit with the formal log paths.
- Test both process exit and stream EOF while the service remains healthy; listener health alone does not prove the start action completed.

## Verification

- A cold start became healthy and returned from the action in about four seconds.
- The manager-style stdout/stderr readers reached EOF before the service was stopped.
- The formal stderr log contained the normal Uvicorn startup sequence, and port 8765 remained healthy after the action process exited.
