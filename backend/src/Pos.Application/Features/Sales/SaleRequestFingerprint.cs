using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Application.Features.Sales;

/// <summary>
/// Huella de la solicitud de venta. Permite detectar que una misma clave de idempotencia
/// se está reutilizando para una venta distinta (lo que sería un error del cliente).
/// </summary>
public static class SaleRequestFingerprint
{
    public static string Compute(ISaleDraft sale, PaymentRequest? payment, CustomerRequest? customer = null)
    {
        ArgumentNullException.ThrowIfNull(sale);

        var builder = new StringBuilder();
        builder.Append("session=").Append(sale.SessionId.ToString(CultureInfo.InvariantCulture));

        var lines = (sale.Lines ?? [])
            .GroupBy(l => l.ProductId)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key}:{g.Sum(l => l.Quantity).ToString("0.######", CultureInfo.InvariantCulture)}");
        builder.Append("|lines=").AppendJoin(',', lines);

        if (sale.Discount is { } discount)
        {
            builder.Append("|discount=")
                .Append(discount.PolicyCode.Trim().ToUpperInvariant())
                .Append(':')
                .Append(discount.Percentage?.ToString("0.######", CultureInfo.InvariantCulture) ?? "-");
        }

        if (payment is not null)
        {
            builder.Append("|payment=")
                .Append(payment.PaymentMethodId.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(payment.AmountTendered.ToString("0.######", CultureInfo.InvariantCulture));
        }

        if (customer is not null)
        {
            builder.Append("|customer=")
                .Append((customer.Identification ?? string.Empty).Trim())
                .Append(':')
                .Append(string.Join(' ', (customer.Name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant());
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash);
    }
}
