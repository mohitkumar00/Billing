<#
.SYNOPSIS
    Stops the local PostgreSQL development database.
#>
param(
    [switch]$Docker
)

$pgBin = "$env:LOCALAPPDATA\PostgreSQL\pgsql\bin"
$pgData = "$env:LOCALAPPDATA\PostgreSQL\data"

if ($Docker) {
    Write-Host "==> Stopping Docker Compose database..." -ForegroundColor Cyan
    docker compose -f docker-compose.dev.yml down
    return
}

if (Test-Path "$pgBin\pg_ctl.exe") {
    Write-Host "==> Stopping native PostgreSQL server..." -ForegroundColor Cyan
    & "$pgBin\pg_ctl.exe" -D $pgData stop
} else {
    docker compose -f docker-compose.dev.yml down
}
