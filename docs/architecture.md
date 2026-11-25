# Architecture

WorkstationMfa is a .NET 8 Windows product with a shared `ManagementEngine` used by:

- **Console** — WPF management UI gated by privacyIDEA `POST /auth`
- **CLI** — silent/unattended host for Intune/SCCM
- **Client** — tray TOTP app with DPAPI-bound secret store

```text
Console/CLI → ManagementEngine → PrivacyIdeaClient
                              → HorizonInstaller
                              → LocalMfaProvisioner → TotpCredentialStore (DPAPI)
```

Connection profiles are non-secret and layered (bundled / local / external). Horizon Client media is bundled under `packages/horizon/` (gitignored binaries).
