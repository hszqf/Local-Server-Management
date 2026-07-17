# Process health checks should stay in process

- Windows Task Manager can group every process launched during the startup-impact window under the startup application. A large grouped count does not necessarily mean every listed process is still alive.
- A seven-second refresh that shells out for each process health check creates repeated PowerShell and Conhost pairs. In this case, a 22-second ancestry sample captured three pairs, which explains a startup-history count near 83 over several minutes.
- Periodic health checks should use in-process APIs. Keep PowerShell for explicit start and stop actions where scripting is part of the configured operation.
- Reading process command lines through `NtQueryInformationProcess(ProcessCommandLineInformation)` with `PROCESS_QUERY_LIMITED_INFORMATION` avoids both transient shells and this machine's normal-user CIM access failures.
- Validate lifecycle fixes with parent/child process data across several timer intervals. After this change, a 23-second sample across three refresh cycles found zero LocalServiceManager descendants.
