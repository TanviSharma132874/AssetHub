using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AssetHub.Shared;

/// <summary>
/// SQLite has no real DateTime type, so values come back with Kind = Unspecified and the
/// browser would read them as local time. These converters keep everything UTC.
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter() : base(
        v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
    { }
}

public sealed class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public NullableUtcDateTimeConverter() : base(
        v => v.HasValue
            ? (v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc))
            : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v)
    { }
}

public static class ModelBuilderExtensions
{
    /// <summary>Call at the end of OnModelCreating.</summary>
    public static void ApplyUtcDateTimes(this ModelBuilder builder)
    {
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                    property.SetValueConverter(new UtcDateTimeConverter());
                else if (property.ClrType == typeof(DateTime?))
                    property.SetValueConverter(new NullableUtcDateTimeConverter());
            }
        }
    }
}

public static class TimeExtensions
{
    public static DateTime AsUtc(this DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

/// <summary>
/// Serialises the operations that must never race (allocating an asset, approving a transfer,
/// booking a resource). Each module runs as one process on SQLite, so an in-process lock is
/// enough; database unique indexes act as a second line of defence.
/// </summary>
public static class WriteGate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        await Gate.WaitAsync();
        try
        {
            return await work();
        }
        finally
        {
            Gate.Release();
        }
    }
}
