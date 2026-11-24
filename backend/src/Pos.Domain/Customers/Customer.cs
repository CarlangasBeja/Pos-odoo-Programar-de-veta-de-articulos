using Pos.Domain.Common;

namespace Pos.Domain.Customers;

/// <summary>
/// Cliente de la venta: "Consumidor final" (sin datos) o una persona identificada con nombre y cédula.
/// Es un value object: se crea válido o no se crea.
/// </summary>
public sealed record Customer
{
    public const int NameMaxLength = 80;
    public const string FinalConsumerName = "Consumidor final";

    public static readonly Customer FinalConsumer = new(FinalConsumerName, null);

    private Customer(string name, string? identification)
    {
        Name = name;
        Identification = identification;
    }

    public string Name { get; }

    /// <summary>Cédula de 10 dígitos; <c>null</c> para el consumidor final.</summary>
    public string? Identification { get; }

    public bool IsFinalConsumer => Identification is null;

    /// <summary>Cliente con nombre y cédula ecuatoriana válida (dígito verificador incluido).</summary>
    public static Result<Customer> Identified(string? name, string? identification)
    {
        var cleanName = string.Join(' ', (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var cleanId = (identification ?? string.Empty).Trim();
        var errors = new List<Error>();

        if (cleanName.Length > NameMaxLength)
        {
            errors.Add(CustomerErrors.NameTooLong());
        }
        else if (!cleanName.All(c => char.IsLetter(c) || c is ' ' or '\'' or '-'))
        {
            errors.Add(CustomerErrors.NameInvalidCharacters());
        }
        else if (cleanName.Split(' ').Count(word => word.Count(char.IsLetter) >= 2) < 2)
        {
            // Nombre y apellido: al menos dos palabras de dos letras o más.
            errors.Add(CustomerErrors.NameRequired());
        }

        if (!Cedula.IsValid(cleanId))
        {
            errors.Add(CustomerErrors.InvalidCedula());
        }

        return errors.Count == 0 ? new Customer(cleanName, cleanId) : Result<Customer>.Failure(errors);
    }
}

/// <summary>Cédula de identidad ecuatoriana: 10 dígitos, provincia 01 a 24 (o 30), tercer dígito menor que 6 y dígito verificador módulo 10.</summary>
public static class Cedula
{
    public const int Length = 10;

    public static bool IsValid(string? value)
    {
        if (value is not { Length: Length } || !value.All(char.IsAsciiDigit))
        {
            return false;
        }

        if (value.Distinct().Count() == 1)
        {
            return false; // 1111111111, 2222222222...: cumplen el verificador pero no son cédulas reales
        }

        var province = (value[0] - '0') * 10 + (value[1] - '0');
        if (province is < 1 or > 24 and not 30 || value[2] - '0' >= 6)
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            var product = (value[i] - '0') * (i % 2 == 0 ? 2 : 1);
            sum += product > 9 ? product - 9 : product;
        }

        return (10 - sum % 10) % 10 == value[9] - '0';
    }
}

public static class CustomerErrors
{
    public const string NameRequiredCode = "CUSTOMER.NAME_REQUIRED";
    public const string NameTooLongCode = "CUSTOMER.NAME_TOO_LONG";
    public const string NameInvalidCharactersCode = "CUSTOMER.NAME_INVALID_CHARACTERS";
    public const string InvalidCedulaCode = "CUSTOMER.INVALID_CEDULA";

    public static Error NameRequired() => Error.Validation(NameRequiredCode, "Escriba nombre y apellido del cliente.", "customer.name");

    public static Error NameInvalidCharacters() => Error.Validation(NameInvalidCharactersCode, "El nombre solo puede tener letras, espacios, guion o apóstrofo.", "customer.name");

    public static Error NameTooLong() => Error.Validation(NameTooLongCode, $"El nombre del cliente admite hasta {Customer.NameMaxLength} caracteres.", "customer.name");

    public static Error InvalidCedula() => Error.Validation(InvalidCedulaCode, "La cédula no es válida: deben ser 10 dígitos con su dígito verificador.", "customer.identification");
}
