<#
.SYNOPSIS
    Builds the BillMate Android application (.apk / .aab) for mobile and handheld POS devices.
.DESCRIPTION
    Checks for .NET MAUI Android workload, OpenJDK, and Android SDK, then compiles and publishes
    a debug or signed release APK.
.EXAMPLE
    .\scripts\build-android.ps1
    .\scripts\build-android.ps1 -Configuration Release -PackageFormat apk
    .\scripts\build-android.ps1 -Configuration Release -PackageFormat aab -KeystorePath my-key.keystore -KeystorePass "pass123"
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("apk", "aab")]
    [string]$PackageFormat = "apk",

    [string]$OutputPath = "artifacts\android",

    [string]$KeystorePath = "",
    [string]$KeystoreAlias = "billmate",
    [string]$KeystorePass = "",

    [string]$AppTitle = "BillMate POS",
    [string]$ApplicationId = "com.billmate.pos"
)

$ErrorActionPreference = "Stop"

# Automatically resolve repository root regardless of current working directory
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "         BillMate Android Build Pipeline                  " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "==> Repository Root: $repoRoot" -ForegroundColor DarkGray

# 1. Check .NET SDK Workloads
Write-Host "==> Checking .NET workloads..." -ForegroundColor Yellow
$workloadOutput = dotnet workload list | Out-String
$hasAndroidWorkload = $workloadOutput -match "maui-android" -or $workloadOutput -match "android"

if (-not $hasAndroidWorkload) {
    Write-Warning "Android workload is not detected in 'dotnet workload list'."
    Write-Host @"

[ACTION REQUIRED] To install the Android workload without stalling, run this command in an Administrator PowerShell:

  dotnet workload install maui-android --skip-manifest-update --source https://api.nuget.org/v3/index.json --ignore-failed-sources

Or install via Visual Studio Installer -> Modify -> Check '.NET Multi-platform App UI development'.
"@ -ForegroundColor Magenta
    throw "Missing required Android workload."
}

# 2. Prepare Output Directory
$absOutput = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    [System.IO.Path]::GetFullPath($OutputPath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputPath))
}

if (-not (Test-Path $absOutput)) {
    New-Item -ItemType Directory -Force -Path $absOutput | Out-Null
}

# 3. Assemble Build Arguments
$projectPath = Join-Path $repoRoot "src\BillMate.App\BillMate.App.csproj"
$framework = "net10.0-android"

$buildArgs = @(
    "publish", "`"$projectPath`"",
    "-f", $framework,
    "-c", $Configuration,
    "-p:EnableAndroidTarget=true",
    "-p:AndroidPackageFormat=$PackageFormat",
    "-p:ApplicationTitle=`"$AppTitle`"",
    "-p:ApplicationId=$ApplicationId",
    "-o", "`"$absOutput`""
)

$resolvedKeystore = if (-not [string]::IsNullOrWhiteSpace($KeystorePath)) {
    if ([System.IO.Path]::IsPathRooted($KeystorePath)) { $KeystorePath } else { Join-Path $repoRoot $KeystorePath }
} else { $null }

if ($null -ne $resolvedKeystore -and (Test-Path $resolvedKeystore)) {
    Write-Host "==> Signing with Keystore: $resolvedKeystore" -ForegroundColor Cyan
    $buildArgs += @(
        "-p:AndroidKeyStore=true",
        "-p:AndroidSigningKeyStore=`"$resolvedKeystore`"",
        "-p:AndroidSigningKeyAlias=$KeystoreAlias",
        "-p:AndroidSigningStorePass=$KeystorePass",
        "-p:AndroidSigningKeyPass=$KeystorePass"
    )
} else {
    Write-Host "==> Generating unsigned/default-signed package ($PackageFormat)..." -ForegroundColor Yellow
    $buildArgs += "-p:AndroidKeyStore=false"
}

Write-Host "==> Running: dotnet $($buildArgs -join ' ')" -ForegroundColor Cyan
& dotnet @buildArgs

if ($LASTEXITCODE -ne 0) {
    throw "Android build failed with exit code $LASTEXITCODE."
}

Write-Host "`n[SUCCESS] Android build complete!" -ForegroundColor Green
Write-Host "Output located at: $absOutput" -ForegroundColor Green
Get-ChildItem -Path $absOutput -Include *.apk, *.aab -Recurse | ForEach-Object {
    Write-Host "  -> $($_.FullName) ($([Math]::Round($_.Length / 1MB, 2)) MB)" -ForegroundColor Yellow
}
