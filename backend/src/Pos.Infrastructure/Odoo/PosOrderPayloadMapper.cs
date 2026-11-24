using Pos.Application.Common;
using Pos.Domain.Operators;
using Pos.Domain.Sales;

namespace Pos.Infrastructure.Odoo;

/// <summary>
/// Traduce la venta del dominio al formato que espera <c>pos.order.sync_from_ui</c> en Odoo 20
/// (el mismo que envía el POS web oficial). Validado contra addons/point_of_sale/models/pos_order.py (rama 20.0).
/// </summary>
internal static class PosOrderPayloadMapper
{
    private const int CreateCommand = 0;
    private const int ReplaceCommand = 6;

    public static Dictionary<string, object?> Map(Sale sale, OperatorIdentity registeredBy, int? partnerId = null)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentNullException.ThrowIfNull(registeredBy);

        var payment = sale.Payment ?? throw new InvalidOperationException("Solo se envían a Odoo ventas confirmadas con pago.");
        if (sale.Status != SaleStatus.Confirmed && sale.Status != SaleStatus.Uncertain)
        {
            throw new InvalidOperationException($"No se puede enviar una venta en estado {sale.Status}.");
        }

        var lines = sale.Lines.Select((line, index) => (object)new object[]
        {
            CreateCommand,
            0,
            new Dictionary<string, object?>
            {
                ["uuid"] = LineUuid(sale.Key, index),
                ["product_id"] = line.Product.Id,
                ["full_product_name"] = line.Product.Name,
                ["qty"] = line.Quantity.Value,
                ["price_unit"] = line.Product.UnitPrice,
                ["discount"] = line.Discount.Value,
                ["tax_ids"] = new object[] { new object[] { ReplaceCommand, 0, line.Product.Taxes.Select(t => t.Id).ToArray() } },
                ["price_subtotal"] = line.Amounts.Subtotal,
                ["price_subtotal_incl"] = line.Amounts.Total,
            },
        }).ToArray();

        var payments = new object[]
        {
            new object[]
            {
                CreateCommand,
                0,
                new Dictionary<string, object?>
                {
                    ["uuid"] = PaymentUuid(sale.Key),
                    ["payment_method_id"] = payment.Method.Id,
                    ["amount"] = payment.AmountTendered,
                },
            },
        };

        var order = new Dictionary<string, object?>
        {
            ["uuid"] = sale.Key.ToString(),
            ["session_id"] = sale.Session.Id,
            ["state"] = "paid",
            ["amount_tax"] = sale.Totals.Tax,
            ["amount_total"] = sale.Totals.Total,
            ["amount_paid"] = payment.AmountTendered,
            // Odoo espera el cambio en negativo y crea con él el pago de "devolución" en efectivo.
            ["amount_return"] = -payment.Change,
            ["lines"] = lines,
            ["payment_ids"] = payments,
            ["to_invoice"] = false,
            ["internal_note"] = $"Registrada desde el POS Web (.NET) por {registeredBy.DisplayName} ({registeredBy.Role.ToDisplayName()}).",
        };

        // Consumidor final = orden sin cliente (como en la caja de Odoo); un cliente identificado va como partner_id.
        if (partnerId is { } partner)
        {
            order["partner_id"] = partner;
        }

        return order;
    }

    public static string LineUuid(Guid saleKey, int index) => DeterministicGuid.Create(saleKey, $"line:{index}").ToString();

    public static string PaymentUuid(Guid saleKey) => DeterministicGuid.Create(saleKey, "payment:0").ToString();
}
