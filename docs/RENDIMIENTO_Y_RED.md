# Rendimiento con 1 millón de registros y despliegue en dos laptops

Cambios pedidos para esta versión:

1. Cargar **1 000 000 de registros** en el sistema.
2. Que las consultas respondan en **1 a 2 segundos como máximo**.
3. Que funcione en **dos laptops**: una con el backend y la base de datos (Odoo + PostgreSQL) y otra con el frontend.

## Resultado (medido el 22/11/2026)

Base de datos con **1 000 011 productos, 1 000 031 ventas, ~2 000 000 de líneas y ~1 000 000 de pagos** (2,6 GB).
Medición a través de la API .NET, por la **IP de red** (`odoo/tools/medir_consultas.py --api http://<IP>:5080`), 5 repeticiones
por consulta. Evidencias completas en [docs/evidencias/rendimiento](evidencias/rendimiento).

| Consulta | Antes (sin optimizar) | Después | Objetivo ≤ 1–2 s |
|---|---:|---:|---|
| Catálogo: primera página (1 000 011 productos) | 2 769 ms | 48 ms | ✅ |
| Catálogo: página 1000 | 4 069 ms | 491 ms | ✅ |
| Catálogo: buscar "chocolate" (49 874 coincidencias) | 3 088 ms | 372 ms | ✅ |
| Catálogo: buscar código `GEN-0765432` | 3 149 ms | 168 ms | ✅ |
| Catálogo: buscar código de barras | 2 785 ms | 144 ms | ✅ |
| Catálogo: una sola letra "a" (988 130 coincidencias) | — | 52 ms | ✅ |
| Catálogo: categoría + texto | — | 129 ms | ✅ |
| Historial: primera página (1 000 031 ventas) | 1 695 ms | 149 ms | ✅ |
| Historial: buscar `Histórico - 0500000` | 7 797 ms | 142 ms | ✅ |
| Historial: buscar producto "yogur" (97 681 ventas) | 2 582 ms | 347 ms | ✅ |
| Venta completa: vista previa + registro en Odoo | — | 74 + 194 ms | ✅ |
| 10 usuarios a la vez (40 consultas) | p95 3 162 ms | p95 943 ms, 0 errores | ✅ |

Valores "peor caso" de 5 repeticiones. Después de reiniciar Odoo, la **primera** venta que atiende cada proceso de Odoo
tarda ~1,5 s (carga su caché); las siguientes, ~0,2 s.

**Prueba con dos equipos.** Se ejecutó el frontend en un contenedor Linux aparte (`node:22-alpine`, sin Odoo ni .NET),
conectado a la API por la IP de red `192.168.0.102:5080`, y se recorrió la interfaz real: catálogo de un millón, búsqueda
(703 ms incluyendo la espera de 250 ms al escribir), venta registrada en Odoo (426 ms) e historial (371 ms)
([05-prueba-dos-equipos.txt](evidencias/rendimiento/05-prueba-dos-equipos.txt) y capturas `lan_*.png`).
Con dos laptops reales hay que crear además la regla del firewall (sección 4).

## 1. Carga de un millón de registros

```powershell
powershell -ExecutionPolicy Bypass -File .\odoo\setup.ps1          # una vez: Odoo, productos base, sesión, API key
powershell -ExecutionPolicy Bypass -File .\odoo\carga-masiva.ps1   # 1 000 000 de productos y 1 000 000 de ventas + índices
powershell -ExecutionPolicy Bypass -File .\odoo\carga-masiva.ps1 -Productos 100000 -Ordenes 100000   # carga menor
powershell -ExecutionPolicy Bypass -File .\odoo\carga-masiva.ps1 -Borrar                            # quitar los datos sintéticos
```

Qué se carga (`odoo/tools/carga_masiva.sql`):

| Datos | Cantidad | Marca para reconocerlos |
|---|---|---|
| Productos (`product.template` + `product.product`) con impuestos y categoría POS | 1 000 000 | código `GEN-0000001` … `GEN-1000000`, código de barras `8000000000001` … |
| Ventas históricas pagadas (`pos.order`) en una sesión cerrada `POS Web .NET/HISTORICO` | 1 000 000 | nombre `Histórico - 0000001` …, recibo `900-1-0000001` … |
| Líneas de venta (`pos.order.line`) | 2 000 000 | 2 por venta, con productos sintéticos |
| Pagos en efectivo (`pos.payment`) | 1 000 000 | 1 por venta |

**Por qué por SQL y no con el ORM de Odoo.** Crear un millón de productos con el ORM tarda horas. El script **clona**
registros reales que creó `setup_pos.py` (productos y ventas que Odoo ya validó) y solo cambia nombre, códigos, precio,
fecha y `uuid`; así los datos tienen exactamente la forma que Odoo espera. Es una herramienta de preparación de datos de
prueba: la API .NET **sigue sin acceder a PostgreSQL** (sección 14 del enunciado) y todas sus consultas pasan por JSON-2.

Las ventas históricas van en una sesión **cerrada** aparte para que la sesión abierta del POS (la que usa el cajero) no
cargue con un millón de órdenes.

## 2. Qué se optimizó y por qué

Primero se midió (línea base) y se activó el registro de consultas lentas de PostgreSQL para ver el SQL **exacto** que
genera Odoo. Cada cambio responde a una causa observada:

| # | Problema observado | Solución | Dónde |
|---|---|---|---|
| 1 | `ilike '%texto%'` en código y código de barras: los índices btree de Odoo no sirven, se recorría el millón | Índices **trigrama** (`pg_trgm`, GIN) | `odoo/tools/indices_rendimiento.sql` |
| 2 | Orden por nombre traducido (`COALESCE(name->>'es_419', …)`) sin índice: ordenaba 1M filas por página | Índice de expresión parcial (solo productos del POS) y otro por precio | mismo archivo |
| 3 | La búsqueda hacía un `OR` entre columnas de **dos tablas** (nombre en la plantilla, código en la variante): PostgreSQL no puede combinar índices así | El dominio usa `('product_tmpl_id', 'any', [...])`: Odoo genera `IN (subconsulta)` sobre una sola tabla y PostgreSQL combina los índices (`BitmapOr`). Código: de 5,1 s a 12 ms en SQL | `OdooProductCatalog.BuildDomain` |
| 4 | Historial: `OR` entre el nombre de la orden y los productos de sus 2M líneas; con términos raros revisaba todas las órdenes | Dos búsquedas: primero por orden o recibo; **solo si no hay ninguna**, por producto vendido. Cada una usa su índice trigrama | `OdooPosOrderGateway.SearchAsync` |
| 5 | Ventas más recientes primero sobre 1M | Índice `(config_id, date_order DESC, id DESC)` | `indices_rendimiento.sql` |
| 6 | Contar el total del catálogo (1M) costaba ~300–400 ms en **cada** página | Los totales grandes (≥ 10 000) se guardan 60 s en caché; los pequeños se cuentan siempre | `OdooProductCatalog.CountAsync` |
| 7 | Odoo en Docker usa por defecto **un solo proceso**: con varias cajas las consultas esperaban en fila (p95 3,1 s) | `--workers=4` (variable `ODOO_WORKERS`) | `odoo/docker-compose.yml` |
| 8 | PostgreSQL con 128 MB de caché y JIT activo | `shared_buffers=512MB`, `effective_cache_size=2GB`, `work_mem=16MB`, `random_page_cost=1.1`, `jit=off` | `odoo/docker-compose.yml` |
| 9 | **Errores** con consultas en paralelo: `could not resize shared memory segment … No space left on device` (Docker da 64 MB de `/dev/shm`) | `shm_size: 1gb` | `odoo/docker-compose.yml` |
| 10 | La API en `0.0.0.0` solo escucha IPv4: `localhost` intentaba primero IPv6 y cada solicitud tardaba **2 s más** | Perfil `lan` con `http://*:5080` (IPv4 e IPv6) | `launchSettings.json` |

**Decisiones y compromisos (para la defensa):**

- *Por qué índices y no una caché de productos en la API:* Odoo sigue siendo la fuente de verdad (precios e impuestos
  siempre actuales). Una copia del catálogo en la API duplicaría datos empresariales, algo que el enunciado pide evitar.
- *Historial en dos pasos:* si el texto coincide con una orden o un recibo, se muestran esas ventas y no las que solo
  coinciden por producto. Es más rápido y, para un cajero que busca un número de orden, más útil.
- *Búsqueda en la plantilla:* el código y el código de barras se buscan en `product.template`, que los guarda para los
  productos de **una sola variante** (todos los del POS). Un producto con varias variantes se encuentra por nombre.
- *Totales en caché:* durante 60 s un total grande puede no incluir un producto recién creado. Los totales menores de
  10 000, como los de una búsqueda concreta, son siempre exactos.
- Ninguno de estos cambios toca Domain ni Application: todo está en Infrastructure (adaptadores de Odoo) y en la
  configuración de Docker. Es la ventaja de Clean Architecture: la integración se optimizó sin cambiar las reglas.

### Cómo repetir la medición

```powershell
python odoo/tools/medir_consultas.py                                  # contra http://localhost:5080
python odoo/tools/medir_consultas.py --api http://192.168.0.102:5080  # por la red, desde la laptop B
```

## 3. La interfaz web de Odoo con un millón de registros

Además del POS, se midió la interfaz de administración de Odoo (`odoo/tools/medir_odoo_web.js`, usuario `admin`), con los
mismos datos. Resultados en [06-odoo-web-antes.txt](evidencias/rendimiento/06-odoo-web-antes.txt):

| Pantalla de Odoo | Tiempo | Estado |
|---|---:|---|
| Lista de órdenes del POS (1 000 033 ventas) | 1,3 s | ✅ |
| Buscar la orden `Histórico - 0500000` | 0,4 s | ✅ |
| Abrir el detalle de una orden | 0,3 s | ✅ |
| Lista de productos del POS (1 000 013) | 2,2 s | ⚠️ |
| Buscar producto por código `GEN-0765432` | 3,7 s | ❌ |
| Buscar producto "chocolate" | 6,7 s | ❌ |
| Tablero del punto de venta | 39 s | ❌ |

**Por qué y por qué no se corrigió.** Estas consultas las genera el **código de Odoo**, no el POS:

- *Búsqueda de productos:* Odoo arma un `OR` con un `EXISTS` correlacionado y `UNION ALL` (nombre, códigos de todas las
  variantes y coincidencias exactas). PostgreSQL debe evaluarlo fila por fila sobre el millón de plantillas; ningún índice
  cambia esa forma de consulta. La API del POS no tiene este problema porque arma su propio dominio, optimizado
  (`OdooProductCatalog.BuildDomain`, sección 2).
- *Tablero del punto de venta:* `pos_config.py` (Odoo 20, línea ~501) calcula las estadísticas de la sesión con
  `session.order_ids`. Por la precarga en bloque del ORM, lee las órdenes de **todas** las sesiones del punto de venta
  (`WHERE session_id IN (...)` sin límite) y las filtra en Python. Con un millón de ventas en el mismo punto de venta tarda
  decenas de segundos, sin importar cómo se repartan entre sesiones.

Corregirlas exige un módulo de Odoo que modifique esos métodos, y el enunciado indica que no se requiere modificar el código
fuente de Odoo ni desarrollar módulos Python (sección 13). Por eso se documentan como limitación: **el POS (frontend + API)
cumple el objetivo de 1–2 s con un millón de registros; la pantalla de administración de Odoo no, en esas tres vistas.**

## 4. Dos laptops en la misma red

```
 Laptop B (cliente)                                Laptop A (servidor)
┌────────────────────────┐                        ┌───────────────────────────────────────┐
│ Navegador              │                        │ API .NET  http://*:5080               │
│   http://localhost:5173│   /api  (HTTP, LAN)    │   │  JSON-2 (solo local)               │
│ Vite (frontend) ───────┼───────────────────────►│   ▼                                    │
│   reenvía /api         │                        │ Odoo 20 :8069 ─── PostgreSQL 16 (Docker)│
└────────────────────────┘                        └───────────────────────────────────────┘
```

- El navegador habla **solo** con la laptop B; Vite reenvía `/api` a la laptop A. Por eso no hay que configurar CORS.
- Odoo (8069) y PostgreSQL **no** se exponen a la red: solo la API los usa, en la misma laptop. La API key de Odoo sigue
  únicamente en la laptop A.
- Se agregó el perfil `lan` a la API (`launchSettings.json`): escucha en `http://*:5080` (todas las interfaces, IPv4 e IPv6) en lugar de solo `localhost`.

### Laptop A (backend + base de datos)

Requisitos: Docker Desktop, .NET 10 SDK y haber ejecutado `odoo/setup.ps1` (y, si se quiere, `odoo/carga-masiva.ps1`).

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\servidor-lan.ps1
```

El script levanta Odoo y PostgreSQL, muestra la IP de la laptop, revisa el firewall e inicia la API con el perfil `lan`.

**Firewall de Windows (una sola vez, lo hace usted como administrador).** Windows bloquea por defecto las conexiones
entrantes. El script no cambia la seguridad del equipo; si falta la regla, muestra este comando para ejecutarlo en una
consola de **administrador**:

```powershell
New-NetFirewallRule -DisplayName 'POS Web API (5080)' -Direction Inbound -Protocol TCP -LocalPort 5080 -Action Allow -Profile Private
```

La red Wi-Fi debe estar como **Privada** (Configuración → Red e Internet → Wi-Fi → la red → *Tipo de perfil de red:
Privada*). En una red **Pública** Windows bloquea la conexión aunque exista la regla. Para quitar la regla después:
`Remove-NetFirewallRule -DisplayName 'POS Web API (5080)'`.

### Laptop B (frontend)

Requisitos: Node.js 20 o superior y una copia del repositorio (no necesita Docker, .NET ni Odoo).

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\cliente-lan.ps1 -Servidor 192.168.0.102              # IP de la laptop A
powershell -ExecutionPolicy Bypass -File .\scripts\cliente-lan.ps1 -Servidor 192.168.0.102 -Produccion  # versión compilada
```

El script prueba la conexión con `http://<IP>:5080/api/health`, indica qué revisar si falla y arranca el frontend. Abra
`http://localhost:5173` (o `http://localhost:4173` con `-Produccion`) en la laptop B.

Alternativa sin Vite en medio: compilar con `VITE_API_BASE_URL=http://<IP-laptop-A>:5080` y agregar el origen del frontend
a `Cors:AllowedOrigins` de la API (variable `Cors__AllowedOrigins__1=http://<IP-laptop-B>:4173`). Se prefiere el reenvío
de Vite porque no requiere tocar CORS.

### Problemas frecuentes

| Síntoma | Causa | Solución |
|---|---|---|
| `cliente-lan.ps1` dice "No hay conexión" | Firewall o red Pública en la laptop A | Crear la regla y marcar la red como Privada |
| Conecta por cable pero no por Wi-Fi | Red de la universidad con aislamiento de clientes | Usar un hotspot del celular o un router propio |
| El frontend dice "error inesperado (HTTP 500)" | Vite no llega a la API | Revisar la IP y que `servidor-lan.ps1` siga en ejecución |
| `/api/health` responde pero Odoo "no disponible" | Docker detenido en la laptop A | Abrir Docker Desktop y volver a ejecutar `servidor-lan.ps1` |
