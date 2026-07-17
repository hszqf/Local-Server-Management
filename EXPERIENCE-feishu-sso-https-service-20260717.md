# Feishu SSO HTTPS service management

## Findings

- A stale `pythonw.exe` instance can keep a port occupied even when a service definition only searches for `python.exe`.
- An HTTP health failure does not prove that the managed process is absent. Confirm the listener PID and command line before starting another detached instance.
- A local CA should be scoped to the one HTTPS health check that needs it. Validate both the URL host name and a certificate chain ending at that configured CA; do not disable certificate validation globally.
- Applications compiled with the .NET Framework 4 compiler may still negotiate legacy TLS when run as a standalone executable. Explicitly select TLS 1.2 when the managed server requires TLS 1.2 or newer.
- Manual actions must not be silently discarded while the periodic auto-start scan is busy. Serialize manual actions and let periodic scans skip a busy cycle.

## Verification

1. Confirm the obsolete process exits and the intended port is free.
2. Start the service with the complete configured argument list.
3. Confirm the listener address, PID, executable name, and command line.
4. Call the health endpoint through the configured CA and verify its required response text.
5. Run the published manager and confirm the row reports `HTTP 200` and `运行中`.
