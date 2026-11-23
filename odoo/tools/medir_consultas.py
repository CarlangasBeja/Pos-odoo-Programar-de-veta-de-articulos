"""Mide el tiempo de las consultas del POS a través de la API .NET (catálogo, historial y detalle).

Uso:  python odoo/tools/medir_consultas.py [--api http://localhost:5080] [--repeticiones 5]
Cada consulta se repite varias veces; se informa la mediana y el peor caso en milisegundos.
Usa el usuario de demostración "cajero" (solo existe en el entorno Development).
"""
import argparse
import json
import statistics
import sys
import time
import urllib.parse
import urllib.request

sys.stdout.reconfigure(encoding="utf-8")
ap = argparse.ArgumentParser()
ap.add_argument("--api", default="http://localhost:5080")
ap.add_argument("--repeticiones", type=int, default=5)
ap.add_argument("--usuario", default="cajero")
ap.add_argument("--clave", default="Cajero2026!")
args = ap.parse_args()


def call(method, path, body=None, token=None, headers=None):
    req = urllib.request.Request(args.api + path, method=method,
                                 data=None if body is None else json.dumps(body).encode(),
                                 headers={"Content-Type": "application/json", **(headers or {}),
                                          **({"Authorization": f"Bearer {token}"} if token else {})})
    t0 = time.perf_counter()
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            data = json.loads(r.read() or b"null")
            status = r.status
    except urllib.error.HTTPError as e:
        data, status = json.loads(e.read() or b"null"), e.code
    return status, data, (time.perf_counter() - t0) * 1000


_, login, _ = call("POST", "/api/auth/login", {"username": args.usuario, "password": args.clave})
token = login["accessToken"]
_, hist, _ = call("GET", "/api/sales?state=all&pageSize=10", token=token)
some_order = hist["items"][-1]["id"] if hist.get("items") else 1

casos = [
    ("Catálogo: primera página (sin filtro)", "/api/products?search=&pageSize=24&sort=nameAsc"),
    ("Catálogo: página 1000", "/api/products?search=&page=1000&pageSize=24&sort=nameAsc"),
    ("Catálogo: buscar texto 'chocolate'", "/api/products?search=chocolate&pageSize=24&sort=nameAsc"),
    ("Catálogo: buscar texto 'artesanal andino'", "/api/products?search=artesanal%20andino&pageSize=24"),
    ("Catálogo: buscar código 'GEN-0765432'", "/api/products?search=GEN-0765432&pageSize=24"),
    ("Catálogo: buscar código de barras exacto", "/api/products?search=8000000123456&pageSize=24"),
    ("Catálogo: una sola letra 'a' (casi todo coincide)", "/api/products?search=a&pageSize=24"),
    ("Catálogo: texto sin resultados 'zzzxq'", "/api/products?search=zzzxq&pageSize=24"),
    ("Catálogo: orden por precio desc", "/api/products?search=&pageSize=24&sort=priceDesc"),
    ("Catálogo: orden por código", "/api/products?search=&pageSize=24&sort=codeAsc"),
    ("Catálogo: filtrar por categoría", "/api/products?search=&pageSize=24&categoryId=1"),
    ("Catálogo: categoría + texto 'premium'", "/api/products?search=premium&pageSize=24&categoryId=1"),
    ("Historial: primera página", "/api/sales?search=&state=all&pageSize=10"),
    ("Historial: página 500", "/api/sales?search=&state=all&page=500&pageSize=10"),
    ("Historial: buscar 'Histórico - 0500000'", "/api/sales?search=" + urllib.parse.quote("Histórico - 0500000") + "&state=all&pageSize=10"),
    ("Historial: buscar producto 'yogur'", "/api/sales?search=yogur&state=all&pageSize=10"),
    ("Historial: rango de un mes", "/api/sales?search=&state=all&from=2026-06-01&to=2026-06-30&pageSize=10"),
    ("Detalle de una venta", f"/api/sales/{some_order}"),
    ("Sesión activa", "/api/pos/sessions/active"),
]

print(f"{'Consulta':48} {'HTTP':>4} {'mediana':>9} {'peor':>9} {'total filas':>12}")
resultados = []
for nombre, path in casos:
    tiempos, status, data = [], None, None
    for _ in range(args.repeticiones):
        status, data, ms = call("GET", path, token=token)
        tiempos.append(ms)
    total = data.get("totalCount", "") if isinstance(data, dict) else ""
    med, peor = statistics.median(tiempos), max(tiempos)
    resultados.append({"consulta": nombre, "http": status, "mediana_ms": round(med), "peor_ms": round(peor), "total": total})
    marca = "" if peor < 1000 else ("  <- más de 1 s" if peor < 2000 else "  <- MÁS DE 2 s")
    print(f"{nombre:48} {status:>4} {med:>8.0f}ms {peor:>8.0f}ms {str(total):>12}{marca}")


# Venta completa (vista previa + registro en Odoo) con el catálogo de un millón de productos.
import uuid
_, sesion, _ = call("GET", "/api/pos/sessions/active", token=token)
sid = sesion.get("session", sesion)["id"]
metodos = sesion.get("paymentMethods") or sesion["session"]["paymentMethods"]
efectivo = next(m for m in metodos if m["type"] == "cash")["id"]
_, prods, _ = call("GET", "/api/products?search=GEN-09&pageSize=2", token=token)
lineas = [{"productId": p["id"], "quantity": 1} for p in prods["items"][:2]]
st, prev, ms_prev = call("POST", "/api/sales/preview", {"sessionId": sid, "lines": lineas, "discount": None}, token)
st2, venta, ms_venta = call("POST", "/api/sales", {"sessionId": sid, "lines": lineas, "discount": None,
                                                   "payment": {"paymentMethodId": efectivo, "amountTendered": prev["total"]}}, token,
                          headers={"Idempotency-Key": str(uuid.uuid4())})
print(f"{'Venta: vista previa (precios de Odoo)':48} {st:>4} {ms_prev:>8.0f}ms")
print(f"{'Venta: registrar en Odoo':48} {st2:>4} {ms_venta:>8.0f}ms   {venta.get('orderName') if isinstance(venta, dict) else ''}")

# Concurrencia: varias cajas consultando a la vez.
from concurrent.futures import ThreadPoolExecutor
usuarios = 10
rutas = ["/api/products?search=chocolate&pageSize=24", "/api/products?search=&pageSize=24",
         "/api/sales?search=&state=all&pageSize=10", "/api/products?search=GEN-0123456&pageSize=24"]
def tarea(i):
    st, _, ms = call("GET", rutas[i % len(rutas)], token=token)
    return st, ms
with ThreadPoolExecutor(max_workers=usuarios) as ex:
    respuestas = list(ex.map(tarea, range(usuarios * 4)))
tiempos = sorted(ms for _, ms in respuestas)
errores = sum(1 for st, _ in respuestas if st != 200)
p95 = tiempos[int(len(tiempos) * 0.95) - 1]
print(f"{f'Concurrencia: {usuarios} usuarios, {len(tiempos)} consultas':48} {'':>4} {statistics.median(tiempos):>8.0f}ms {p95:>8.0f}ms (mediana / p95)  errores: {errores}")
resultados.append({"consulta": "concurrencia", "usuarios": usuarios, "mediana_ms": round(statistics.median(tiempos)), "p95_ms": round(p95), "errores": errores})
resultados.append({"consulta": "venta", "preview_ms": round(ms_prev), "registro_ms": round(ms_venta), "http": st2})
print(json.dumps(resultados, ensure_ascii=False))
