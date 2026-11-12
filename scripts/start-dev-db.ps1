<#
.SYNOPSIS
    Starts the local PostgreSQL development database on port 5432.
    Uses either native PostgreSQL 16 (in $env:LOCALAPPDATA\PostgreSQL) or Docker Compose if available.
#>
param(
    [switch]$Docker
)

$pgBin = "$env:LOCALAPPDATA\PostgreSQL\pgsql\bin"
$pgData = "$env:LOCALAPPDATA\PostgreSQL\data"

if ($Docker -or (-not (Test-Path $pgBin))) {
    Write-Host "==> Starting database using Docker Compose..." -ForegroundColor Cyan
    docker compose -f docker-compose.dev.yml up -d
    return
}

Write-Host "==> Starting native PostgreSQL server..." -ForegroundColor Cyan
& "$pgBin\pg_ctl.exe" -D $pgData -l "$env:LOCALAPPDATA\PostgreSQL\server.log" start
Write-Host "==> PostgreSQL is running on localhost:5432 (db: billmatedb, user: billmate)" -ForegroundColor Green
