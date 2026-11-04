using Pos.Domain.Operators;

namespace Pos.Application.Common;

/// <summary>Operador autenticado que ejecuta el caso de uso.</summary>
public sealed record OperatorIdentity(string Username, string DisplayName, OperatorRole Role);
