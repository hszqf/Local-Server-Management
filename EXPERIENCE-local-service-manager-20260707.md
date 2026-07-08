# Experience: local service manager

- Keep the local service manager outside individual project repos when it manages more than one project.
- Startup registration is application-level; per-service auto-start is service-level and must be stored separately.
- If a service table has row controls, put those controls inside the same table to keep row height, scrolling, and hit testing aligned.
- Tags are the extension point for future projects: keep the first tab as `全部` or `All`, then add project or capability tabs from service tags.
- Keep service display names user-facing. Implementation details can stay in config commands, logs, or health checks.
- Public repos should commit `config.example.json` only. Machine-specific paths, domains, token locations, and service commands belong in ignored `config.local.json`.
- Service tabs, health checks, endpoints, and start/stop actions should be config data. Adding a new service should not require C# source edits.
- Auto-start checks must distinguish not-started from unhealthy: only run start actions for `未运行` service statuses, and classify HTTP responses with bad status/content as `异常` so live services are not touched.
