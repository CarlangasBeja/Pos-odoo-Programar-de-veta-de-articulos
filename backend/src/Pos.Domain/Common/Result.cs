namespace Pos.Domain.Common;

/// <summary>
/// Tipo de resultado que se puede construir a partir de una lista de errores. Permite que un componente genérico
/// (por ejemplo, el comportamiento de validación del pipeline de Application) devuelva un fallo del tipo exacto que
/// espera quien llamó, sin lanzar excepciones.
/// </summary>
public interface IFailureFactory<TSelf>
    where TSelf : IFailureFactory<TSelf>
{
    static abstract TSelf FromErrors(IReadOnlyList<Error> errors);
}

/// <summary>
/// Resultado de una operación que puede fallar por motivos esperados.
/// Las excepciones se reservan para fallos inesperados (errores de programación o de infraestructura).
/// </summary>
public class Result : IFailureFactory<Result>
{
    private static readonly Result SuccessInstance = new([]);

    protected Result(IReadOnlyList<Error> errors) => Errors = errors;

    public IReadOnlyList<Error> Errors { get; }

    public bool IsSuccess => Errors.Count == 0;

    public bool IsFailure => !IsSuccess;

    public Error FirstError => IsFailure
        ? Errors[0]
        : throw new InvalidOperationException("Un resultado exitoso no tiene errores.");

    public static Result Success() => SuccessInstance;

    public static Result Failure(Error error) => new([error]);

    public static Result Failure(IEnumerable<Error> errors)
    {
        var list = errors.ToArray();
        return list.Length == 0
            ? throw new ArgumentException("Un resultado fallido necesita al menos un error.", nameof(errors))
            : new Result(list);
    }

    /// <summary>Combina varios resultados acumulando todos los errores (patrón Notification).</summary>
    public static Result Combine(IEnumerable<Result> results)
    {
        var errors = results.SelectMany(r => r.Errors).ToArray();
        return errors.Length == 0 ? SuccessInstance : new Result(errors);
    }

    public static implicit operator Result(Error error) => Failure(error);

    static Result IFailureFactory<Result>.FromErrors(IReadOnlyList<Error> errors) => Failure(errors);
}

/// <summary>Resultado con valor. Solo se puede leer <see cref="Value"/> cuando la operación fue exitosa.</summary>
public sealed class Result<T> : Result, IFailureFactory<Result<T>>
{
    private readonly T? _value;

    private Result(T value) : base([]) => _value = value;

    private Result(IReadOnlyList<Error> errors) : base(errors)
    {
    }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("No se puede leer el valor de un resultado fallido.");

    public static Result<T> Ok(T value) => new(value);

    public static new Result<T> Failure(Error error) => new([error]);

    public static new Result<T> Failure(IEnumerable<Error> errors)
    {
        var list = errors.ToArray();
        return list.Length == 0
            ? throw new ArgumentException("Un resultado fallido necesita al menos un error.", nameof(errors))
            : new Result<T>(list);
    }

    public Result<TOut> Map<TOut>(Func<T, TOut> map) =>
        IsSuccess ? Result<TOut>.Ok(map(Value)) : Result<TOut>.Failure(Errors);

    public static implicit operator Result<T>(T value) => Ok(value);

    public static implicit operator Result<T>(Error error) => Failure(error);

    static Result<T> IFailureFactory<Result<T>>.FromErrors(IReadOnlyList<Error> errors) => Failure(errors);
}
