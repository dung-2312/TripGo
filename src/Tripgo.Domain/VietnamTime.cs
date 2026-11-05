namespace Tripgo.Domain;

/// <summary>All service dates and clock times are Vietnam time, whatever the server's own time zone is.</summary>
public static class VietnamTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(7);

    public static DateTimeOffset ToVietnam(this DateTimeOffset value) => value.ToOffset(Offset);

    public static DateOnly Today(DateTimeOffset? now = null) =>
        DateOnly.FromDateTime((now ?? DateTimeOffset.UtcNow).ToOffset(Offset).DateTime);
}
