namespace Company.Function.Utilities;

public static class Utc
{
    public static DateTime Now => DateTime.UtcNow;

    public static DateTime EnsureUtc(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc
            ? dt
            : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
}
