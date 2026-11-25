# WorkstationMfa

PrivacyIDEA-authenticated Windows management console for workstation-bound TOTP MFA and Omnissa/VMware Horizon Client lifecycle.

## Components

| Binary | Role |
|---|---|
| `WorkstationMfa.Console.exe` | Login-gated management UI |
| `WorkstationMfa.Cli.exe` | Silent / Intune-friendly host |
| `WorkstationMfa.Client.exe` | End-user tray TOTP client (DPAPI-bound) |

## Quick start (dev)

```powershell
dotnet build src/WorkstationMfa.sln -c Release
dotnet test src/WorkstationMfa.sln -c Release
```

Run console:

```powershell
dotnet run --project src/WorkstationMfa.Console -c Release
```

Silent onboard example:

```powershell
$env:PI_ADMIN_PASSWORD = "****"
dotnet run --project src/WorkstationMfa.Cli -c Release -- onboard `
  --profile "packages/profiles/grupoalfarero.json" `
  --user someuser `
  --pi-user admin `
  --pi-pass-env PI_ADMIN_PASSWORD `
  --replace-token `
  --verify
```

## Connection profiles

Non-secret JSON describing a privacyIDEA server, realm, and optional Horizon broker/media path.  
Resolution order: `--profile` / `--profile-url` → `ProgramData\WorkstationMfa\profile.source` → local profiles → bundled `packages/profiles`.

Sample: [packages/profiles/grupoalfarero.json](packages/profiles/grupoalfarero.json)

## Token policy

One managed workstation TOTP per user. Re-enrollment **replaces** (destroys) the previous token and forces local re-bind. CLI requires `--replace-token` when a token already exists.

## Packaging

See [installer/README.md](installer/README.md) and `installer/Publish-Bundle.ps1`.

## Docs

- [docs/architecture.md](docs/architecture.md)
- [docs/admin-runbook.md](docs/admin-runbook.md)
- [docs/silent-deployment.md](docs/silent-deployment.md)
- [docs/hardening-checklist.md](docs/hardening-checklist.md)

Historical requirements: [agent.md](agent.md)
