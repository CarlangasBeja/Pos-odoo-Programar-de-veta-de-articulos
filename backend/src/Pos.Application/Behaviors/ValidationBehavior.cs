using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Pos.Domain.Common;

namespace Pos.Application.Behaviors;

/// <summary>
/// Pipeline behavior que valida TODA solicitud antes de que llegue a su Handler (patrón Mediator + Chain of
/// Responsibility). Los Handlers no se acuerdan de validar: basta con escribir el Validator de la solicitud.
/// <para>
/// A diferencia del taller (que lanza <c>CustomValidationException</c>), aquí la falla se devuelve como
/// <see cref="Result{T}"/> fallido: es el mismo canal que usan las reglas del dominio y los rechazos de Odoo
/// (decisión D4 del proyecto: las fallas esperadas no son excepciones). La API las traduce a HTTP 400 en un solo lugar.
/// </para>
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : IFailureFactory<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        // Se recibe una lista: las solicitudes sin datos de entrada no tienen Validator y la lista llega vacía.
        var errors = new List<Error>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            errors.AddRange(result.Errors.Select(ToError));
        }

        return errors.Count > 0
            ? TResponse.FromErrors(errors)
            : await next(cancellationToken);
    }

    private static Error ToError(ValidationFailure failure) =>
        Error.Validation(failure.ErrorCode, failure.ErrorMessage, ValidationTargets.FromPropertyName(failure.PropertyName));
}

/// <summary>Convierte el nombre de propiedad de FluentValidation ("Lines[0].ProductId") al nombre del campo JSON ("lines[0].productId").</summary>
public static class ValidationTargets
{
    public static string FromPropertyName(string propertyName)
    {
        ArgumentNullException.ThrowIfNull(propertyName);
        return string.Join('.', propertyName.Split('.').Select(CamelCase));
    }

    private static string CamelCase(string segment) =>
        segment.Length > 0 && char.IsUpper(segment[0]) && segment.All(c => char.IsLetterOrDigit(c) || c is '[' or ']' or '_')
            ? char.ToLowerInvariant(segment[0]) + segment[1..]
            : segment;
}
