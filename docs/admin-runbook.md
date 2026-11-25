# Admin runbook

## Interactive provision

1. Install the WorkstationMfa bundle (or run from a published output folder that includes `profiles/` and `packages/horizon/`).
2. Launch **Workstation MFA Console**.
3. Confirm the connection profile (bundled Grupo Alfarero or your own).
4. Sign in with a privacyIDEA **admin** account (domain users work when PI grants them admin).
5. Optionally click **Install Horizon**.
6. Select a user → **Provision / Replace**.
7. If a token already exists, confirm destruction of the previous token.
8. Verify the tray client shows a live code; test Horizon Prompt 1 (UPN + OTP) then Prompt 2 (AD password).

## Important

- Run provision while logged on as the **end user** so DPAPI binds correctly.
- Never share screenshots of Base32 seeds (the product never displays them).
- Replace on a new PC intentionally invalidates the old workstation secret.
