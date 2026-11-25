# Installer / bundle

v1 ships a **folder bundle** (self-contained publish) rather than a full WiX MSI.

Run:

```powershell
powershell -ExecutionPolicy Bypass -File installer/Publish-Bundle.ps1
```

Output lands in `artifacts/WorkstationMfa-Bundle/` with:

- Console, CLI, Client executables
- `profiles/grupoalfarero.json`
- `packages/horizon/` (copied if present locally)
- docs excerpts

Optional later: WiX Burn bootstrapper wrapping this folder.
