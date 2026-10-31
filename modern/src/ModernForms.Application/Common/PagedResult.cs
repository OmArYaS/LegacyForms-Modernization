namespace ModernForms.Application.Common;

/// <summary>Standard offset/limit paging envelope.</summary>
public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    #pragma warning disable CA1000 // intentional generic type factory — used by test helpers
    public static PagedResult<T> Empty(int page, int pageSize) => new()
    {
        Items = Array.Empty<T>(),
        Page = page,
        PageSize = pageSize
    };
    #pragma warning restore CA1000
}

/// <summary>Paging guard rails shared by all read paths.</summary>
public static class Paging
{
    public const int MaxPageSize = 100;

    public static int NormalizePage(int page) => page < 1 ? 1 : page;

    public static int NormalizePageSize(int size, int defaultSize = 25) =>
        size < 1 ? defaultSize : size > MaxPageSize ? MaxPageSize : size;
}
