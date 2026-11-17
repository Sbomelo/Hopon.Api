namespace Hopon.Api.Services;

public static class SaTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(2);

    public static DateTime ToUtc(DateTime saLocal)
    {
        if (saLocal.Kind == DateTimeKind.Utc)
            return saLocal;

        // datetime-local has no zone. Ignore the server's local zone, even for Kind.Local.
        return DateTime.SpecifyKind(saLocal - Offset, DateTimeKind.Utc);
    }

    public static DateTime ToSa(DateTime utc)
    {
        return DateTime.SpecifyKind(utc + Offset, DateTimeKind.Unspecified);
    }
}
