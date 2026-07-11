# Local Service Manager Experience 2026-07-12 Network Recovery

- The Codex watchdog service is a machine-local tool at `D:\aiusetool\codex_watchdog`, wired through ignored `config.local.json`; do not commit that path or service definition to the public example config.
- Windows can require administrator rights even for reading `Get-NetRoute` and `Get-NetAdapter` in this environment. A non-elevated watchdog should request a one-shot elevated helper before adapter enumeration.
- Network adapter reset is disruptive, so trigger it only after sustained probe failure and apply a cooldown between attempts.
