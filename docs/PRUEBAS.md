# Pruebas automatizadas y evidencias

## Cómo ejecutarlas

| Suite | Comando (desde la raíz del repositorio) | Necesita Odoo |
|---|---|---|
| Backend: unitarias (Domain, Application, Infrastructure) | `dotnet test backend/tests/Pos.UnitTests` | No |
| Backend: contrato HTTP de la API | `dotnet test backend/tests/Pos.ApiTests` | No |
| Backend: integración real (P09) | `dotnet test backend/tests/Pos.IntegrationTests` | **Sí** (lee los user-secrets que deja `odoo/setup.ps1`) |
| Frontend | `cd frontend && npm test` | No |
| Todo el backend | `dotnet test backend/PosOdoo.slnx` | Las de integración se marcan "ignoradas" si Odoo no está configurado |

En Visual Studio: **Prueba → Explorador de pruebas → Ejecutar todas**.

Para correr solo las pruebas que no necesitan Odoo: `dotnet test backend/PosOdoo.slnx --filter "TestCategory!=OdooReal"`.

## Resultados de la última ejecución (2/11/2026)

| Suite | Resultado | Evidencia |
|---|---|---|
| Backend unitarias | **743 de 743** correctas (incluye 600 vectores de paridad con Odoo 20) | [evidencias/backend-unit-tests.txt](evidencias/backend-unit-tests.txt) |
| Backend API | **18 de 18** correctas | [evidencias/backend-api-tests.txt](evidencias/backend-api-tests.txt) |
| Integración real con Odoo 20.0 (Colombia, COP) | **5 de 5** correctas | [evidencias/backend-integration-odoo-real.txt](evidencias/backend-integration-odoo-real.txt) |
| Integración real con Odoo 20.0 (genérica, USD) | **5 de 5** correctas | mismo archivo |
| Frontend | **646 de 646** correctas (incluye los mismos 600 vectores) | [evidencias/frontend-tests.txt](evidencias/frontend-tests.txt) |
| Venta real de punta a punta | Registrada, pagada y verificada en Odoo; repetición sin duplicado | [evidencias/P09-venta-real-odoo20.md](evidencias/P09-venta-real-odoo20.md) |
| Colección de Postman (Newman) | 19 solicitudes: casos de uso de la API y lecturas JSON-2 de Odoo | [postman/POS-Odoo20.postman_collection.json](postman/POS-Odoo20.postman_collection.json) |

> Estas ejecuciones se hicieron en un entorno sin acceso a nuget.org: las pruebas .NET corrieron con NUnitLite y con sustitutos
> locales de MediatR 14.2.0 y FluentValidation 12.2.0 que tienen la misma API pública. En Visual Studio se ejecutan las mismas pruebas
> con los paquetes reales (se descargan solos al compilar la primera vez).

## Cómo están organizadas las pruebas de Application

Siguen la forma del *Taller de cierre de Application*: cada pieza se prueba donde corresponde.

| Qué se prueba | Cómo | Archivo |
|---|---|---|
| La lógica de cada caso de uso | El Handler se crea con `new` y dobles de prueba (Pure DI), sin contenedor ni Mediator | `RegisterSaleCommandHandlerTests.cs`, `HandlerTests.cs`, `ExtensibilityTests.cs` |
| La forma de cada solicitud | Cada Validator se prueba solo | `ValidatorTests.cs` |
| Que la validación ocurre antes del Handler, el orden de los behaviors y la falta de un Handler | El Mediator armado con el contenedor; la prueba registra el logging, como lo haría el host | `MediatorTests.cs` |
| Que no falte nada en el registro | Recorre el ensamblado de Application (no el de MediatR): Handler por solicitud, respuestas `Result`, solicitudes sin Validator, orden de behaviors | `RegisterApplicationServicesTests.cs` |

## Pruebas mínimas del enunciado (sección 15)

| ID | Comprobación | Pruebas | Archivo |
|---|---|---|---|
| P01 | Rechazar una venta sin productos | `SaleTests.P01_NoSeConfirmaUnaVentaSinProductos`, `RegisterSaleCommandHandlerTests.P01_RechazaUnaVentaSinProductosYNoLaEnvia`, `MediatorTests.Send_LasReglasDelNegocioLasSigueAplicandoElDominio` | `backend/tests/Pos.UnitTests/Domain/SaleTests.cs`, `.../Application/` |
| P02 | Rechazar cantidad cero o negativa | `SaleTests.P02_LaCantidadDebeSerMayorQueCero` (3 casos), `RegisterSaleCommandHandlerTests.P02_RechazaCantidadCeroONegativaYNoLaEnvia`, `cart.test.ts` "rechaza la cantidad inválida", `ui.test.tsx` "una cantidad inválida se rechaza…" | backend y `frontend/tests/domain/cart.test.ts` |
| P03 | Rechazar una sesión POS cerrada o inválida simulando el contrato externo | `RegisterSaleCommandHandlerTests.P03_RechazaUnaSesionCerradaSimulandoElContratoExterno`, `P03_RechazaCuandoNoHaySesionAbierta`, `P03_RechazaUnaVentaPreparadaParaOtraSesion`, `SaleTests.P03_…`, `ApiContractTests.UnaSesionCerradaImpideVenderCon422` | `FakePosSessionGateway` simula el contrato |
| P04 | Rechazar un producto no habilitado y evitar el envío | `RegisterSaleCommandHandlerTests.P04_RechazaUnProductoNoHabilitadoYEvitaElEnvio`, `P04_RechazaUnProductoQueNoExisteEnOdoo`, `SaleTests.P04_…`, `CatalogAndTaxTests.LosImpuestosQueNoSePuedenReplicarDeshabilitanElProducto` | se verifica `SubmitCount == 0` |
| P05 | No declarar éxito ante error o timeout de Odoo | `RegisterSaleCommandHandlerTests.P05_NoDeclaraExitoAnteUnTimeoutDeOdoo`, `P05_NoDeclaraExitoSiOdooRechazaLaVenta`, `P05_NoDeclaraExitoSiLaOrdenQuedaSinPagarEnOdoo`, `OdooPosOrderGatewayTests.P05_*`, `OdooJson2ClientTests.SinRespuestaATiempoEsTimeoutYPudoHaberseProcesado` | |
| P06 | Controlar repetición o devolver resultado incierto sin crear otra venta | `RegisterSaleCommandHandlerTests.P06_UnaSolicitudRepetidaNoCreaOtraVenta`, `P06_DosSolicitudesSimultaneasConLaMismaClaveSoloSeProcesanUnaVez`, `P06_UnReintentoTrasUnResultadoInciertoConciliaSinReenviar`, `P06_SiOdooNoTieneLaVentaInciertaElReintentoUsaLaMismaClave`, `P06_ReutilizarLaClaveConOtraVentaEsUnConflicto`, `ApiContractTests.UnaVentaRegistradaDevuelve201YLaRepeticion200SinDuplicar` | |
| P07 | Frontend: actualizar carrito, cantidades y total provisional | `cart.test.ts` (grupo "Carrito (P07)"), `ui.test.tsx` "Ticket (P07)" | `frontend/tests/` |
| P08 | Frontend: error comprensible cuando falla la API .NET | `apiClient.test.ts` (grupo "Cliente HTTP (P08)"), `checkout.test.ts` "P08: si la API .NET no responde…", `ui.test.tsx` "Inicio de sesión (P08)" | `frontend/tests/` |
| P09 | Integración real: venta de dos productos con efectivo registrada y verificada en Odoo | `OdooIntegrationTests.P09_RegistraEnOdooUnaVentaDeDosProductosEnEfectivoYLaVerifica`, `P09_RepetirLaSolicitudNoCreaUnaSegundaOrdenEnOdoo`, `P09_UnDescuentoAutorizadoSeRegistraConElTotalQueCalculaOdoo` + procedimiento manual. Pasan por el mismo pipeline que la API (`ISender` → behaviors → Handler → JSON-2) | `backend/tests/Pos.IntegrationTests`, `docs/INTEGRACION_ODOO.md` |
| P10 | Cambio controlado en una política o en el mecanismo de integración | `ExtensibilityTests.P10_UnaPoliticaNuevaSeIncorporaSinCambiarElHandler`, `P10_DeshabilitarUnaPoliticaSoloAfectaAEsaPolitica`, `P10_CambiarElMecanismoDeIntegracionNoAlteraLasReglas` | `backend/tests/Pos.UnitTests/Application/ExtensibilityTests.cs` |

## Reglas de negocio cubiertas (CA-06)

| Regla | Dónde se aplica | Pruebas |
|---|---|---|
| RN-01 al menos un producto | `Sale.Confirm`, `SaleComposer` | P01 |
| RN-02 cantidades mayores que cero | `Quantity.Create`, `domain/cart.ts` | P02 |
| RN-03 precios verificados con Odoo | El cliente solo envía `productId` y `quantity`; el precio sale de `IProductCatalog` | `PreviewSaleQueryHandlerTests.CalculaLaVentaConPreciosDeOdooSinRegistrarla`, `OdooPosOrderGatewayTests.ElPayloadSigueElContratoDeSyncFromUi`, `checkout.test.ts` "envía solo productos y cantidades" |
| RN-04 descuentos autorizados | `IDiscountPolicy` por rol | `DiscountPolicyTests`, RF-08 |
| RN-05 no se confirma una venta inconsistente | `Sale.Confirm` (total > 0, pago suficiente, método admitido) | `SaleTests.RN05_…`, `ConTarjetaElMontoDebeSerExacto` |
| RN-06 una venta confirmada no se edita | `SaleStateMachine`, `TicketStore.lock()` | `SaleTests.RN06_…`, `checkout.test.ts` "bloquea el ticket" |
| RN-07 no se informa pago exitoso sin confirmación | Verificación por `uuid` tras `sync_from_ui` | P05 |
| RN-08 una petición repetida no genera otra venta | Idempotency Key + `uuid` único en Odoo | P06, P09 |

Las reglas del negocio no se repiten en los Validators: `ValidatorTests.RegistrarVenta_LasReglasDelNegocioNoSeDuplicanEnElValidator`
comprueba que una venta vacía o con cantidad cero pasa la validación de forma y la rechaza el dominio (una sola fuente de verdad).

## Dobles de prueba

Escritos a mano, sin librerías de mocking, en `backend/tests/Pos.UnitTests/Fakes` (reutilizados por `Pos.ApiTests`):
`FakeProductCatalog`, `FakePosSessionGateway`, `FakePosOrderGateway` (programable: acepta, rechaza, timeout, orden ya creada),
`InMemoryIdempotencyStore`, `FixedTimeProvider` y `FakeHttpHandler` (servidor JSON-2 simulado). Las pruebas unitarias no
reemplazan la integración real (P09), tal como pide el enunciado.

## Paridad de impuestos con Odoo

`odoo/tools/generate_tax_vectors.py` usa el motor real de Odoo 20 (`account.tax`) para calcular 600 ventas aleatorias en dos monedas
(con y sin decimales), con `round_globally` y `round_per_line`, impuestos incluidos y excluidos, varios impuestos por línea,
descuentos y cantidades fraccionarias. El resultado está en `backend/tests/Pos.UnitTests/Domain/Data/odoo20-tax-vectors.json` y lo usan
tanto el backend (`TaxCalculatorOdooParityTests`) como el frontend (`tests/domain/tax.test.ts`). Para regenerarlo:

```powershell
docker compose -f odoo/docker-compose.yml exec -T odoo sh -c "odoo shell -d pos --no-http < /opt/pos-setup/tools/generate_tax_vectors.py" > vectores.txt
```

(y copiar la línea que empieza con `@@VECTORS@@`, sin el prefijo, al archivo JSON).
