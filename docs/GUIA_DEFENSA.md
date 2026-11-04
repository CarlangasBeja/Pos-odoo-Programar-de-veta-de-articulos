# Guía para la defensa: cambios de requisito y dónde tocar

La defensa incluye un cambio pequeño y controlado de requisitos (sección 17 y CA-08). Esta tabla muestra, para cambios probables,
qué archivos se modifican y cuáles **no** cambian. Que la lista de "no cambia" sea larga es la evidencia de un buen diseño.

| Cambio solicitado | Qué se modifica | Qué NO cambia |
|---|---|---|
| El cajero puede dar hasta 15 % de descuento manual | `appsettings.json` → `Discounts:Manual:MaxPercentByRole:Cashier` | Ningún archivo de código |
| Nueva política: "2 % los martes" | Nueva clase `TuesdayDiscountPolicy : AutomaticDiscountPolicy` en `Pos.Domain/Discounts/Policies`; una línea en `DiscountPolicyRegistration`; su configuración | `SaleComposer`, los Handlers, los controladores, el frontend (la política aparece sola en el diálogo) |
| Desactivar el descuento por volumen | `Discounts:Volume:Enabled = false` | Código |
| Nuevo caso de uso (por ejemplo, "anular una venta") | Carpeta `Features/Sales/Commands/CancelSale/` con `CancelSaleCommand`, `CancelSaleCommandHandler` y `CancelSaleCommandValidator`; una acción en `SalesController` que haga `sender.Send(...)`; el método del contrato en `IPosOrderGateway` y su adaptador | `RegisterApplicationServices` (el escaneo registra Handler y Validator), los behaviors, los demás Handlers |
| Medir cuánto tarda cada solicitud | Un behavior nuevo (`TimingBehavior`) y una línea `AddOpenBehavior` en `RegisterApplicationServices` | Ningún Handler ni controlador (ya existe `LoggingBehavior`, que registra la duración) |
| Volver al Mediator propio de clase (`SimpleMediator`) | `RegisterApplicationServices`, los `using` de los Commands, Queries y Handlers (las interfaces se llaman igual) | La lógica de los Handlers, Domain, Infrastructure, los controladores (siguen usando una interfaz `ISender`/`IMediator`) |
| Nuevo rol "Administrador" con 50 % | Valor en `OperatorRole`, su nombre en `OperatorRoleNames`, límite en configuración | Handlers y políticas |
| Cantidad máxima por línea 500 | `Quantity.Max` (backend) y `MAX_QUANTITY` en `frontend/src/domain/cart.ts` | Todo lo demás; las pruebas de límites lo detectan |
| Guardar las claves de idempotencia en SQL Server | Nuevo adaptador `SqlIdempotencyStore : IIdempotencyStore` y su registro en `DependencyInjection` | Application, Domain, API, frontend |
| Otra versión de Odoo o mecanismo distinto (por ejemplo, XML-RPC) | Adaptadores en `Pos.Infrastructure/Odoo` | Handlers, dominio, API, frontend (demostrado en `ExtensibilityTests.P10_CambiarElMecanismoDeIntegracionNoAlteraLasReglas`) |
| Mostrar el nombre del cliente en la venta | `RegisterSaleCommand` (campo nuevo) y su Validator, `Sale`, `PosOrderPayloadMapper` (`partner_id`) y el formulario del cobro | Políticas de descuento, idempotencia, historial |
| Pago con tarjeta exige número de autorización | `PaymentRequest` y `PaymentRequestValidator`, `Payment.Create` (validación), payload (`payment_ref_no`) y el formulario | Catálogo, descuentos, sesión |
| Cambiar el tiempo máximo de espera de Odoo | `Odoo:SubmitTimeoutSeconds` en configuración | Código |

## Preguntas típicas y dónde está la respuesta

**Sobre la capa Application (indicaciones del docente)**

- **¿Qué estilo usaron en Application y por qué no Services?** CQRS con el patrón Mediator usando MediatR (opción c de las
  indicaciones). La opción Services estaba excluida y además hace crecer una clase por entidad. Ver D8.
- **¿Clean Architecture exige un Mediator?** No. Exige casos de uso que no dependan de lo externo. El Mediator es una forma de
  escribirlos; se eligió porque permite validar y registrar en un solo punto (los behaviors). Usar MediatR no da Clean Architecture
  por sí solo: los Handlers dependen solo de `Contracts/`.
- **¿Qué es un pipeline behavior y en qué orden se ejecutan?** Una clase que envuelve la llamada al Handler (Chain of
  Responsibility). El primero registrado es el más externo: `LoggingBehavior` y luego `ValidationBehavior`. Por eso las solicitudes
  rechazadas también quedan registradas. Ver D8 y `RegisterApplicationServices.cs`.
- **¿Dónde se valida?** La forma de la solicitud, en los Validators (FluentValidation) que ejecuta `ValidationBehavior`. Las reglas
  del negocio (RN-01, RN-02, RN-04, RN-05), en el dominio. `ValidatorTests.RegistrarVenta_LasReglasDelNegocioNoSeDuplicanEnElValidator`
  lo demuestra.
- **¿Por qué el behavior devuelve un `Result` y no lanza `CustomValidationException` como en el taller?** Porque todo el proyecto trata
  las fallas esperadas como `Result` (D4) y la API las traduce a HTTP en un solo lugar. `Result<T>` implementa `IFailureFactory<T>`
  para que el behavior construya el tipo exacto. Ver D8.
- **¿Qué pasa si falta un Handler o un Validator?** Sin Handler, `Send` falla con `InvalidOperationException`, y
  `RegisterApplicationServicesTests` lo detecta antes. Un Validator olvidado no lo detecta nadie por sí solo; por eso una prueba fija la
  lista de solicitudes que pueden no tener Validator.
- **¿Por qué `AddLogging()` no está en Application?** Registrar el logging le corresponde al host (ASP.NET Core en la API; la prueba en
  `MediatorTests`). MediatR 14 lo exige; `MediatorTests.SinLoggingRegistradoElMediatorNoSePuedeUsar` lo deja documentado.
- **¿Y la licencia de MediatR?** Desde la versión 13 es dual (RPL 1.5 o comercial de Lucky Penny Software). El uso educativo está
  permitido; sin clave solo se escribe una advertencia en el registro. Para producción se lee la licencia vigente y la clave se configura
  con `MEDIATR_LICENSE_KEY`.
- **¿Por qué `VerifySaleAttemptCommand` es un Command si el endpoint es GET?** Porque actualiza el registro local del intento. No cambia
  nada en Odoo, así que repetir el GET es seguro.
- **Diferencia entre llamar a un UseCase y enviar con `ISender.Send()`:** con el UseCase el controlador nombra la clase y F12 llega a
  ella; con `Send` el controlador solo conoce la solicitud y el Mediator encuentra el Handler (indirección), pasando antes por los behaviors.

**Sobre el resto del diseño**

- **¿Por qué el frontend no manda el precio?** RN-03: el precio se lee de Odoo en el backend (`SaleComposer.LoadProductsAsync`). Ver D6.
- **¿Qué pasa si se corta la red justo al cobrar?** Resultado incierto: no se informa éxito, el ticket queda bloqueado y se puede
  verificar o reintentar con la misma clave sin duplicar. Ver D3 y D4, y `CheckoutDialog.tsx`.
- **¿Por qué no usaron el patrón State?** Los estados casi no tienen comportamiento propio; una tabla de transiciones es más clara. Ver D5.
- **¿Dónde está la API key?** Solo en el backend, en user-secrets; nunca llega al navegador. Ver `ARQUITECTURA.md` → Seguridad.
- **¿Cómo saben que el total coincide con Odoo?** 600 vectores generados con el motor real de Odoo 20 y ventas reales aceptadas. Ver D6.
- **¿En qué se diferencia de la validación técnica del docente (Postman)?** Allí se crearon orden, líneas y pago por separado y hubo que
  escribir importes a mano. Aquí se usa `pos.order.sync_from_ui`, el mismo método del POS de Odoo, sin escrituras manuales y con control
  de duplicados. Ver `INTEGRACION_ODOO.md` → "Comparación con la validación técnica".
- **¿Dónde está Clean Architecture en el frontend?** `domain/` y `application/` no importan React ni `fetch`; cada caso de uso es una
  clase con `execute`. Ver D7.
- **¿Qué diferencia hay entre patrón, principio y práctica?** Tabla de categorías al inicio de `DECISIONES_DISENO.md`.

## Recorrido sugerido del código (10 minutos)

1. `Pos.Domain/Sales/Sale.cs`: reglas RN-01, 02, 05, 06.
2. `Pos.Application/Features/Sales/Commands/RegisterSale/`: el Command, su Validator (forma) y su Handler (orquestación,
   idempotencia, nada de HTTP ni Odoo).
3. `Pos.Application/Behaviors/` y `RegisterApplicationServices.cs`: el pipeline y el registro por escaneo.
4. `Pos.Api/Controllers/SalesController.cs`: el controlador solo arma el Command y hace `sender.Send(...)`.
5. `Pos.Infrastructure/Odoo/OdooPosOrderGateway.cs`: `sync_from_ui` + verificación.
6. `Pos.Domain/Discounts/IDiscountPolicy.cs` y una política concreta.
7. `backend/tests/Pos.UnitTests/Application/`: `RegisterSaleCommandHandlerTests` (P01–P06), `ValidatorTests`, `MediatorTests`.
8. `frontend/src/application/usecases/checkout.ts` y `CheckoutDialog.tsx`: cómo se ve un resultado incierto.
