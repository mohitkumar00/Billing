# 📦 BillMate — Client Packaging & Onboarding Guide

## 1. Prerequisites on Build Machine
- **.NET 10 SDK** (`10.0.303+`)
- **.NET MAUI Workload** (install from an **Administrator** PowerShell):
  ```powershell
  dotnet workload install maui
  dotnet sln add src\BillMate.App\BillMate.App.csproj
  ```
- **Inno Setup 6** (`iscc.exe` on `PATH`) for building the `.exe` installer.
- *(Optional)* Place `postgresql-16-windows-x64.exe` inside `installer\BillMate.Setup\` to bundle PostgreSQL offline inside the `.exe`.

---

## 2. Building a White-Label Client Package

1. Copy `config\client_config.template.json` to `config\client_sharma.json` and customize `BusinessName`, `AppTitle`, `PrimaryColor`, `InvoicePrefix`, and `StateCode`.
2. Run the build pipeline:
   ```powershell
   .\scripts\build-client.ps1 -ConfigPath .\config\client_sharma.json
   ```
3. Outputs appear in `artifacts\<AppSlug>\`:
   - `<AppSlug>_Setup_v1.0.0.exe` — Windows Server + Desktop Installer
   - `android\*.apk` — Android Phone/Tablet sideload package

---

## 3. Shop Floor Installation (< 15 Minutes)
1. **Windows PC (Main Counter):** Run `<AppSlug>_Setup_v1.0.0.exe` as Administrator.
   - Installs PostgreSQL 16 (if bundled), generates a unique JWT signing key, registers the `BillMateApi` Windows Service (auto-restart on crash), and opens LAN port `5050` on the Private Wi-Fi firewall.
2. **Android Devices:** Copy the signed `.apk` onto the shop phone/tablet, install, and enter the Windows PC's local IP (e.g. `https://192.168.1.10:5050`).
3. **Google Drive Backup:** In **Settings → Backup**, sign in with the shop owner's Google account once so Hangfire uploads compressed `.dump.gz` snapshots every night at 11 PM IST.
