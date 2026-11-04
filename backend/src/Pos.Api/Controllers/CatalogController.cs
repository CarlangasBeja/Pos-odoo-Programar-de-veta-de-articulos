using MediatR;
using Microsoft.AspNetCore.Mvc;
using Pos.Api.Http;
using Pos.Application.Common;
using Pos.Application.Features.Catalog;
using Pos.Application.Features.Catalog.Queries.GetProductCategories;
using Pos.Application.Features.Catalog.Queries.SearchProducts;
using Pos.Application.Features.Discounts;
using Pos.Application.Features.Discounts.Queries.GetDiscountPolicies;
using Pos.Application.Features.Sessions;
using Pos.Application.Features.Sessions.Queries.GetActiveSession;

namespace Pos.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(ISender sender) : ControllerBase
{
    /// <summary>RF-01 / RF-02: catálogo de Odoo con búsqueda por nombre, código o código de barras.</summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductView>>> Search(
        [FromQuery] string? search,
        [FromQuery] int? categoryId,
        [FromQuery] bool onlyInStock,
        [FromQuery] ProductSort sort,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new SearchProductsQuery(search, categoryId, onlyInStock, sort, page, pageSize),
            cancellationToken);
        return result.IsSuccess ? result.Value : (ActionResult)result.ToProblem(HttpContext);
    }

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<CategoryView>>> Categories(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetProductCategoriesQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : (ActionResult)result.ToProblem(HttpContext);
    }
}

[ApiController]
[Route("api/pos/sessions")]
public sealed class PosSessionsController(ISender sender) : ControllerBase
{
    /// <summary>Sesión POS vigente en Odoo, sus métodos de pago y si permite vender.</summary>
    [HttpGet("active")]
    public async Task<ActionResult<SessionView>> Active(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetActiveSessionQuery(), cancellationToken);
        return result.IsSuccess ? result.Value : (ActionResult)result.ToProblem(HttpContext);
    }
}

[ApiController]
[Route("api/discounts")]
public sealed class DiscountsController(ISender sender) : ControllerBase
{
    /// <summary>RF-06: políticas de descuento habilitadas para el rol del operador autenticado.</summary>
    [HttpGet("policies")]
    public async Task<ActionResult<IReadOnlyList<DiscountPolicyView>>> Policies(CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetDiscountPoliciesQuery(User.ToOperator()), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : (ActionResult)result.ToProblem(HttpContext);
    }
}
