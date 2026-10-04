namespace HuntOps.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Skip, int Take);

public static class Paging
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;

    /// <summary>Clamps client-supplied paging values instead of rejecting them.</summary>
    public static (int Skip, int Take) Normalize(int? skip, int? take) =>
        (Math.Max(0, skip ?? 0), Math.Clamp(take ?? DefaultTake, 1, MaxTake));
}
