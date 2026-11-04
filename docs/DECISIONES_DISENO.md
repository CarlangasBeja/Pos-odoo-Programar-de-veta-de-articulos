# Registro de decisiones de diseño

Cada decisión responde a un problema real del POS y sigue el formato pedido en la sección 17 del enunciado:
**(1) problema, (2) alternativas investigadas, (3) solución elegida y su categoría, (4) consecuencias y compromisos,
(5) ubicación en el código y (6) prueba o demostración.**

Categorías usadas (el enunciado pide distinguirlas):

| Categoría | Qué es | Ejemplos en este proyecto |
|---|---|---|
| Enfoque arquitectónico | Cómo se organiza el sistema completo | Clean Architecture, Puertos y Adaptadores, CQRS |
| Patrón GoF | Solución de diseño orientado a objetos (Gamma et al.) | Mediator, Chain of Responsibility, Strategy, Adapter, Template Method, Observer |
| Patrón empresarial / de integración | Soluciones para sistemas de información e integración (Fowler, Hohpe) | Gateway, Anti-Corruption Layer, Idempotency Key, Notification |
| Principio SOLID | Regla para repartir responsabilidades | SRP, OCP, LSP, ISP, DIP |
| Práctica de Clean Code | Hábito de escritura de código | Nombres del dominio, funciones pequeñas, sin números mágicos |

Resumen:

| # | Problema | Solución | Categoría |
|---|---|---|---|
| D1 | Integrarse con Odoo sin acoplar el negocio a su API | Puertos + Adaptadores con Gateway y capa anticorrupción | Arquitectónico + empresarial + GoF Adapter |
| D2 | Políticas de descuento que cambian | Strategy + catálogo (registro) + Template Method | GoF |
| D3 | Una solicitud repetida no debe duplicar la venta | Idempotency Key + `uuid` de Odoo + UUID v5 deterministas | Patrón de integración |
| D4 | No afirmar éxito sin confirmación de Odoo | Resultado tipado (aceptada / rechazada / incierta) + lectura posterior + Result/Notification | Empresarial |
| D5 | Estados de la venta | Máquina de estados con transiciones explícitas (**sin** patrón State) | Alternativa sencilla sin patrón |
| D6 | Totales iguales a los de Odoo | Cálculo en el dominio verificado con vectores del motor real de Odoo | Decisión de dominio + pruebas |
| D7 | Estado del ticket en el frontend sin acoplarlo a React | Observer (almacén propio) + casos de uso en TypeScript puro | GoF Observer + Clean Architecture |
| D8 | Organizar la capa Application sin el estilo Service y validar en un solo punto | CQRS (Commands y Queries) + Mediator con MediatR + pipeline behaviors | GoF Mediator + Chain of Responsibility + CQRS |

---

## D1. Integración con Odoo: Puertos y Adaptadores con Gateway y capa anticorrupción

**1. Problema.** El POS depende de Odoo para productos, precios, impuestos, sesiones y registro de ventas. La API JSON-2 de Odoo
tiene particularidades (`false` en lugar de `null`, many2one como `[id, "nombre"]`, comandos `[0, 0, {...}]`, errores con
`name/message`). Si esos detalles llegan a los casos de uso, cualquier cambio de Odoo (versión, mecanismo) obliga a tocar el negocio,
y las pruebas necesitarían un Odoo real.

**2. Alternativas investigadas.**

| Alternativa | Por qué no |
|---|---|
| Llamar a `HttpClient` desde los controladores | Mezcla HTTP, reglas y Odoo en un solo lugar; imposible de probar sin Odoo (incumple CA-02). |
| Un repositorio genérico `IRepository<T>` | Odoo no es una base de datos de este sistema; un CRUD genérico escondería operaciones de negocio como "registrar venta con pago". |
| Una fachada única `OdooService` con todos los métodos | Crece sin control (clase Dios) y obliga a cada caso de uso a depender de todo (viola ISP). |
| Leer/escribir PostgreSQL de Odoo directamente | Prohibido por el enunciado (sección 14) y se salta las reglas de Odoo. |

**3. Solución elegida.** Application define **puertos** pequeños (la carpeta `Contracts`, como en las prácticas de clase) según lo
que necesita cada caso de uso (`IProductCatalog`, `IPosSessionGateway`, `IPosOrderGateway`). Infrastructure los implementa con **adaptadores** (GoF Adapter) que actúan como
**Gateway** (Fowler) hacia Odoo. `OdooValues` y los mapeadores forman una **capa anticorrupción**: traducen el modelo de Odoo al
modelo del dominio. `OdooJson2Client` encapsula el protocolo JSON-2 y convierte HTTP en resultados (`Success`, `Fault`,
`TransportFailure`) sin excepciones para los casos esperados.

Categoría: enfoque arquitectónico (Clean Architecture / Puertos y Adaptadores) + patrón empresarial Gateway + GoF Adapter.
Principios: DIP (Application depende de abstracciones), ISP (tres puertos pequeños), SRP (cada adaptador traduce un área de Odoo).

**4. Consecuencias y compromisos.**
- (+) Los Handlers de los casos de uso se prueban con dobles escritos a mano, sin Odoo.
- (+) Cambiar el mecanismo de integración solo afecta Infrastructure (demostrado en P10).
- (−) Hay más tipos (puertos, mapeadores, DTO). Se aceptó porque la integración es la parte más volátil del sistema.
- (−) Algunas lecturas se repiten (sesión y precios). Se mitiga con caché corta de datos de referencia (`OdooReferenceData`).

**5. Código.** `backend/src/Pos.Application/Contracts/*`, `backend/src/Pos.Infrastructure/Odoo/*`
(`OdooJson2Client`, `OdooProductCatalog`, `OdooPosSessionGateway`, `OdooPosOrderGateway`, `OdooValues`, `PosOrderPayloadMapper`).

**6. Pruebas.** `OdooJson2ClientTests` (encabezados JSON-2, errores, timeouts), `OdooPosOrderGatewayTests` (contrato de
`sync_from_ui`), `OdooProductCatalogTests` (valores `false`), `ExtensibilityTests.P10_CambiarElMecanismoDeIntegracionNoAlteraLasReglas`
y las pruebas de integración real `OdooIntegrationTests` (P09).

---

## D2. Políticas de descuento variables: Strategy + catálogo + Template Method

**1. Problema.** RF-06/07/08 piden aplicar políticas habilitadas, poder agregar o cambiar políticas sin impacto innecesario y
rechazar descuentos no autorizados con mensajes comprensibles. Cada política tiene reglas propias (manual con tope por rol,
por volumen, por monto mínimo) y el docente puede pedir una nueva en la defensa.

**2. Alternativas investigadas.**

| Alternativa | Evaluación |
|---|---|
| `switch` por código de política dentro del caso de uso | Simple al inicio, pero cada política nueva modifica el caso de uso (viola OCP) y concentra reglas ajenas. |
| Chain of Responsibility (varias políticas encadenadas) | Útil para descuentos acumulables; aquí se aplica **una** política por venta, así que la cadena agrega complejidad sin uso. |
| Specification (condiciones combinables) | Bueno para "¿aplica o no?", pero las políticas también calculan el porcentaje por línea y verifican el rol. |
| Motor de reglas configurable (JSON/BD) | Demasiado para 3 políticas; mueve lógica de negocio a datos difíciles de probar. |

**3. Solución elegida.** **Strategy (GoF)**: cada política implementa `IDiscountPolicy` (`Evaluate`, `IsAvailableFor`,
`MaxPercentageFor`). `DiscountPolicyCatalog` actúa como **registro**: recibe las estrategias por inyección de dependencias y las
resuelve por código. Las políticas automáticas heredan de `AutomaticDiscountPolicy`, que aplica **Template Method** (verifica el rol
y delega la regla concreta a `Decide`). La configuración (qué políticas están activas y sus límites) vive en `appsettings.json`.

Categoría: patrones GoF. Principios: OCP (agregar sin modificar), SRP (una regla por clase), LSP (cualquier política sustituye a otra).

**4. Consecuencias y compromisos.**
- (+) Una política nueva = una clase + una línea de registro + configuración. Ni `SaleComposer`, ni los Handlers, ni los controladores cambian.
- (+) La autorización por rol (RN-04) queda dentro de cada política con mensajes claros (`DiscountErrors`).
- (−) No se combinan políticas en una venta. Es una regla de negocio explícita; si cambiara, Chain of Responsibility sería la evolución.
- (−) El frontend no conoce las reglas automáticas; muestra "se calcula al cobrar" y el backend verifica el resultado.

**5. Código.** `backend/src/Pos.Domain/Discounts/**`, `backend/src/Pos.Application/Features/Discounts/DiscountPolicyCatalog.cs`,
`backend/src/Pos.Api/Composition/DiscountPolicyRegistration.cs`, `frontend/src/domain/discount.ts`.

**6. Pruebas.** `DiscountPolicyTests` (límites por rol, no aplicable), `RegisterSaleCommandHandlerTests.RF08_RechazaUnDescuentoNoAutorizadoSinEnviarLaVenta`,
`ExtensibilityTests.P10_UnaPoliticaNuevaSeIncorporaSinCambiarElHandler`, `ApiContractTests.UnDescuentoNoAutorizadoDevuelve403`
y `OdooIntegrationTests.P09_UnDescuentoAutorizadoSeRegistraConElTotalQueCalculaOdoo` (Odoo acepta el total con descuento).

---

## D3. Solicitudes repetidas sin ventas duplicadas: Idempotency Key + `uuid` de Odoo

**1. Problema.** RN-08 / RF-16: un doble clic, un reintento tras un corte de red o una respuesta que no llegó no deben crear una
segunda venta (ni cobrar dos veces al cliente).

**2. Alternativas investigadas.**

| Alternativa | Evaluación |
|---|---|
| Deshabilitar el botón en el frontend | Necesario pero insuficiente: no cubre reintentos tras timeout ni dos pestañas. |
| Buscar en Odoo una venta "parecida" reciente | Heurística frágil: dos clientes pueden comprar lo mismo. |
| Bloqueo por sesión POS (una venta a la vez) | Evita concurrencia pero no reintentos de la misma venta. |
| Transacción distribuida | Odoo no participa en transacciones externas; JSON-2 confirma cada llamada en su propia transacción. |

**3. Solución elegida.** Patrón **Idempotency Key**: el frontend genera un UUID por intento de cobro y lo envía en el encabezado
`Idempotency-Key`; ese mismo UUID se usa como `uuid` de la `pos.order` en Odoo (que tiene restricción `unique (uuid)` y lo usa para
detectar órdenes existentes en `sync_from_ui`). El backend guarda el resultado de cada clave (`IIdempotencyStore`) con una
**huella** de la solicitud; una repetición devuelve el resultado guardado, una solicitud simultánea recibe 409 y una clave reutilizada
con otra venta recibe 409. Los `uuid` de líneas y pago son **UUID v5 deterministas**, para que un reenvío actualice las mismas
líneas en lugar de agregarlas (comportamiento verificado en el código de `_update_lines` de Odoo 20).

Categoría: patrón de integración empresarial (Idempotent Receiver / Idempotency Key).

**4. Consecuencias y compromisos.**
- (+) Tres barreras independientes: botón deshabilitado, almacén de claves y unicidad en Odoo.
- (+) Tras un resultado incierto, reintentar es seguro (demostrado contra Odoo real: queda 1 orden).
- (−) El almacén usa un archivo JSON pensado para un solo servidor. Para varias instancias se reemplaza el adaptador por uno de base de
  datos sin tocar Application.
- (−) Las claves se conservan 30 días (configurable).

**5. Código.** `frontend/src/application/usecases/checkout.ts` (ciclo de vida de la clave),
`backend/src/Pos.Application/Features/Sales/Commands/RegisterSale/RegisterSaleCommandHandler.cs`, `Features/Sales/SaleRequestFingerprint.cs`,
`backend/src/Pos.Infrastructure/Idempotency/FileIdempotencyStore.cs`, `Odoo/DeterministicGuid.cs`, `Odoo/PosOrderPayloadMapper.cs`.

**6. Pruebas.** `RegisterSaleCommandHandlerTests.P06_*` (repetición, simultaneidad, clave reutilizada, conciliación),
`FileIdempotencyStoreTests`, `OdooPosOrderGatewayTests.RN08_LosUuidDeLineasYPagosSonDeterministas`,
`OdooIntegrationTests.P09_RepetirLaSolicitudNoCreaUnaSegundaOrdenEnOdoo`, `checkout.test.ts` (misma clave en reintentos) y la
evidencia `docs/evidencias/P09-venta-real-odoo20.md`.

---

## D4. No afirmar éxito sin confirmación: resultado tipado y verificación posterior

**1. Problema.** RN-07 / RF-17: con un timeout o un corte de conexión, Odoo pudo o no registrar la venta. Decir "éxito" podría
entregar mercancía sin cobro; decir "error" podría hacer cobrar dos veces.

**2. Alternativas investigadas.**

| Alternativa | Evaluación |
|---|---|
| Excepciones para todo | Las fallas esperadas (validación, rechazo de Odoo) quedan escondidas en `try/catch` y el flujo se vuelve difícil de leer. |
| Reintentos automáticos (Retry) al registrar | Peligroso para escrituras: reenviar a ciegas puede duplicar si no hay idempotencia, y oculta el problema al operador. Se descartó; los reintentos son explícitos y usan la misma clave. |
| Circuit Breaker | Útil con mucho tráfico hacia un servicio inestable; en un POS de una caja no aporta valor proporcional a su complejidad. |
| Confiar en el `200 OK` de `sync_from_ui` | Odoo puede responder bien con una orden en estado `draft`; hay que mirar el estado real. |

**3. Solución elegida.** El puerto devuelve un **resultado tipado** `OrderSubmissionOutcome` = `Accepted` | `Rejected` | `Unknown`.
Tras **cualquier** respuesta (incluso errores o timeouts) el adaptador **lee la orden por `uuid`** antes de concluir. Solo una orden en
estado `paid`/`done` se informa como registrada; si no hay confirmación el estado es `uncertain` (HTTP 202) con instrucciones y un
endpoint para verificar (`GET /api/sales/attempts/{clave}`). Para errores esperados se usa **Result / Notification** (`Result<T>` con
varios `Error` con código, mensaje y campo), traducidos a **Problem Details (RFC 9457)** con mensajes en español en un solo lugar.

La validación de forma del pipeline de Application (D8) usa este mismo canal: devuelve un `Result` fallido, no una excepción.

Categoría: patrones empresariales (Notification, read-after-write). Principio: SRP (el adaptador decide qué pasó; el Handler
decide qué hacer; la API decide el código HTTP).

**4. Consecuencias y compromisos.**
- (+) La interfaz distingue tres finales: registrada (verde), rechazada (rojo, se puede corregir) e incierta (ámbar, no se puede cerrar
  el cobro hasta verificar o reintentar).
- (+) Errores consistentes en toda la API (RNF-08).
- (−) Una lectura extra a Odoo por venta (≈10–20 ms en las pruebas).

**5. Código.** `Pos.Application/Contracts/Gateways/IPosOrderGateway.cs`, `Pos.Application/Features/Sales/SaleAttemptFactory.cs`,
`Features/Sales/Commands/VerifySaleAttempt/VerifySaleAttemptCommandHandler.cs`,
`Pos.Infrastructure/Odoo/OdooPosOrderGateway.cs`, `Pos.Api/Http/ErrorResults.cs`, `frontend/src/infrastructure/http/apiClient.ts`,
`frontend/src/presentation/features/checkout/CheckoutDialog.tsx`.

**6. Pruebas.** `RegisterSaleCommandHandlerTests.P05_*`, `OdooPosOrderGatewayTests.P05_*`, `VerifySaleAttemptCommandHandlerTests`,
`ApiContractTests.UnResultadoInciertoDevuelve202ConLaRutaParaVerificar`, `apiClient.test.ts` y `checkout.test.ts` (P08).

---

## D5. Estados de la venta: máquina de estados explícita, sin patrón State

**1. Problema.** RN-05/RN-06: una venta pasa por preparación → confirmada → registrada / rechazada / incierta, y una venta confirmada
no se edita como venta en preparación.

**2. Alternativas.** (a) Patrón State de GoF (una clase por estado); (b) banderas booleanas sueltas (`isConfirmed`, `isPaid`);
(c) enumeración + tabla de transiciones permitidas.

**3. Solución elegida.** (c): `SaleStatus` + `SaleStateMachine` con la tabla de transiciones; los métodos del agregado `Sale` verifican
el estado antes de modificar. **El patrón State no aporta valor aquí**: los estados casi no tienen comportamiento propio (solo
"¿se puede editar?" y "¿a qué estado se puede pasar?"); cinco clases de estado agregarían indirección sin eliminar condicionales
relevantes. Las banderas se descartaron porque permiten combinaciones imposibles. Si en el futuro cada estado tuviera
comportamiento distinto (por ejemplo, devoluciones o anulaciones con reglas propias), se migraría a State.

Categoría: alternativa sencilla sin patrón adicional (el enunciado pide explicar cuándo un patrón no aporta valor).

**4. Consecuencias.** (+) Toda la lógica de transición se lee en una tabla. (−) Si crecen los comportamientos por estado, habría que
refactorizar.

**5. Código.** `backend/src/Pos.Domain/Sales/SaleStatus.cs`, `Sale.cs`; en el frontend `TicketStore.lock()/unlock()`.

**6. Pruebas.** `SaleTests.RN06_UnaVentaConfirmadaNoSeEdita`, `SaleTests.UnaVentaRegistradaNoVuelveAEstadoIncierto`,
`checkout.test.ts` ("bloquea el ticket mientras se cobra").

---

## D6. Totales idénticos a los de Odoo: cálculo en el dominio verificado contra el motor real

**1. Problema.** RF-05 y RN-03. Al revisar `pos_order.py` de la rama 20.0 se encontró que `sync_from_ui` **recalcula el total**
(`_compute_prices`) y exige que el pago coincida exactamente (`action_pos_order_paid` lanza "no se pagó por completo").
Además, en Odoo 20 la compañía usa por defecto `round_globally`, que distribuye diferencias de redondeo entre líneas.

**2. Alternativas.** (a) Usar el total del frontend: viola RN-03; (b) llamar a `account.tax.compute_all` por línea: no reproduce el
redondeo global del documento; (c) registrar sin pago y pagar después: `sync_from_ui` registra orden y pago juntos y el POS de Odoo
no ofrece un método público para cotizar; (d) replicar el cálculo en el dominio y **demostrar** que coincide.

**3. Solución elegida.** (d): `TaxCalculator` replica `_get_tax_details`, `_round_base_lines_tax_details` y
`_get_tax_totals_summary` para los casos admitidos (impuestos porcentuales, todos excluidos o uno incluido). Los productos con
impuestos que no se pueden replicar se marcan como no vendibles con un mensaje (P04), en lugar de arriesgar un total distinto.
Se generaron **600 vectores con el motor real de Odoo 20** (`odoo/tools/generate_tax_vectors.py`) en USD y en una moneda sin
decimales, con ambos modos de redondeo, varios impuestos, descuentos y cantidades fraccionarias.

**4. Consecuencias.** (+) Total verificado antes de cobrar; Odoo nunca rechaza por diferencia. (+) El mismo algoritmo en TypeScript
para el total provisional del frontend, probado con los mismos vectores. (−) Si Odoo cambia su motor, hay que regenerar los vectores
(la prueba avisaría). (−) No se soportan redondeo de efectivo, posición fiscal por defecto ni multimoneda: la sesión los detecta y
lo informa (`UnsupportedSettings`).

**5. Código.** `Pos.Domain/Taxes/TaxCalculator.cs`, `frontend/src/domain/tax.ts`, `odoo/tools/generate_tax_vectors.py`.

**6. Pruebas.** `TaxCalculatorOdooParityTests` (600 casos), `frontend/tests/domain/tax.test.ts` (los mismos 600 casos) y las ventas
reales de P09 (Odoo aceptó cada total calculado por el backend, con y sin descuento, en USD y COP).

---

## D7. Frontend: estado del ticket con Observer y casos de uso independientes de React

**1. Problema.** RNF-02 exige que los casos de uso del frontend no dependan del framework ni de HTTP. El ticket se usa en varias
partes (catálogo, panel, hoja móvil, cobro) y debe bloquearse durante el cobro.

**2. Alternativas.** (a) `useState` + Context: el estado y las reglas quedan dentro de componentes React; (b) Redux / Zustand:
dependencia extra para un estado pequeño; (c) almacén propio en TypeScript con suscripción (**Observer**) consumido con
`useSyncExternalStore`.

**3. Solución elegida.** (c). `TicketStore` (application) notifica cambios a los suscriptores; las reglas del carrito están en
`domain/cart.ts` (funciones puras e inmutables); los casos de uso son **una clase por operación con un solo método `execute`**
(`LoginUseCase`, `SearchProductsUseCase`, `PreviewTicketUseCase`, `StartCheckoutUseCase`...; ninguno agrupa operaciones al estilo
*Service*) y dependen de **puertos** (`ports.ts`), implementados con `fetch` en `infrastructure/http`. `main.tsx` es la raíz de
composición y `createServices` conecta cada caso de uso con sus puertos.

**4. Consecuencias.** (+) Dominio y casos de uso se prueban sin navegador ni red. (+) Cambiar React por otro framework solo afecta
`presentation/`. (−) Un poco más de código de conexión (contextos y hooks).

**5. Código.** `frontend/src/application/cartStore.ts`, `frontend/src/application/usecases/*`, `frontend/src/presentation/hooks/hooks.ts`.

**6. Pruebas.** `frontend/tests/domain/cart.test.ts` (P07), `tests/application/checkout.test.ts`, `tests/presentation/ui.test.tsx`.

---

## D8. Capa Application: CQRS con el patrón Mediator (MediatR) y pipeline behaviors

**1. Problema.** Las indicaciones del docente piden escribir la capa Application con casos de uso independientes de lo externo
y prohíben el estilo *Service* (una clase por entidad con varios métodos). Además, todos los casos de uso necesitan lo mismo
antes de ejecutarse (validar la forma de la solicitud) y alrededor (dejar registro de qué pasó). En un *Service* o en un
*UseCase* cada clase debe acordarse de hacerlo, y nada avisa si lo olvida. Los controladores también crecían: recibían un caso de
uso por cada acción.

**2. Alternativas investigadas** (las tres del *Taller de cierre de Application*).

| Alternativa | Evaluación |
|---|---|
| *Service Layer* (Fowler): `SalesService` con `RegisterAsync`, `PreviewAsync`... | Excluida por las indicaciones del trabajo final. Además crece con cada operación y cada método debe acordarse de validar. |
| *Use Case / Interactor* (Martin): una clase por caso de uso con `ExecuteAsync` | Válida y sin dependencias (fue la primera versión de este proyecto), pero la validación y el registro se repetían en cada clase y cada controlador recibía varias dependencias. |
| CQRS + Mediator propio (`SimpleMediator` de las prácticas) | Muestra el mecanismo en pocas líneas, pero habría que mantener código propio de reflexión y registro, y agregar a mano un mecanismo de *behaviors*. |
| **CQRS + MediatR** | Elegida: el mismo estilo que el Mediator propio, con *pipeline behaviors* listos para usar y registro por escaneo. |

**3. Solución elegida.** Cada caso de uso es un **Command** (cambia algo) o una **Query** (solo lee) con su **Handler**
(`IRequestHandler<,>`), en `Features/<Área>/Commands|Queries/<Caso>/` junto a su **Validator** (FluentValidation). Los controladores
dependen solo de `ISender` y no nombran ningún Handler. Dos **pipeline behaviors** envuelven a todos los Handlers:
`LoggingBehavior` (nombre, duración y resultado de cada solicitud, nunca su contenido) y `ValidationBehavior` (ejecuta los
Validators de la solicitud y corta la cadena si fallan). `RegisterApplicationServices` registra todo por escaneo
(`AddMediatR` con `Lifetime = Scoped` y `AddValidatorsFromAssembly`), igual que en el taller.

| Commands (cambian algo) | Queries (solo leen) |
|---|---|
| `LoginCommand`, `RegisterSaleCommand`, `VerifySaleAttemptCommand` | `SearchProductsQuery`, `GetProductCategoriesQuery`, `GetActiveSessionQuery`, `GetDiscountPoliciesQuery`, `PreviewSaleQuery`, `GetSalesHistoryQuery`, `GetSaleDetailQuery` |

Diferencias con el taller, elegidas a propósito:

- `ValidationBehavior` **devuelve un `Result` fallido** en lugar de lanzar `CustomValidationException`. Es coherente con D4 (las
  fallas esperadas no son excepciones) y la API las traduce a HTTP 400 en el mismo lugar que el resto de los errores
  (`ErrorResults`), con el mismo formato. Para construir el `Result<T>` exacto que espera cada Handler, `Result` y `Result<T>`
  implementan `IFailureFactory<TSelf>` (miembro estático abstracto de C# 11) y el behavior se restringe a
  `TResponse : IFailureFactory<TResponse>`. Una prueba exige que toda respuesta sea un `Result`.
- Los Validators revisan la **forma** (campos presentes, identificadores, longitudes, paginación, clave de idempotencia). Las
  reglas del **negocio** (RN-01, RN-02, RN-04, RN-05) siguen en el dominio: hay una sola fuente de verdad y el dominio sigue
  protegiéndose aunque alguien llame al Handler sin pasar por el Mediator.
- `VerifySaleAttemptCommand` es un Command aunque el endpoint sea `GET`: actualiza el registro local del intento. Nunca cambia nada
  en Odoo, por eso el `GET` sigue siendo seguro de repetir.

Categoría: patrón GoF **Mediator** (variante orientada a solicitudes) + **Chain of Responsibility** (los behaviors) + estilo
**CQRS** a nivel de solicitudes (separa lectura y escritura; la fuente de datos es la misma, Odoo). Principios: SRP (un Handler por
caso de uso), OCP (un behavior nuevo no toca ningún Handler), DIP (los controladores dependen de `ISender`).

**4. Consecuencias y compromisos.**
- (+) Validación y registro en un solo punto. Un caso de uso nuevo es su Command o Query, su Handler y su Validator; no se toca el
  registro ni ningún behavior.
- (+) Controladores delgados con una sola dependencia (`ISender`).
- (+) Los Handlers se prueban con Pure DI (`new` + dobles); el pipeline se prueba con el contenedor (`MediatorTests`).
- (−) Indirección: F12 desde el controlador llega a `ISender.Send`, no al Handler. Se mitiga con nombres y carpetas consistentes
  (`RegisterSaleCommand` y `RegisterSaleCommandHandler` viven juntos).
- (−) Dependencias externas en Application: MediatR 14.2.0 y FluentValidation 12.2.0, con versiones fijadas como en el taller. Desde la
  versión 13, MediatR tiene **licencia dual** (RPL 1.5 o comercial de Lucky Penny Software). El uso educativo está permitido y sin clave
  solo escribe una advertencia en el registro. Si se tiene una clave, se configura con la variable de entorno `MEDIATR_LICENSE_KEY`.
  Domain sigue sin ninguna dependencia.
- (−) MediatR exige `ILoggerFactory`. Lo registra el host (ASP.NET Core) y, en las pruebas, la propia prueba; Application no registra
  logging (decisión D5 del taller).
- (−) Ninguna herramienta detecta un Validator olvidado (la "lista vacía ambigua" del taller). Se mitiga con
  `RegisterApplicationServicesTests.SoloLasSolicitudesSinDatosDelClienteNoTienenValidator`, que fija qué solicitudes pueden no tenerlo.
- Usar MediatR no da Clean Architecture por sí solo: los Handlers dependen únicamente de contratos de Application (`Contracts/`), nunca
  de Odoo, de HTTP ni del contenedor.

**Orden de los behaviors** (pregunta de la tarea del taller). `LoggingBehavior` se registra primero, así que es el más externo: mide y
registra también las solicitudes que la validación rechaza, lo que sirve para detectar un cliente que envía datos mal formados. Las
pruebas de resultado no detectan un cambio de orden, porque el resultado es el mismo. Por eso `RegisterApplicationServicesTests` fija el
orden del registro y `MediatorTests` comprueba que la solicitud rechazada sí queda en el registro.

**5. Código.** `backend/src/Pos.Application/Features/**`, `Behaviors/ValidationBehavior.cs`, `Behaviors/LoggingBehavior.cs`,
`RegisterApplicationServices.cs`, `Pos.Domain/Common/Result.cs` (`IFailureFactory`), `Pos.Api/Controllers/*` (`ISender`).

**6. Pruebas.** `ValidatorTests` (cada Validator, sin contenedor); `MediatorTests` (un Command mal formado no llega al Handler ni a
Odoo, las reglas del negocio las sigue aplicando el dominio, una solicitud sin Handler falla con `InvalidOperationException`, el
logging registra también las rechazadas y nunca la contraseña); `RegisterApplicationServicesTests` (completitud: un Handler por
solicitud, respuestas `Result`, solicitudes sin Validator, orden de behaviors);
`ApiContractTests.ElValidatorDelPipelineRechazaLaFormaConElCampoDelJson`; y la integración real P09, que ahora pasa por el mismo
pipeline que la API.

---

## Antipatrones evitados (y cómo)

| Antipatrón | Cómo se evitó |
|---|---|
| Controlador gordo | Los controladores solo traducen HTTP ↔ Command/Query y dependen solo de `ISender`; las reglas están en Domain/Application (CA-02). |
| Capa de *Services* que crece (excluida por las indicaciones) | Un Command o Query con su Handler por caso de uso (backend) y una clase con `execute` por operación (frontend). Ninguna clase agrupa las operaciones de una entidad. |
| Validación olvidada o repetida | `ValidationBehavior` valida toda solicitud en un solo punto; una prueba fija qué solicitudes no tienen Validator. |
| Clase Dios / `OdooService` | Un adaptador por área de Odoo y un cliente JSON-2 pequeño. |
| Modelo anémico | `Sale`, `Quantity`, `DiscountPercentage`, `Payment` y `PosSession` validan sus propias reglas. |
| Obsesión por primitivos | Value objects para cantidad, porcentaje, moneda e impuestos. |
| Números y cadenas mágicas | Códigos de error en constantes (`SaleErrors`, `PaymentErrors`...), límites con nombre (`Quantity.Max`). |
| Tragarse excepciones | Las fallas esperadas son `Result`; las inesperadas llegan a `UnexpectedExceptionHandler`, se registran y se responde 500 con `traceId`. |
| Secretos en el repositorio | API key en user-secrets o variables de entorno; validación al iniciar con mensaje claro; `.gitignore` cubre `.env`. |
| Service Locator | Solo inyección por constructor; una raíz de composición por aplicación. El único que resuelve servicios por tipo es el Mediator, al buscar el Handler de una solicitud; ningún código del POS llama a `GetService`. |
| Dependencia cautiva | El cliente HTTP tipado es transitorio y lo crea `IHttpClientFactory`; la caché es un servicio aparte. |
| Copiar y pegar reglas | `SaleComposer` comparte la construcción de la venta entre vista previa y registro; `SaleDraftValidator` comparte sus reglas de forma. |
| Reintentos ciegos en escrituras | Los reintentos son explícitos y reutilizan la clave de idempotencia (D3, D4). |

## Principios SOLID aplicados (con ejemplos concretos)

- **SRP**: un Handler por caso de uso; `OdooJson2Client` solo habla JSON-2; `PosOrderPayloadMapper` solo arma el payload; `ErrorResults` solo traduce errores a HTTP.
- **OCP**: políticas de descuento nuevas sin modificar el Handler (D2, P10); un comportamiento transversal nuevo (por ejemplo, medir tiempos) es un behavior más y una línea de registro, sin tocar ningún Handler (D8).
- **LSP**: cualquier `IDiscountPolicy` o cualquier `IPosOrderGateway` (real o doble) es intercambiable; las pruebas lo hacen.
- **ISP**: puertos separados para catálogo, sesión, órdenes, idempotencia y operadores en lugar de una interfaz enorme.
- **DIP**: Application define las interfaces (`Contracts`); Infrastructure las implementa; los controladores dependen de `ISender`; Domain no depende de nada.
