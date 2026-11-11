; BillMate White-Label Self-Hosted Installer (Inno Setup 6)
; Bundles: ASP.NET Core 9/10 Windows Service + MAUI Desktop + Optional Silent PostgreSQL 16 + Firewall Rule

#ifndef AppName
  #define AppName "BillMate Billing"
#endif
#ifndef AppSlug
  #define AppSlug "BillMate"
#endif
#ifndef ApiStageDir
  #define ApiStageDir "..\..\artifacts\BillMate_Billing\api"
#endif
#ifndef DesktopStageDir
  #define DesktopStageDir "..\..\artifacts\BillMate_Billing\desktop"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\artifacts\BillMate_Billing"
#endif

[Setup]
AppId={{8F2A9B1E-4C7D-4A3F-9D5E-1B2C3D4E5F6A}
AppName={#AppName}
AppVersion=1.0.0
AppPublisher=BillMate Systems
DefaultDirName={autopf}\{#AppSlug}
DefaultGroupName={#AppName}
PrivilegesRequired=admin
OutputDir={#OutputDir}
OutputBaseFilename={#AppSlug}_Setup_v1.0.0
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "{#ApiStageDir}\*"; DestDir: "{app}\Api"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#DesktopStageDir}\*"; DestDir: "{app}\Desktop"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
; Place postgresql-16-windows-x64.exe next to this .iss if bundling PostgreSQL offline
Source: "postgresql-16-windows-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist

[Dirs]
Name: "{commonappdata}\BillMate\Logs"
Name: "{commonappdata}\BillMate\Backups"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\Desktop\BillMate.App.exe"; Check: FileExists(ExpandConstant('{app}\Desktop\BillMate.App.exe'))
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Desktop\BillMate.App.exe"; Check: FileExists(ExpandConstant('{app}\Desktop\BillMate.App.exe'))

[Run]
; 1. Silent PostgreSQL 16 install if bundled and not already running
Filename: "{tmp}\postgresql-16-windows-x64.exe"; Parameters: "--mode unattended --unattendedmodeui minimal --superpassword ""billmate_pg_admin"" --serviceaccount ""NT AUTHORITY\NetworkService"" --serverport 5432"; StatusMsg: "Installing PostgreSQL 16 database engine..."; Flags: waituntilterminated; Check: FileExists(ExpandConstant('{tmp}\postgresql-16-windows-x64.exe'))

; 2. Generate a strong per-client JWT key in appsettings.Production.json
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""$bytes = New-Object byte[] 48; [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes); $key = [Convert]::ToBase64String($bytes); $cfg = @{{ Jwt = @{{ SigningKey = $key }} }} | ConvertTo-Json; Set-Content -Path '{app}\Api\appsettings.Production.json' -Value $cfg -Encoding UTF8"""; Flags: runhidden waituntilterminated

; 3. Register and start BillMateApi Windows Service with automatic crash recovery (< 2 min NFR)
Filename: "sc.exe"; Parameters: "stop BillMateApi"; Flags: runhidden waituntilterminated
Filename: "sc.exe"; Parameters: "delete BillMateApi"; Flags: runhidden waituntilterminated
Filename: "sc.exe"; Parameters: "create BillMateApi binPath= ""\""{app}\Api\BillMate.Api.exe\"" --urls https://0.0.0.0:5050"" start= auto DisplayName= ""{#AppName} Server"""; Flags: runhidden waituntilterminated
Filename: "sc.exe"; Parameters: "failure BillMateApi reset= 86400 actions= restart/5000/restart/15000/restart/30000"; Flags: runhidden waituntilterminated
Filename: "sc.exe"; Parameters: "start BillMateApi"; StatusMsg: "Starting {#AppName} background service..."; Flags: runhidden waituntilterminated

; 4. Open Private/LAN Firewall port 5050 for Android tablets/phones
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""BillMate LAN API"""; Flags: runhidden waituntilterminated
Filename: "netsh.exe"; Parameters: "advfirewall firewall add rule name=""BillMate LAN API"" dir=in action=allow protocol=TCP localport=5050 profile=private"; Flags: runhidden waituntilterminated

[UninstallRun]
Filename: "sc.exe"; Parameters: "stop BillMateApi"; Flags: runhidden waituntilterminated
Filename: "sc.exe"; Parameters: "delete BillMateApi"; Flags: runhidden waituntilterminated
Filename: "netsh.exe"; Parameters: "advfirewall firewall delete rule name=""BillMate LAN API"""; Flags: runhidden waituntilterminated
