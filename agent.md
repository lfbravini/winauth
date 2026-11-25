\# Project Specification: Custom Hardened WinAuth Distro (`GrupoAlfarero-MFA`) \& Workstation Provisioner



\## 1. Global Architecture Context (Production Baseline)

You are building the client-side Windows MFA authenticator and automated provisioning pipeline for an existing, production-validated VMware/Omnissa Horizon VDI environment. Do not alter server-side assumptions; the desktop client must integrate seamlessly with this live stack:



\- \*\*VDI Perimeter:\*\* Omnissa / VMware Horizon (9 Connection Servers; pilot server `ALP` at `192.168.253.236`) configured for 2-Factor RADIUS authentication (`PAP`, UDP/1812, `Enforce 2-factor and Windows user name matching = Enabled`, `Use same user name and password = Disabled`).

\- \*\*RADIUS Front-End (`pi-freeradius`):\*\* Dockerized FreeRADIUS using `rlm\_perl` forwarding authentication requests to `http://privacyidea:8080/validate/check` with `REALM` left blank.

\- \*\*MFA Engine (`pi-server` + `pi-db`):\*\* Dockerized privacyIDEA backed by MariaDB (`https://mfa.corptronics.com`).

&#x20; - \*\*Identity \& Realm Routing:\*\* Active Directory `GRUPOALFARERO.NET` (`OU=ALFARERIA HISPANO VENEZOLANA,DC=GRUPOALFARERO,DC=NET`) resolved via `sAMAccountName` (`objectGUID` UID, referral chasing disabled). Incoming UPNs (`user@grupoalfarero.net`) are split via `splitAtSign` into realm `grupoalfarero.net` (with future multi-domain support for `corptronics.com`).

&#x20; - \*\*Active privacyIDEA Policies:\*\*

&#x20;   - `GrupoAlfarero-Horizon-OTP` (`authentication`): `otppin = none`, `passthru = unchecked` (validates strictly the 6-digit SHA1/30s TOTP code on Horizon Prompt 1).

&#x20;   - `QR-Account-Label` (`enrollment`): `tokenlabel = <u>@<r>`.

&#x20;   - `Block-User-Portal-Login` (`webui`): `login\_mode = privacyIDEA` (blocks end-users from self-enrolling via web UI).

\- \*\*End-to-End Login Flow:\*\*

&#x20; 1. \*\*Horizon Prompt 1:\*\* User enters `username@grupoalfarero.net` + 6-digit TOTP code generated locally by this custom WinAuth client.

&#x20; 2. \*\*Horizon Prompt 2:\*\* Horizon locks the username from Prompt 1; user enters their Windows AD password to launch published apps/desktops.



\## 2. Strategic Objective: Workstation-Bound TOTP ("Something You Have" = Approved PC)

\- Instead of mobile phones, each user's TOTP secret must sit \*\*exclusively on their assigned Windows workstation\*\* (supporting both domain-joined machines and standalone/workgroup PCs where the user may hold local Administrator rights).

\- \*\*User-Centric Provisioning Model:\*\* During workstation onboarding, an administrator runs a PowerShell script that authenticates to the privacyIDEA REST API, lists Active Directory users (`GET /user/?realm=grupoalfarero.net`), deletes any prior token assigned to the selected user, mints a brand-new TOTP token on the spot (`POST /token/init`), passes the Base32 `otpkey` strictly in RAM to our custom WinAuth binary via a silent CLI flag, and scrubs memory. No QR code or Base32 secret is ever displayed on screen or written in plaintext to disk.



\## 3. Codebase Target \& Hardening Requirements (`winauth` C# / .NET Fork)

Base repository: `https://github.com/winauth/winauth` (C# Windows Forms targeting `.NET Framework 4.6.2` / `4.8`, single portable executable output).



\### A. Security Hardening (Mandatory Removals \& Locks)

1\. \*\*Strip Secret Viewing \& Export UI (`WinAuthForm.cs` / `WinAuthForm.Designer.cs`):\*\*

&#x20;  - Completely remove/disable the `Show Secret Key` context menu action so users cannot view the Base32 secret or `otpauth://` URI.

&#x20;  - Completely remove/disable the `Export` menu action (`UriKeyFormat`, zip/text exports).

&#x20;  - Completely remove/disable the `Change Protection` dialog and main window `Add` / `Import` buttons during normal execution so users cannot downgrade encryption or add personal tokens.

2\. \*\*Disable Registry \& External Backups (`WinAuthConfig.cs`):\*\*

&#x20;  - Locate and disable automatic backup writes to the Windows Registry (`HKCU\\Software\\WinAuth3\\Backup`) or secondary backup files so the secret exists solely inside the encrypted `winauth.xml` file.

3\. \*\*Enforce Mandatory Windows DPAPI Machine + User Binding (`WinAuthConfig.cs`):\*\*

&#x20;  - Hardcode `PasswordTypes.Machine | PasswordTypes.User` (`ProtectedData.Protect` with `DataProtectionScope.CurrentUser` combined with WinAuth's machine-specific entropy) on every config save. Copying `winauth.xml` to another PC must fail decryption.



\### B. Touchless CLI Provisioning Interface (`Program.cs`)

Implement a non-interactive CLI argument handler before the GUI message loop starts:

\- \*\*Syntax:\*\* `GrupoAlfarero-MFA.exe --silent-provision "<upn>" "<base32\_secret>"`

\- \*\*Behavior:\*\*

&#x20; 1. Validates arguments (`upn` e.g. `ccamarinha@grupoalfarero.net`, valid Base32 `secret`).

&#x20; 2. Initializes or overwrites the local `%APPDATA%\\WinAuth\\winauth.xml` configuration with a single RFC 6238 `GoogleAuthenticator` (TOTP, 6 digits, SHA1, 30-second step) entry named `Horizon VDI (<upn>)`.

&#x20; 3. Enables `AutoRefresh = true` on the authenticator entry and configures default application preferences (`StartWithWindows = true`, `MinimizeToTray = true`).

&#x20; 4. Saves the config encrypted with DPAPI (`Machine | User`) and exits with code `0` (or non-zero with stderr message on failure) without displaying any UI or secret values.



\### C. End-User Experience Requirements (Keep \& Polish)

1\. \*\*System Tray First:\*\* Starts minimized to the Windows notification area (system tray) on Windows logon.

2\. \*\*1-Click Copy \& Auto-Refresh:\*\* Left-clicking or right-clicking the tray icon allows instant copying of the live 6-digit TOTP code to clipboard; opening the compact window displays the continuously refreshing 6-digit code with countdown indicator.

3\. \*\*Horizon Global Hotkey (`Ctrl + Alt + T`):\*\* Pre-configure (or expose a simple hotkey toggle) that types `{CODE}{ENTER}` into the active Horizon Client Prompt 1 field.

4\. \*\*Corporate Branding:\*\* Update assembly metadata, window title (`GrupoAlfarero MFA`), and executable output name to `GrupoAlfarero-MFA.exe`.



\## 4. Companion PowerShell Provisioner (`scripts/Provision-WorkstationMFA.ps1`)

Maintain alongside the C# solution a clean PowerShell script that:

1\. Prompts for privacyIDEA admin credentials (`SecureString`) and obtains a JWT via `POST /auth`.

2\. Fetches users via `GET /user/?realm=grupoalfarero.net` and cross-references `GET /token/?realm=grupoalfarero.net` to display `Provisioned` vs. `Unprovisioned` status in `Out-GridView` (with console fallback).

3\. If the chosen user already has an assigned token, prompts for confirmation and deletes it via `DELETE /token/<serial>`.

4\. Calls `POST /token/init` (`type=totp`, `otplen=6`, `hashlib=sha1`, `timestep=30`) to generate a new token and captures `result.value.otpkey.value` in memory.

5\. Invokes `GrupoAlfarero-MFA.exe --silent-provision "<user>@grupoalfarero.net" "<seed>"` in the current user's Windows context, then nulls out `$Base32Seed` and `$JwtToken` and triggers `\[System.GC]::Collect()`.



\## 5. Cursor Agent Coding Guidelines

\- Always inspect existing `WinAuth` classes (`Program.cs`, `WinAuthForm.cs`, `WinAuthConfig.cs`, `WinAuthAuthenticator.cs`, `GoogleAuthenticator.cs`) before modifying code; preserve WinAuth's battle-tested RFC 6238 TOTP and DPAPI cryptography classes rather than rewriting crypto from scratch.

\- Ensure the solution builds cleanly via `msbuild` / Visual Studio in `Release` configuration as a self-contained `.exe` (merge or embed `Newtonsoft.Json` / dependencies if required so single-file portability is preserved).

\- Never log, print, or write Base32 seeds or `otpauth://` URIs to console output, debug logs, or temporary files.

