; Inno Setup Script for BillMate POS & Counter Billing System
; Compiles a single-file setup installer: BillMate-Setup-v1.0.exe

#define MyAppName "BillMate POS"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "BillMate Solutions"
#define MyAppURL "https://billmate.local"
#define MyAppExeName "BillMate.App.exe"

[Setup]
AppId={{E68A8E67-0F2C-4A2D-A491-953B18FDF3F1}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\BillMate
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\setup
OutputBaseFilename=BillMate-Setup-v1.0
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Copy all dist files (Desktop App + API Sidecar + DB Scripts)
Source: "..\dist\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Initialize database and register background service silently
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\db\init-prod-db.ps1"" -InstallService"; Flags: runhidden waituntilterminated; StatusMsg: "Configuring local database and services..."

; Option to launch app immediately after installation
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop and cleanup API if running
Filename: "taskkill.exe"; Parameters: "/F /IM BillMate.App.exe /IM BillMate.Api.exe"; Flags: runhidden; RunOnceId: "StopProcesses"
