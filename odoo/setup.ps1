<#
.SYNOPSIS
  Prepara Odoo 20 Community en Docker para el POS Web (.NET) y configura los secretos del backend.

.DESCRIPTION
  1. Levanta PostgreSQL y Odoo 20 (docker compose).
  2. Crea la base de datos (compañía en Colombia, español) si no existe.
  3. Instala los módulos Punto de venta (point_of_sale) e Inventario (stock), para mostrar existencias.
  4. Ejecuta setup_pos.py: impuestos, productos, métodos de pago, punto de venta, usuario de integración,
     sesión POS abierta y una API key nueva.
  5. Guarda la API key y los identificadores en los user-secrets de Pos.Api (nunca en el repositorio).

  Es seguro ejecutarlo varias veces: no duplica datos (solo genera una API key nueva).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\odoo\setup.ps1
  powershell -ExecutionPolicy Bypass -File .\odoo\setup.ps1 -Country US -Language en_US
#>
param(
    [string] $Database = "pos",
    [string] $Country = "CO",
    [string] $Language = "es_419",
    [string] $OdooUrl = "http://localhost:8069"
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$here = $PSScriptRoot
$repo = Split-Path -Parent $here
$compose = Join-Path $here "docker-compose.yml"
$apiProject = Join-Path $repo "backend/src/Pos.Api"
$env:ODOO_DB_NAME = $Database

function Step([string] $text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Invoke-Checked([scriptblock] $command, [string] $failure) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw $failure }
}

foreach ($tool in "docker", "dotnet") {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "No se encontró '$tool'. Instálelo (Docker Desktop / .NET 10 SDK) y vuelva a intentarlo."
    }
}

Step "1/5 Iniciando PostgreSQL y Odoo 20 (la primera vez descarga las imágenes)"
Invoke-Checked { docker compose -f $compose up -d } "No se pudieron iniciar los contenedores. ¿Docker Desktop está abierto?"

Write-Host "Esperando a que Odoo responda en $OdooUrl ..."
$ready = $false
for ($i = 0; $i -lt 90; $i++) {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri "$OdooUrl/web/health" -TimeoutSec 3
        if ($response.StatusCode -eq 200) { $ready = $true; break }
    } catch { }
    Start-Sleep -Seconds 2
}
if (-not $ready) { throw "Odoo no respondió en $OdooUrl. Revise: docker compose -f odoo/docker-compose.yml logs odoo" }

Step "2/5 Creando la base de datos '$Database' (si no existe)"
$exists = (docker compose -f $compose exec -T db psql -U odoo -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$Database'") -join ""
if ($exists.Trim() -ne "1") {
    Invoke-Checked { docker compose -f $compose exec -T odoo odoo db init $Database --country $Country --language $Language } "No se pudo crear la base de datos."
} else {
    Write-Host "La base '$Database' ya existe; se reutiliza."
}

Step "3/5 Instalando el módulo Punto de venta (puede tardar unos minutos)"
Invoke-Checked { docker compose -f $compose exec -T odoo odoo -d $Database -i point_of_sale,stock --stop-after-init --no-http } "No se pudo instalar point_of_sale y stock."

Step "4/5 Configurando productos, impuestos, pagos, sesión POS y usuario de integración"
$output = docker compose -f $compose exec -T odoo sh -c "odoo shell -d $Database --no-http < /opt/pos-setup/setup_pos.py"
if ($LASTEXITCODE -ne 0) { throw "Falló setup_pos.py. Revise el mensaje anterior." }
$output | Where-Object { $_ -like "[setup]*" } | ForEach-Object { Write-Host $_ }
$marker = "@@POS_SETUP@@"
$line = $output | Where-Object { $_ -like "$marker*" } | Select-Object -Last 1
if (-not $line) { throw "setup_pos.py no devolvió el resultado esperado." }
$result = $line.Substring($marker.Length) | ConvertFrom-Json

Step "5/5 Guardando la configuración en los user-secrets del backend (fuera del repositorio)"
Invoke-Checked { dotnet user-secrets set "Odoo:BaseUrl" $OdooUrl --project $apiProject | Out-Null } "No se pudo guardar Odoo:BaseUrl"
Invoke-Checked { dotnet user-secrets set "Odoo:Database" $result.database --project $apiProject | Out-Null } "No se pudo guardar Odoo:Database"
Invoke-Checked { dotnet user-secrets set "Odoo:PosConfigId" "$($result.posConfigId)" --project $apiProject | Out-Null } "No se pudo guardar Odoo:PosConfigId"
Invoke-Checked { dotnet user-secrets set "Odoo:ApiKey" $result.apiKey --project $apiProject | Out-Null } "No se pudo guardar Odoo:ApiKey"

Write-Host ""
Write-Host "Listo. Odoo 20 quedó preparado:" -ForegroundColor Green
Write-Host ("  Odoo:            {0}  (usuario admin / contraseña admin)" -f $OdooUrl)
Write-Host ("  Base de datos:   {0}   Moneda: {1}   Redondeo de impuestos: {2}" -f $result.database, $result.currency, $result.taxRounding)
Write-Host ("  Punto de venta:  {0} (id {1})" -f $result.posConfigName, $result.posConfigId)
Write-Host ("  Sesión POS:      {0} (id {1}, estado {2})" -f $result.sessionName, $result.sessionId, $result.sessionState)
Write-Host ("  Métodos de pago: efectivo id {0}, tarjeta id {1}" -f $result.paymentMethods.cash, $result.paymentMethods.card)
Write-Host ("  API key:         guardada en user-secrets (vence en {0} días)" -f $result.apiKeyExpiresInDays)
Write-Host ""
Write-Host "Siguiente paso: abra backend/PosOdoo.slnx en Visual Studio y ejecute Pos.Api (perfil http)."
