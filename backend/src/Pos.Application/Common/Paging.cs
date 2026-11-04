using Pos.Domain.Common;

namespace Pos.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public PagedResult<TOut> Map<TOut>(Func<T, TOut> map) =>
        new(Items.Select(map).ToArray(), Page, PageSize, TotalCount);
}

/// <summary>
/// Página solicitada, siempre válida y acotada para no sobrecargar a Odoo. Los Validators rechazan antes los valores
/// inválidos con los mismos códigos y mensajes; <see cref="Create"/> protege la regla si alguien usa el Handler directamente.
/// </summary>
public sealed record PageRequest
{
    public const int MaxPageSize = 100;
    public const string InvalidPageCode = "PAGING.INVALID_PAGE";
    public const string InvalidPageMessage = "La página debe ser 1 o mayor.";
    public const string InvalidPageSizeCode = "PAGING.INVALID_PAGE_SIZE";

    private PageRequest(int page, int pageSize)
    {
        Page = page;
        PageSize = pageSize;
    }

    public static string InvalidPageSizeMessage { get; } = $"El tamaño de página debe estar entre 1 y {MaxPageSize}.";

    public int Page { get; }

    public int PageSize { get; }

    public int Offset => (Page - 1) * PageSize;

    public static bool IsValidPage(int? page) => page is null or >= 1;

    public static bool IsValidPageSize(int? pageSize) => pageSize is null or (>= 1 and <= MaxPageSize);

    public static Result<PageRequest> Create(int? page, int? pageSize, int defaultPageSize)
    {
        if (!IsValidPage(page))
        {
            return Error.Validation(InvalidPageCode, InvalidPageMessage, "page");
        }

        if (!IsValidPageSize(pageSize))
        {
            return Error.Validation(InvalidPageSizeCode, InvalidPageSizeMessage, "pageSize");
        }

        return new PageRequest(page ?? 1, pageSize ?? defaultPageSize);
    }
}
