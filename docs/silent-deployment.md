# Silent deployment (Intune / SCCM)

## Two-stage pattern

1. **Elevated (SYSTEM/admin):** install product bits + Horizon Client  
2. **User context:** enroll + DPAPI bind

### Stage A — install

Publish the bundle, then (elevated):

```powershell
# Example after Publish-Bundle.ps1
.\WorkstationMfa.Cli.exe onboard `
  --profile ".\profiles\grupoalfarero.json" `
  --user PLACEHOLDER `
  --pi-user admin `
  --pi-pass-env PI_ADMIN_PASSWORD `
  --install-horizon `
  --what-if
```

Use a dedicated install script that only runs Horizon ensure if you split stages.

### Stage B — user-context onboard

```powershell
$env:PI_ADMIN_PASSWORD = "<admin password from secure vault>"
.\WorkstationMfa.Cli.exe onboard `
  --profile ".\profiles\grupoalfarero.json" `
  --user $env:USERNAME `
  --pi-user admin `
  --pi-pass-env PI_ADMIN_PASSWORD `
  --replace-token `
  --verify
```

## Exit codes

| Code | Meaning |
|---:|---|
| 0 | Success |
| 10 | Auth failed |
| 20 | Profile failed |
| 30 | Horizon failed |
| 40 | Enroll failed |
| 50 | Bind failed |
| 60 | Verify failed |
| 70 | Replace required (pass `--replace-token`) |

Do not pass admin passwords on the command line; use `--pi-pass-env` or `--pi-pass-stdin`.
