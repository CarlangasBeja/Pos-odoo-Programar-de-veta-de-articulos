<#
.SYNOPSIS
  LAPTOP B (cliente): ejecuta el frontend y lo conecta con la API de la laptop A.

.DESCRIPTION
  Comprueba que la API de la laptop A responde y luego inicia Vite. El navegador habla solo con esta laptop
  (http://localhost:5173) y Vite reenvía /api a la laptop A, así que no hace falta configurar CORS.
  Con -Produccion compila el frontend y lo sirve optimizado (vite preview, puerto 4173).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\cliente-lan.ps1 -Servidor 192.168.1.50
  powershell -ExecutionPolicy Bypass -File .\scripts\cliente-lan.ps1 -Servidor 192.168.1.50 -Produccion
#>
param(
    [Parameter(Mandatory = $true)][string] $Servidor,
    [int] $Puerto = 5080,
    [switch] $Produccion
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$repo = Split-Path -Parent $PSScriptRoot
$api = "http://${Servidor}:$Puerto"

Write-Host "1/3 Probando la conexión con la API de la laptop A ($api)..." -ForegroundColor Cyan
$tcp = Test-NetConnection -ComputerName $Servidor -Port $Puerto -WarningAction SilentlyContinue
if (-not $tcp.TcpTestSucceeded) {
    Write-Host "  No hay conexión con $api." -ForegroundColor Red
    Write-Host "  Revise: (1) ambas laptops en la misma red; (2) la API iniciada con scripts/servidor-lan.ps1;"
    Write-Host "  (3) la regla del Firewall de Windows en la laptop A; (4) la red marcada como 'Privada'."
    exit 1
}
try {
    $salud = Invoke-RestMethod "$api/api/health" -TimeoutSec 10
    Write-Host ("  API: {0}; Odoo: {1}" -f $salud.status, $salud.checks.odoo.description) -ForegroundColor Green
} catch {
    Write-Host "  El puerto responde, pero /api/health falló: $($_.Exception.Message)" -ForegroundColor Yellow
}

Set-Location (Join-Path $repo "frontend")
if (-not (Test-Path node_modules)) {
    Write-Host "`n2/3 Instalando dependencias del frontend..." -ForegroundColor Cyan
    npm install --no-audit --no-fund
}

$env:POS_API_URL = $api
if ($Produccion) {
    Write-Host "`n3/3 Compilando y sirviendo el frontend optimizado en http://localhost:4173 ..." -ForegroundColor Cyan
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "Falló la compilación del frontend." }
    npm run preview
} else {
    Write-Host "`n3/3 Iniciando el frontend en http://localhost:5173 ..." -ForegroundColor Cyan
    npm run dev
}
