<#
.SYNOPSIS
  LAPTOP A (servidor): levanta Odoo + PostgreSQL (Docker) y la API .NET escuchando en la red local.

.DESCRIPTION
  1. Inicia los contenedores de Odoo y PostgreSQL.
  2. Muestra las direcciones IP de esta laptop para usarlas en la laptop B.
  3. Revisa si el Firewall de Windows permite conexiones entrantes al puerto de la API. Si no, indica el comando
     que debe ejecutar USTED en una consola de administrador (este script no cambia la configuración de seguridad).
  4. Inicia la API con el perfil "lan" (http://*:5080).

  Odoo (8069) y PostgreSQL NO se exponen a la red: solo la API los usa, en esta misma laptop.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\servidor-lan.ps1
#>
param([int] $Puerto = 5080)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$repo = Split-Path -Parent $PSScriptRoot

Write-Host "1/4 Iniciando Odoo y PostgreSQL..." -ForegroundColor Cyan
docker compose -f (Join-Path $repo "odoo/docker-compose.yml") up -d
if ($LASTEXITCODE -ne 0) { throw "No se pudo iniciar Docker. ¿Docker Desktop está abierto?" }

Write-Host "`n2/4 Direcciones IP de esta laptop (use una de estas en la laptop B):" -ForegroundColor Cyan
$ips = Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -notlike "127.*" -and $_.IPAddress -notlike "169.254.*" -and $_.InterfaceAlias -notmatch "vEthernet|WSL|Loopback|Docker" } |
    Select-Object InterfaceAlias, IPAddress
$ips | Format-Table -AutoSize

Write-Host "3/4 Firewall de Windows (puerto $Puerto)..." -ForegroundColor Cyan
$regla = Get-NetFirewallRule -DisplayName "POS Web API ($Puerto)" -ErrorAction SilentlyContinue
if ($regla) {
    Write-Host "  La regla 'POS Web API ($Puerto)' ya existe." -ForegroundColor Green
} else {
    Write-Host "  No hay una regla para el puerto $Puerto. La laptop B no podrá conectarse hasta que la cree." -ForegroundColor Yellow
    Write-Host "  Abra PowerShell COMO ADMINISTRADOR y ejecute:" -ForegroundColor Yellow
    Write-Host "    New-NetFirewallRule -DisplayName 'POS Web API ($Puerto)' -Direction Inbound -Protocol TCP -LocalPort $Puerto -Action Allow -Profile Private"
    Write-Host "  (La red Wi-Fi debe estar marcada como 'Privada'. Para quitar la regla luego:"
    Write-Host "    Remove-NetFirewallRule -DisplayName 'POS Web API ($Puerto)')"
}

Write-Host "`n4/4 Iniciando la API en http://*:$Puerto (Ctrl+C para detener)..." -ForegroundColor Cyan
dotnet run --project (Join-Path $repo "backend/src/Pos.Api") --launch-profile lan
