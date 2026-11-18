# Creates / resets the UWorldTrust demo database on LocalDB (or -Server <name>).
#   powershell -ExecutionPolicy Bypass -File database\setup.ps1
param([string]$Server = "(localdb)\MSSQLLocalDB")
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$scripts = "01_schema.sql", "02_procs.sql", "03_seed.sql", "04_fraud_schema.sql", "05_fraud_seed.sql", "06_dev_schema.sql", "07_trust_v2.sql"
foreach ($s in $scripts) {
    Write-Host "-> $s"
    sqlcmd -S $Server -b -i (Join-Path $here $s)
    if ($LASTEXITCODE -ne 0) { throw "$s failed" }
}
Write-Host "UWorldTrust is ready." -ForegroundColor Green
