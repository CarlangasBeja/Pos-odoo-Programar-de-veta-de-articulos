#!/usr/bin/env bash
# Prepara Odoo 20 Community en Docker para el POS Web (.NET) y guarda los secretos del backend.
# Equivalente a setup.ps1 para Linux/macOS. Uso: ./odoo/setup.sh [base] [país] [idioma]
set -euo pipefail

DATABASE="${1:-pos}"
COUNTRY="${2:-CO}"
LANGUAGE="${3:-es_419}"
ODOO_URL="${ODOO_URL:-http://localhost:8069}"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(dirname "$HERE")"
COMPOSE=(docker compose -f "$HERE/docker-compose.yml")
API_PROJECT="$REPO/backend/src/Pos.Api"
export ODOO_DB_NAME="$DATABASE"

step() { printf '\n==> %s\n' "$1"; }
for tool in docker dotnet python3; do
  command -v "$tool" >/dev/null || { echo "No se encontró '$tool'. Instálelo y vuelva a intentarlo." >&2; exit 1; }
done

step "1/5 Iniciando PostgreSQL y Odoo 20"
"${COMPOSE[@]}" up -d
echo "Esperando a que Odoo responda en $ODOO_URL ..."
for _ in $(seq 1 90); do
  if curl -fsS "$ODOO_URL/web/health" >/dev/null 2>&1; then ready=1; break; fi
  sleep 2
done
[ "${ready:-0}" = 1 ] || { echo "Odoo no respondió. Revise: docker compose -f odoo/docker-compose.yml logs odoo" >&2; exit 1; }

step "2/5 Creando la base de datos '$DATABASE' (si no existe)"
if [ "$("${COMPOSE[@]}" exec -T db psql -U odoo -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$DATABASE'" | tr -d '[:space:]')" != "1" ]; then
  "${COMPOSE[@]}" exec -T odoo odoo db init "$DATABASE" --country "$COUNTRY" --language "$LANGUAGE"
else
  echo "La base '$DATABASE' ya existe; se reutiliza."
fi

step "3/5 Instalando el módulo Punto de venta"
"${COMPOSE[@]}" exec -T odoo odoo -d "$DATABASE" -i point_of_sale --stop-after-init --no-http

step "4/5 Configurando productos, impuestos, pagos, sesión POS y usuario de integración"
OUTPUT="$("${COMPOSE[@]}" exec -T odoo sh -c "odoo shell -d $DATABASE --no-http < /opt/pos-setup/setup_pos.py")"
grep '^\[setup\]' <<<"$OUTPUT" || true
JSON="$(grep '^@@POS_SETUP@@' <<<"$OUTPUT" | tail -n 1 | sed 's/^@@POS_SETUP@@//')"
[ -n "$JSON" ] || { echo "setup_pos.py no devolvió el resultado esperado." >&2; exit 1; }
field() { python3 -c "import json,sys; print(json.loads(sys.argv[1])[sys.argv[2]])" "$JSON" "$1"; }

step "5/5 Guardando la configuración en los user-secrets del backend"
dotnet user-secrets set "Odoo:BaseUrl" "$ODOO_URL" --project "$API_PROJECT" >/dev/null
dotnet user-secrets set "Odoo:Database" "$(field database)" --project "$API_PROJECT" >/dev/null
dotnet user-secrets set "Odoo:PosConfigId" "$(field posConfigId)" --project "$API_PROJECT" >/dev/null
dotnet user-secrets set "Odoo:ApiKey" "$(field apiKey)" --project "$API_PROJECT" >/dev/null

echo
echo "Listo. Odoo: $ODOO_URL (admin / admin)"
echo "  Punto de venta: $(field posConfigName) (id $(field posConfigId))"
echo "  Sesión POS:     $(field sessionName) ($(field sessionState))"
echo "  Moneda:         $(field currency)   Redondeo: $(field taxRounding)"
echo "  API key:        guardada en user-secrets"
echo
echo "Siguiente paso: dotnet run --project backend/src/Pos.Api  y  cd frontend && npm run dev"
