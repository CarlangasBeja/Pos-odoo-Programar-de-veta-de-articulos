# Integración con Odoo 20 Community

## Mecanismo validado

- **API**: JSON-2 de Odoo 20 (`POST /json/2/<modelo>/<método>`), con `Authorization: bearer <API key>` y `X-Odoo-Database: <base>`.
  Los argumentos van por nombre en el cuerpo JSON y cada llamada es una transacción. Código del controlador: `addons/rpc/controllers/json2.py`.
- **Registro de la venta y el pago**: `pos.order.sync_from_ui(orders)`, el mismo método que usa el POS web de Odoo
  (`addons/point_of_sale/static/src/app/services/pos_store.js`). Se contrastó con el código de la rama 20.0
  (`addons/point_of_sale/models/pos_order.py`) y **se ejecutó con éxito** contra una instancia instalada (ver
  [evidencias/P09-venta-real-odoo20.md](evidencias/P09-venta-real-odoo20.md)).

Hechos verificados en el código de Odoo 20 que condicionan el diseño:

| Hecho | Consecuencia en este proyecto |
|---|---|
| `sync_from_ui` busca la orden por `uuid` y `pos.order` tiene la restricción `unique (uuid)` | La clave de idempotencia se usa como `uuid` (D3). |
| Un reenvío de una orden pagada pasa por `_update_lines`, que convierte en actualización las líneas cuyo `uuid` ya existe | Los `uuid` de líneas y pago son UUID v5 deterministas. |
| `_process_saved_order` llama a `_compute_prices` y luego `action_pos_order_paid` exige pago exacto | El backend calcula el total con las mismas reglas que Odoo (D6) y lo verifica antes de cobrar. |
| El cambio se envía en `amount_return` **en negativo**; Odoo crea un pago de devolución en efectivo | `PosOrderPayloadMapper` envía `-cambio`. |
| `price_subtotal` y `price_subtotal_incl` de cada línea son obligatorios | Se envían calculados por el dominio. |
| Los errores de negocio responden 422 con `{name, message}`; la transacción se revierte | Se informa "rechazada" solo después de verificar que la orden no existe. |
| La compañía usa por defecto `round_globally` | `TaxCalculator` soporta ambos modos. |
| API keys de usuarios no administradores duran 1 día salvo que su grupo indique otra cosa | `setup_pos.py` crea el grupo "POS Web - Integración (API)" con 180 días. |

## Comparación con la validación técnica del docente (Postman)

El documento *Validación técnica de Odoo 20 Community mediante API JSON-2* creó una orden con `pos.order/create`, escribió líneas e
importes con `write`, creó el pago con `pos.payment/create`, tuvo que fijar `amount_paid` a mano y luego llamó a
`action_pos_order_paid`. El propio documento advierte que esos ajustes manuales no son un flujo de cobro validado y deja como "no
validados" `sync_from_ui`, el flujo automático desde C# y la prevención de duplicados. Este proyecto resuelve cada punto que el
Anexo F deja al grupo:

| Etapa (Anexo F) | Qué debía resolver el grupo | Cómo se resolvió aquí |
|---|---|---|
| Preparación | Consultar registros reales y validar permisos | `odoo/setup.ps1` crea sesión, productos, métodos de pago y un usuario de integración **no administrador**; la API consulta la sesión vigente y los productos en cada venta (`OdooPosSessionGateway`, `OdooProductCatalog`). |
| Orden | Definir argumentos, validaciones y transacción de negocio | Un solo `pos.order.sync_from_ui` con orden, líneas y pago (el mismo método del POS de Odoo); cada llamada JSON-2 es una transacción. Las validaciones de forma y de negocio ocurren antes de llamar a Odoo (Validators + dominio). |
| Líneas | Investigar creación y cálculo correctos | Las líneas viajan con `price_subtotal` y `price_subtotal_incl` calculados por el dominio con las mismas reglas que Odoo (600 vectores de paridad, D6). |
| Totales | Evitar escrituras diagnósticas de importes | No hay ninguna escritura manual: Odoo recalcula con `_compute_prices` y acepta el total porque coincide. |
| Pago | Vincular el cobro de forma coherente y verificable | El pago va dentro de la misma orden; el cambio se envía en `amount_return` (negativo) y Odoo crea la devolución en efectivo. |
| Estado | Finalizar sin intervención manual | `sync_from_ui` deja la orden pagada; la API la lee por `uuid` y solo informa éxito si el estado es `paid` o `done` (RN-07). |
| Consulta | Probar persistencia, totales y errores | Historial y detalle se leen de Odoo (`search_read`); las pruebas P09 verifican total, pago, cambio y líneas en la orden real. |
| Duplicados (Anexo G) | Controlar reintentos desde la API .NET | Clave de idempotencia = `uuid` de la orden (restricción `unique` en Odoo) + almacén de intentos; repetir no crea otra orden (P06, P09). |

La colección [postman/POS-Odoo20.postman_collection.json](postman/POS-Odoo20.postman_collection.json) incluye, además de los casos de
uso de la API, las consultas de solo lectura a Odoo (sesiones, productos, métodos de pago y la orden por `uuid`). Sirven para estudiar
el ERP y comprobar lo que registró el POS. La API key se escribe solo en la copia local de Postman, nunca en el repositorio.

## Preparación automática (recomendada)

Requisitos: Docker Desktop y .NET 10 SDK.

```powershell
powershell -ExecutionPolicy Bypass -File .\odoo\setup.ps1          # Windows
./odoo/setup.sh                                                    # Linux / macOS
```

El script:

1. Levanta `postgres:16` y `odoo:20.0` (`odoo/docker-compose.yml`).
2. Crea la base `pos` con la compañía en Colombia y español (`odoo db init pos --country CO --language es_419`), lo que instala la
   localización colombiana y la moneda COP.
3. Instala `point_of_sale`.
4. Ejecuta `odoo/setup_pos.py` dentro de `odoo shell`:
   - impuestos "IVA 19 %", "IVA 5 %" e "IVA 19 % incluido";
   - 4 categorías POS y 11 productos (10 habilitados en el POS y 1 deshabilitado para probar P04);
   - diario y método de pago **Efectivo** y método **Tarjeta (datáfono externo)**;
   - punto de venta "POS Web .NET" sin redondeo de efectivo;
   - usuario de integración `pos.integracion` con permisos de **Usuario del POS** (no administrador);
   - sesión POS **abierta**;
   - una API key nueva (ámbito `rpc`, 179 días).
5. Guarda `Odoo:BaseUrl`, `Odoo:Database`, `Odoo:PosConfigId` y `Odoo:ApiKey` en los **user-secrets** de `Pos.Api`.

Es idempotente: se puede ejecutar de nuevo (por ejemplo, para renovar la API key) sin duplicar datos.
Odoo queda en http://localhost:8069 con el usuario `admin` / contraseña `admin`.

> Si su versión de Docker no encuentra la imagen `odoo:20.0`, revise las etiquetas disponibles en Docker Hub (`odoo`) y cambie la línea
> `image:` de `odoo/docker-compose.yml`.

## Preparación manual (alternativa sin el script)

1. En Odoo: **Aplicaciones → Punto de venta → Instalar**.
2. **Punto de venta → Configuración → Métodos de pago**: cree "Efectivo" con un diario de tipo *Efectivo*.
3. **Punto de venta → Configuración → Punto de venta**: cree uno, asígnele el método "Efectivo" y deje desactivado el redondeo de efectivo.
4. Marque al menos dos productos con **Disponible en el PdV** y **Se puede vender**, con precio e impuesto de venta.
5. **Ajustes → Usuarios**: cree un usuario de integración con el rol *Punto de venta: Usuario*.
6. Inicie sesión como ese usuario → **Preferencias → Seguridad de la cuenta → Nueva clave API** (ámbito RPC). Copie la clave.
7. Abra una sesión del punto de venta (**Abrir sesión** en el tablero del PdV).
8. Configure el backend:

```powershell
dotnet user-secrets set "Odoo:ApiKey" "<clave>" --project backend/src/Pos.Api
dotnet user-secrets set "Odoo:PosConfigId" "<id del punto de venta>" --project backend/src/Pos.Api
dotnet user-secrets set "Odoo:Database" "<nombre de la base>" --project backend/src/Pos.Api
```

El id del punto de venta aparece en la URL al abrirlo en Odoo (`.../pos.config/<id>`).

## Procedimiento reproducible de la prueba P09 (manual)

1. Ejecute `odoo/setup.ps1`, luego la API (`Pos.Api`, perfil *http*) y el frontend (`npm run dev`).
2. Inicie sesión como `cajero` / `Cajero2026!` en http://localhost:5173.
3. Agregue **dos productos** al ticket y presione **Cobrar**. Verifique que el resumen muestra el total verificado.
4. Elija **Efectivo**, ingrese un monto mayor que el total y presione **Confirmar cobro**.
5. La pantalla muestra **Venta registrada y pagada en Odoo** con la orden (por ejemplo `POS Web .NET - 000001`), el recibo y el cambio.
6. En Odoo: **Punto de venta → Pedidos → Pedidos**. La orden aparece en estado **Pagado**, con las dos líneas, el pago en efectivo y la
   devolución del cambio. La nota interna indica el operador del POS web.
7. En el POS web: **Historial** muestra la misma orden; al abrirla se ven líneas y pagos leídos desde Odoo.

Para guardar la petición y la respuesta JSON-2 exactas, inicie la API con
`Logging__LogLevel__Pos.Infrastructure.Odoo=Debug` (la API key nunca se registra).

## Limitaciones conocidas (se informan al operador, no se ocultan)

| Configuración de Odoo | Comportamiento del POS |
|---|---|
| Redondeo de efectivo activo en el punto de venta | La sesión se marca como no apta para vender con el motivo. |
| Posición fiscal predeterminada | Igual que el anterior (cambiaría los impuestos). |
| Moneda del POS distinta de la de la compañía | Igual que el anterior. |
| Impuestos fijos, de división, de grupo o que afectan la base de otros; mezcla de incluidos y excluidos en un producto | El producto aparece como "no disponible para la venta" con el motivo. |
| Listas de precios con reglas | Se usa el precio de venta del producto (`lst_price`). |
| Métodos de pago con terminal integrada o "cuenta de cliente" | Se muestran deshabilitados. |
| Facturación electrónica, inventario, sucursales, modo sin conexión | Fuera del alcance (sección 12 del enunciado). |

## Solución de problemas

| Síntoma | Causa probable | Qué hacer |
|---|---|---|
| La API no arranca: "Odoo:ApiKey no está configurada" | Falta el secreto | Ejecute `odoo/setup.ps1` o `dotnet user-secrets set ...` |
| `/api/health` responde "Odoo rechazó la API key" | Clave vencida o borrada | Ejecute de nuevo `odoo/setup.ps1` (genera otra). |
| "No hay una sesión POS abierta en Odoo" | Se cerró la sesión | Abra una sesión en Odoo o ejecute de nuevo el script. |
| "El punto de venta X no existe" | `Odoo:PosConfigId` incorrecto | Revise el id que imprime el script. |
| El frontend dice "No se pudo conectar con el servidor del POS" | La API no está en ejecución o en otro puerto | Inicie `Pos.Api` en http://localhost:5080 o defina `POS_API_URL` al ejecutar Vite. |
