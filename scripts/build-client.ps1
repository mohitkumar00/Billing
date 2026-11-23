<#
.SYNOPSIS
    Builds a white-labeled BillMate distribution (Windows API + MAUI Desktop + Inno Setup .exe + Android .apk).
.EXAMPLE
    .\scripts\build-client.ps1 -ConfigPath .\config\client_config.template.json
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ConfigPath,

    [string]$OutputRoot = "artifacts",
    [switch]$SkipAndroid,
    [switch]$SkipDesktop,
    [switch]$SkipInstaller,
    [string]$AndroidKeystorePath = "",
    [string]$AndroidKeystoreAlias = "billmate",
    [string]$AndroidKeystorePass = ""
)

$ErrorActionPreference = "Stop"

# Automatically resolve repository root regardless of current working directory
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path

# Resolve ConfigPath
$resolvedConfigPath = if (Test-Path $ConfigPath) {
    (Resolve-Path $ConfigPath).Path
} elseif (Test-Path (Join-Path $repoRoot $ConfigPath)) {
    (Resolve-Path (Join-Path $repoRoot $ConfigPath)).Path
} else {
    throw "White-label config not found: $ConfigPath"
}

$cfg = Get-Content $resolvedConfigPath -Raw | ConvertFrom-Json
foreach ($required in @("BusinessName", "AppTitle", "InvoicePrefix", "StateCode")) {
    if ([string]::IsNullOrWhiteSpace($cfg.$required)) {
        throw "Missing required field '$required' in $resolvedConfigPath"
    }
}

$slug = ($cfg.AppTitle -replace '[^a-zA-Z0-9]+', '_').Trim('_')
$baseOut = if ([System.IO.Path]::IsPathRooted($OutputRoot)) { $OutputRoot } else { Join-Path $repoRoot $OutputRoot }
$stageDir = Join-Path $baseOut $slug
$apiOut = Join-Path $stageDir "api"
$winOut = Join-Path $stageDir "desktop"
$apkOut = Join-Path $stageDir "android"

New-Item -ItemType Directory -Force -Path $apiOut, $winOut, $apkOut | Out-Null
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "==> Building white-label package for '$($cfg.BusinessName)' ($slug)" -ForegroundColor Cyan
Write-Host "==> Repository Root: $repoRoot" -ForegroundColor DarkGray
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Publish ASP.NET Core API (Self-contained win-x64 Windows Service)
Write-Host "==> Publishing BillMate.Api (win-x64)..." -ForegroundColor Yellow
$apiProj = Join-Path $repoRoot "src\BillMate.Api\BillMate.Api.csproj"
dotnet publish $apiProj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -o $apiOut
if ($LASTEXITCODE -ne 0) { throw "BillMate.Api publish failed." }

Copy-Item $resolvedConfigPath (Join-Path $apiOut "client_config.json") -Force

# 2. Check MAUI workload before publishing Desktop/Android
$workloads = dotnet workload list | Out-String
$hasMaui = $workloads -match "maui"

$appProj = Join-Path $repoRoot "src\BillMate.App\BillMate.App.csproj"

if (-not $SkipDesktop) {
    if (-not $hasMaui) {
        Write-Warning "Skipping MAUI Windows Desktop publish: .NET MAUI workload is not installed. Run 'dotnet workload install maui' from an elevated shell."
    }
    else {
        Write-Host "==> Publishing BillMate.App (Windows Desktop)..." -ForegroundColor Yellow
        dotnet publish $appProj `
            -f net10.0-windows10.0.19041.0 -c Release `
            -p:ApplicationTitle="$($cfg.AppTitle)" `
            -p:WindowsPackageType=None `
            -o $winOut
        if ($LASTEXITCODE -ne 0) { throw "BillMate.App Windows publish failed." }
        Copy-Item $resolvedConfigPath (Join-Path $winOut "client_config.json") -Force
    }
}

if (-not $SkipAndroid) {
    if (-not $hasMaui) {
        Write-Warning "Skipping Android .apk publish: .NET MAUI workload is not installed."
    }
    else {
        Write-Host "==> Publishing BillMate.App (Android APK)..." -ForegroundColor Yellow
        $androidArgs = @(
            "publish", $appProj,
            "-f", "net10.0-android", "-c", "Release",
            "-p:EnableAndroidTarget=true",
            "-p:AndroidPackageFormat=apk",
            "-p:ApplicationTitle=$($cfg.AppTitle)",
            "-o", $apkOut
        )
        $resolvedKeystore = if (-not [string]::IsNullOrWhiteSpace($AndroidKeystorePath)) {
            if ([System.IO.Path]::IsPathRooted($AndroidKeystorePath)) { $AndroidKeystorePath } else { Join-Path $repoRoot $AndroidKeystorePath }
        } else { $null }

        if ($null -ne $resolvedKeystore -and (Test-Path $resolvedKeystore)) {
            $androidArgs += @(
                "-p:AndroidKeyStore=true",
                "-p:AndroidSigningKeyStore=$resolvedKeystore",
                "-p:AndroidSigningKeyAlias=$AndroidKeystoreAlias",
                "-p:AndroidSigningStorePass=$AndroidKeystorePass",
                "-p:AndroidSigningKeyPass=$AndroidKeystorePass"
            )
        }
        else {
            $androidArgs += "-p:AndroidKeyStore=false"
        }
        & dotnet @androidArgs
        if ($LASTEXITCODE -ne 0) { throw "BillMate.App Android publish failed." }
    }
}

# 3. Compile Inno Setup Installer if iscc.exe is available
if (-not $SkipInstaller) {
    $isccCmd = Get-Command iscc -ErrorAction SilentlyContinue
    $isccPath = if ($null -ne $isccCmd) { $isccCmd.Source } else {
        @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
            Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if ([string]::IsNullOrWhiteSpace($isccPath)) {
        Write-Warning "Inno Setup compiler (iscc.exe) not found. Skipping .exe installer compilation."
    }
    else {
        Write-Host "==> Compiling Inno Setup installer using $isccPath..." -ForegroundColor Yellow
        $issFile = Join-Path $repoRoot "installer\BillMate.Setup\BillMate.iss"
        & $isccPath `
            "/DAppName=$($cfg.AppTitle)" `
            "/DAppSlug=$slug" `
            "/DApiStageDir=$apiOut" `
            "/DDesktopStageDir=$winOut" `
            "/DOutputDir=$stageDir" `
            $issFile
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed." }
    }
}

Write-Host "==> Staging complete: $stageDir" -ForegroundColor Green
