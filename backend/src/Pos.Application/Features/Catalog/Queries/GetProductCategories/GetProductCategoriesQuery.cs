using MediatR;
using Pos.Domain.Common;

namespace Pos.Application.Features.Catalog.Queries.GetProductCategories;

/// <summary>Categorías del punto de venta para los filtros del catálogo. No recibe datos del cliente: no tiene Validator.</summary>
public sealed record GetProductCategoriesQuery : IRequest<Result<IReadOnlyList<CategoryView>>>;
