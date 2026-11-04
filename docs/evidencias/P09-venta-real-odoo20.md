# Evidencia P09: venta real registrada en Odoo 20 desde el frontend → API .NET → Odoo

Ejecución real del 29/10/2026 contra **Odoo 20.0 Community** (compañía en Colombia, moneda COP, localización `l10n_co`,
redondeo de impuestos `round_globally`), con la API .NET de este repositorio. Se registra una venta de **dos productos con pago en efectivo**
y se verifica directamente en Odoo. Ningún dato es simulado.

Para reproducirla: ejecute `odoo/setup.ps1`, inicie la API con `Logging:LogLevel:Pos.Infrastructure.Odoo = Debug`
(registra el cuerpo de cada llamada JSON-2; la API key va en el encabezado y nunca se registra) y cobre una venta desde el frontend
o con `backend/src/Pos.Api/Pos.Api.http`.

## 1. Petición del frontend a la API .NET

```http
POST /api/sales
Authorization: Bearer <token del operador "cajero">
Content-Type: application/json
Idempotency-Key: cb06c9b4-a6ca-4623-ba24-f3ffcecbea37

{
  "sessionId": 1,
  "lines": [
    {
      "productId": 3,
      "quantity": 2
    },
    {
      "productId": 10,
      "quantity": 1
    }
  ],
  "discount": null,
  "payment": {
    "paymentMethodId": 1,
    "amountTendered": 20000
  }
}
```

El cliente **no envía precios** (RN-03): solo productos, cantidades, descuento elegido y pago.

## 2. Llamada de la API .NET a Odoo (JSON-2)

`POST http://localhost:8070/json/2/pos.order/sync_from_ui` con `Authorization: bearer <API key>` y `X-Odoo-Database: posco`.
Es el mismo método que usa el POS web oficial de Odoo 20. El `uuid` de la orden es la clave de idempotencia; los `uuid` de líneas y
pago son deterministas (UUID v5), de modo que un reenvío actualiza la misma orden en lugar de crear otra.

```json
{
  "orders": [
    {
      "uuid": "cb06c9b4-a6ca-4623-ba24-f3ffcecbea37",
      "session_id": 1,
      "state": "paid",
      "amount_tax": 1710.0,
      "amount_total": 13210.0,
      "amount_paid": 20000,
      "amount_return": -6790.0,
      "lines": [
        [
          0,
          0,
          {
            "uuid": "da5cac3a-54e3-5950-90f2-00b5edc8ca29",
            "product_id": 3,
            "full_product_name": "Café americano 12 oz",
            "qty": 2,
            "price_unit": 4500.0,
            "discount": 0,
            "tax_ids": [
              [
                6,
                0,
                [
                  72
                ]
              ]
            ],
            "price_subtotal": 9000.0,
            "price_subtotal_incl": 10710.0
          }
        ],
        [
          0,
          0,
          {
            "uuid": "c5c557fe-4f94-5a1f-b7e6-d2f962ecccd3",
            "product_id": 10,
            "full_product_name": "Pan de bono",
            "qty": 1,
            "price_unit": 2500.0,
            "discount": 0,
            "tax_ids": [
              [
                6,
                0,
                []
              ]
            ],
            "price_subtotal": 2500.0,
            "price_subtotal_incl": 2500.0
          }
        ]
      ],
      "payment_ids": [
        [
          0,
          0,
          {
            "uuid": "3c52676c-5736-5959-bc1a-4f763ca7e4f9",
            "payment_method_id": 1,
            "amount": 20000
          }
        ]
      ],
      "to_invoice": false,
      "internal_note": "Registrada desde el POS Web (.NET) por Ana Cajera (Cajero)."
    }
  ],
  "context": {
    "lang": "es_419"
  }
}
```

Respuesta de Odoo: **HTTP 200** (extracto de `pos.order`):

```json
{
  "pos.order": [
    {
      "id": 8,
      "name": "POS Web .NET - 000008",
      "pos_reference": "260-1-000008",
      "state": "paid",
      "amount_total": 13210.0,
      "amount_paid": 13210.0,
      "amount_return": 6790.0,
      "uuid": "cb06c9b4-a6ca-4623-ba24-f3ffcecbea37"
    }
  ]
}
```

Inmediatamente después, la API **lee la orden de vuelta** para no afirmar éxito sin confirmación (RN-07):

```json
{
  "domain": [
    [
      "uuid",
      "=",
      "cb06c9b4-a6ca-4623-ba24-f3ffcecbea37"
    ]
  ],
  "fields": [
    "id",
    "name",
    "pos_reference",
    "date_order",
    "state",
    "amount_total",
    "amount_paid",
    "amount_return",
    "session_id",
    "internal_note"
  ],
  "limit": 1,
  "context": {
    "lang": "es_419"
  }
}
```

Llamadas a Odoo registradas por la API durante la operación (incluye las consultas previas de sesión, productos e impuestos):

| Modelo.método | HTTP | Duración |
|---|---|---|
| `pos.config.search_count` | 200 | 56 ms |
| `pos.config.search_read` | 200 | 15 ms |
| `pos.session.search_read` | 200 | 13 ms |
| `res.company.read` | 200 | 12 ms |
| `res.currency.read` | 200 | 10 ms |
| `pos.payment.method.read` | 200 | 9 ms |
| `product.product.fields_get` | 200 | 9 ms |
| `product.product.search_read` | 200 | 27 ms |
| `account.tax.read` | 200 | 14 ms |
| `pos.category.search_read` | 200 | 10 ms |
| `pos.order.sync_from_ui` | 200 | 91 ms |
| `pos.order.search_read` | 200 | 12 ms |

## 3. Respuesta de la API .NET al frontend

```http
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Date: Thu, 29 Oct 2026 22:21:35 GMT
Server: Kestrel
Cache-Control: no-store
Location: http://localhost:5083/api/sales/8
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: no-referrer

{
  "saleKey": "cb06c9b4-a6ca-4623-ba24-f3ffcecbea37",
  "status": "registered",
  "message": "Venta registrada y pagada en Odoo (POS Web .NET - 000008).",
  "orderId": 8,
  "orderName": "POS Web .NET - 000008",
  "posReference": "260-1-000008",
  "orderState": "paid",
  "total": 13210.0,
  "amountTendered": 20000,
  "change": 6790.0,
  "paymentMethodName": "Efectivo",
  "paymentMethodType": "cash",
  "processedAt": "2026-10-29T22:21:36.5226598+00:00",
  "replayed": false
}
```

## 4. Repetición de la MISMA solicitud (RN-08 / P06)

Misma `Idempotency-Key` y mismo cuerpo: la API devuelve el resultado guardado, **sin volver a llamar a Odoo**.

```http
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Date: Thu, 29 Oct 2026 22:21:35 GMT
Server: Kestrel
Cache-Control: no-store
Transfer-Encoding: chunked
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: no-referrer

{
  "saleKey": "cb06c9b4-a6ca-4623-ba24-f3ffcecbea37",
  "status": "registered",
  "message": "Venta registrada y pagada en Odoo (POS Web .NET - 000008).",
  "orderId": 8,
  "orderName": "POS Web .NET - 000008",
  "posReference": "260-1-000008",
  "orderState": "paid",
  "total": 13210.0,
  "amountTendered": 20000,
  "change": 6790.0,
  "paymentMethodName": "Efectivo",
  "paymentMethodType": "cash",
  "processedAt": "2026-10-29T22:21:36.5226598+00:00",
  "replayed": true
}
```

## 5. Registro observado en Odoo (consulta independiente por JSON-2)

```json
{
  "pos.order": [
    {
      "id": 8,
      "name": "POS Web .NET - 000008",
      "pos_reference": "260-1-000008",
      "state": "paid",
      "amount_total": 13210.0,
      "amount_tax": 1710.0,
      "amount_paid": 13210.0,
      "amount_return": 6790.0,
      "session_id": [
        1,
        "POS Web .NET/00001"
      ],
      "uuid": "cb06c9b4-a6ca-4623-ba24-f3ffcecbea37",
      "internal_note": "Registrada desde el POS Web (.NET) por Ana Cajera (Cajero).",
      "date_order": "2026-10-29 22:21:36"
    }
  ],
  "pos.order.line": [
    {
      "id": 14,
      "full_product_name": "Café americano 12 oz",
      "qty": 2.0,
      "price_unit": 4500.0,
      "discount": 0.0,
      "price_subtotal": 9000.0,
      "price_subtotal_incl": 10710.0,
      "uuid": "da5cac3a-54e3-5950-90f2-00b5edc8ca29"
    },
    {
      "id": 15,
      "full_product_name": "Pan de bono",
      "qty": 1.0,
      "price_unit": 2500.0,
      "discount": 0.0,
      "price_subtotal": 2500.0,
      "price_subtotal_incl": 2500.0,
      "uuid": "c5c557fe-4f94-5a1f-b7e6-d2f962ecccd3"
    }
  ],
  "pos.payment": [
    {
      "id": 16,
      "payment_method_id": [
        1,
        "Efectivo"
      ],
      "amount": -6790.0,
      "is_change": true,
      "uuid": "1055abe4-5986-4513-be02-16750c57375e"
    },
    {
      "id": 15,
      "payment_method_id": [
        1,
        "Efectivo"
      ],
      "amount": 20000.0,
      "is_change": false,
      "uuid": "3c52676c-5736-5959-bc1a-4f763ca7e4f9"
    }
  ],
  "ordenes_con_este_uuid": 1
}

```

`ordenes_con_este_uuid = 1`: la repetición no creó una segunda venta. El cambio entregado quedó registrado como un pago de
devolución (`is_change: true`) y el total pagado coincide exactamente con el total calculado por el backend.
