<#
.SYNOPSIS
    Initializes PostgreSQL database and runs initial schema & seed for BillMate.
#>

param(
    [string]$PgBinPath = "$env:LOCALAPPDATA\PostgreSQL\pgsql\bin",
    [string]$DataDir = "$env:LOCALAPPDATA\PostgreSQL\data",
    [string]$SuperUser = "billmate",
    [string]$SuperPassword = "billmate_dev_password",
    [string]$Database = "billmatedb",
    [int]$Port = 5432,
    [switch]$InstallService
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path "$PgBinPath\postgres.exe")) {
    $fallbackPaths = @(
        "C:\Program Files\PostgreSQL\16\bin",
        "C:\Program Files\PostgreSQL\15\bin",
        "$env:LOCALAPPDATA\PostgreSQL\pgsql\bin"
    )
    foreach ($p in $fallbackPaths) {
        if (Test-Path "$p\postgres.exe") {
            $PgBinPath = $p
            break
        }
    }
}

if (-not (Test-Path "$PgBinPath\postgres.exe")) {
    throw "PostgreSQL binaries not found. Please specify -PgBinPath."
}

Write-Host "Using PostgreSQL binaries at: $PgBinPath" -ForegroundColor Cyan

# 1. Initialize cluster if needed
if (-not (Test-Path "$DataDir\PG_VERSION")) {
    Write-Host "Initializing new PostgreSQL cluster at $DataDir..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $DataDir -Force | Out-Null
    
    $pwFile = [System.IO.Path]::GetTempFileName()
    Set-Content -Path $pwFile -Value $SuperPassword -NoNewline
    
    try {
        & "$PgBinPath\initdb.exe" -D $DataDir -U $SuperUser -A scram-sha-256 --pwfile=$pwFile --encoding=UTF8
    } finally {
        Remove-Item $pwFile -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "PostgreSQL cluster already initialized at $DataDir" -ForegroundColor Green
}

# 2. Register Windows Service if requested and elevated
if ($InstallService) {
    Write-Host "Registering PostgreSQL Windows Service 'BillMatePostgreSQL'..." -ForegroundColor Yellow
    & "$PgBinPath\pg_ctl.exe" register -N "BillMatePostgreSQL" -D $DataDir -S auto
    Start-Service -Name "BillMatePostgreSQL" -ErrorAction SilentlyContinue
} else {
    # Check if postgres is running on port
    $conn = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue
    if (-not $conn) {
        Write-Host "Starting PostgreSQL background daemon..." -ForegroundColor Yellow
        & "$PgBinPath\pg_ctl.exe" start -D $DataDir -w
    }
}

# 3. Create database if it does not exist
$env:PGPASSWORD = $SuperPassword
$dbExists = & "$PgBinPath\psql.exe" -U $SuperUser -p $Port -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$Database';"
if ($dbExists.Trim() -ne "1") {
    Write-Host "Creating database '$Database'..." -ForegroundColor Yellow
    & "$PgBinPath\psql.exe" -U $SuperUser -p $Port -d postgres -c "CREATE DATABASE $Database OWNER $SuperUser;"
} else {
    Write-Host "Database '$Database' already exists." -ForegroundColor Green
}

# 4. Run demo seed script if present
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$seedSql = Join-Path $scriptDir "seed-demo-data.sql"
if (Test-Path $seedSql) {
    Write-Host "Applying seed data from $seedSql..." -ForegroundColor Yellow
    & "$PgBinPath\psql.exe" -U $SuperUser -p $Port -d $Database -f $seedSql
}

Write-Host "Database setup completed successfully." -ForegroundColor Green
