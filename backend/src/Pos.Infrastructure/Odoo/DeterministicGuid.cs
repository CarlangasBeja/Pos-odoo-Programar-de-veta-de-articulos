using System.Security.Cryptography;
using System.Text;

namespace Pos.Infrastructure.Odoo;

/// <summary>
/// UUID versión 5 (RFC 9562, basado en nombre). Con la misma clave de venta y el mismo nombre
/// siempre se obtiene el mismo UUID: así un reenvío de la orden actualiza las mismas líneas y pagos
/// en Odoo en lugar de agregar líneas nuevas.
/// </summary>
internal static class DeterministicGuid
{
    public static Guid Create(Guid namespaceId, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var namespaceBytes = namespaceId.ToByteArray(bigEndian: true);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, input, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, input, namespaceBytes.Length, nameBytes.Length);

#pragma warning disable CA5350 // SHA-1 es el algoritmo que exige el estándar para UUID v5; no se usa con fines de seguridad.
        var hash = SHA1.HashData(input);
#pragma warning restore CA5350

        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // versión 5
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // variante RFC
        return new Guid(bytes, bigEndian: true);
    }
}
