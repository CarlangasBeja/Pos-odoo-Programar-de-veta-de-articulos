using FluentValidation;

namespace Pos.Application.Common.Validation;

/// <summary>
/// Reglas de forma que comparten varios Validators (paginación y longitud de búsquedas), para no repetir
/// códigos ni mensajes en cada solicitud.
/// </summary>
public static class CommonRules
{
    public static IRuleBuilderOptions<T, int?> MustBeValidPage<T>(this IRuleBuilder<T, int?> rule) =>
        rule.Must(PageRequest.IsValidPage)
            .WithErrorCode(PageRequest.InvalidPageCode)
            .WithMessage(PageRequest.InvalidPageMessage)
            .OverridePropertyName("page");

    public static IRuleBuilderOptions<T, int?> MustBeValidPageSize<T>(this IRuleBuilder<T, int?> rule) =>
        rule.Must(PageRequest.IsValidPageSize)
            .WithErrorCode(PageRequest.InvalidPageSizeCode)
            .WithMessage(PageRequest.InvalidPageSizeMessage)
            .OverridePropertyName("pageSize");

    /// <summary>El texto (sin espacios a los lados) no supera <paramref name="maxLength"/> caracteres; vacío o nulo es válido.</summary>
    public static IRuleBuilderOptions<T, string?> MustHaveAtMost<T>(this IRuleBuilder<T, string?> rule, int maxLength) =>
        rule.Must(text => text is null || text.Trim().Length <= maxLength);
}
