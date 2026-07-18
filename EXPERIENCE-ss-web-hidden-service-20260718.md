# Hidden Vite Service Experience 2026-07-18

## Symptom

- Starting start-ss-web.bat from Explorer leaves a visible CMD process chain for the long-running Vite server.
- A health-only start action can mistake that manual process for a manager-owned service and leave the foreground window in place.

## Durable Pattern

- Keep the machine path and full service actions in ignored config.local.json.
- Start cmd.exe through ProcessStartInfo with UseShellExecute=false and CreateNoWindow=true, redirect stdout/stderr to {{LOG_ROOT}}, and require the exact HTTP title before reporting healthy.
- Stop from the port listener upward. Normalize slash direction and accept only the exact project Vite, npm run dev, and parent CMD command shapes; stop walking at the first unrelated ancestor.
- Never kill every node.exe, cmd.exe, or process matching a broad project-name substring.

## Verification

- Port 5173 returned HTTP 200 with the SS Web title.
- The managed process chain ended at a hidden CMD launcher rather than an Explorer-launched BAT.
- The stderr log remained empty, and stop removed the complete prior BAT process chain before the hidden restart.
