<#
.SYNOPSIS
  Carga datos SINTÉTICOS en Odoo para pruebas de rendimiento (por defecto, 1 000 000 de productos y 1 000 000 de ventas
  históricas) y crea los índices que necesitan las consultas del POS.

.DESCRIPTION
  Requiere haber ejecutado antes odoo/setup.ps1. Clona por SQL los productos y ventas reales que creó el script de
  preparación (ver odoo/tools/carga_masiva.sql). Es repetible: borra la carga anterior antes de crear la nueva.
  Para quitar los datos sintéticos:  .\odoo\carga-masiva.ps1 -Borrar

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\odoo\carga-masiva.ps1
  powershell -ExecutionPolicy Bypass -File .\odoo\carga-masiva.ps1 -Productos 100000 -Ordenes 100000
  powershell -ExecutionPolicy Bypass -File .\odoo\carga-masiva.ps1 -Borrar
#>
param(
    [int] $Productos = 1000000,
    [int] $Ordenes = 1000000,
    [string] $Database = "pos",
    [switch] $Borrar
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$compose = Join-Path $PSScriptRoot "docker-compose.yml"

function Invoke-Sql([string] $file, [string[]] $variables = @()) {
    $args = @("compose", "-f", $compose, "exec", "-T", "db", "psql", "-U", "odoo", "-d", $Database, "-v", "ON_ERROR_STOP=1")
    foreach ($v in $variables) { $args += @("-v", $v) }
    $args += @("-f", "/opt/pos-setup/tools/$file")
    & docker @args
    if ($LASTEXITCODE -ne 0) { throw "Falló $file" }
}

# Los scripts SQL se copian al contenedor de PostgreSQL (el de Odoo ya los monta en /opt/pos-setup).
& docker compose -f $compose exec -T db mkdir -p /opt/pos-setup/tools | Out-Null
foreach ($f in "carga_masiva.sql", "borrar_carga_masiva.sql", "indices_rendimiento.sql") {
    $id = (& docker compose -f $compose ps -q db).Trim()
    & docker cp (Join-Path $PSScriptRoot "tools\$f") "${id}:/opt/pos-setup/tools/$f" | Out-Null
}

if ($Borrar) {
    Write-Host "Borrando los datos sintéticos..." -ForegroundColor Cyan
    Invoke-Sql "borrar_carga_masiva.sql"
    Write-Host "Listo." -ForegroundColor Green
    return
}

$inicio = Get-Date
Write-Host "Cargando $Productos productos y $Ordenes ventas históricas (puede tardar varios minutos)..." -ForegroundColor Cyan
Invoke-Sql "carga_masiva.sql" @("productos=$Productos", "ordenes=$Ordenes")
Write-Host "`nCreando índices para las consultas del POS..." -ForegroundColor Cyan
Invoke-Sql "indices_rendimiento.sql"
Write-Host ("`nListo en {0:N1} minutos." -f ((Get-Date) - $inicio).TotalMinutes) -ForegroundColor Green
