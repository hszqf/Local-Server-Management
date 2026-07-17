# Local Server Management

Windows tray app for starting, stopping, and checking local development services from one small panel.

The app is config-driven. Service definitions, tabs, links, health checks, and start/stop commands live in JSON instead of C# code.

## Quick Start

1. Clone the repo.
2. Copy the example config:

```powershell
Copy-Item .\config.example.json .\config.local.json
```

3. Edit `config.local.json` for your machine:

- `PROJECT_ROOT`: local project path used by the example commands.
- `LOG_ROOT`: log directory, or keep `{{LogRoot}}` to use the app default.
- Add your own variables for private paths, tokens, and service-specific settings.
- Update service `start`, `stop`, and `health` fields as needed.

4. Build:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\publish.ps1
```

5. Run:

```powershell
.\dist\LocalServiceManager.exe
```

## Config Files

- `config.example.json` is committed and documents the expected shape.
- `config.local.json` is ignored by Git and is where machine-specific paths and private service commands belong.

Do not commit `config.local.json`.

## Included Example Services

The committed example config is intentionally generic. It includes only placeholder services:

- `示例 Web 服务`: demonstrates an HTTP health check and process start action.
- `示例进程服务`: demonstrates a process health check and PowerShell start/stop actions.

Machine-specific services, domains, private paths, and token file locations belong only in ignored `config.local.json`.

## Service Schema

Each service supports:

- `id`: stable service id used by auto-start registry values.
- `name`: display name.
- `endpoint`: display URL.
- `tags`: tab filters, such as `example`, `api`, or `frontend`.
- `health`: `http` or `process` health check.
- `health.tlsCaFile`: optional CA certificate path for one HTTPS health check; the certificate chain and URL host name are both validated without changing the Windows trust store.
- `start`: `process` or `powershell` start action.
- `stop`: `process` or `powershell` stop action.
- `detached`: for `process` actions, set this to `true` when the command launches a long-running server and should return control to the manager immediately.

Supported placeholders:

- `{{AppRoot}}`: folder containing the config files.
- `{{AppDir}}`: executable folder.
- `{{LogRoot}}`: default log root.
- `{{YOUR_VARIABLE}}`: any key from `variables` in the config.

## Startup

The app can register itself for current-user startup through:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run\LocalServiceManager
```

Per-service auto-start is stored separately under:

```text
HKCU\Software\LocalServiceManager\ServiceAutoStart
```

When the app opens, checked services that are not currently running are started automatically.

## Build Output

```text
dist\LocalServiceManager.exe
```

The repository currently tracks the built exe so a simple pull can update the app, but local configuration remains ignored.


