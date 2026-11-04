using Pos.Application.Common;
using Pos.Application.Features.Catalog;
using Pos.Domain.Catalog;
using Pos.Domain.Common;

namespace Pos.Application.Contracts.Gateways;

/// <summary>Contrato (puerto de salida) hacia el catálogo de productos del ERP; lo implementa un adaptador de Odoo en Infrastructure.</summary>
public interface IProductCatalog
{
    Task<Result<PagedResult<Product>>> SearchAsync(ProductSearchCriteria criteria, CancellationToken cancellationToken);

    /// <summary>Devuelve los productos encontrados; los identificadores inexistentes simplemente no aparecen.</summary>
    Task<Result<IReadOnlyList<Product>>> GetByIdsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ProductCategory>>> GetCategoriesAsync(CancellationToken cancellationToken);
}
