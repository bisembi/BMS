using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Bms.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF's in-memory key identity must use the same ordinal semantics as the
/// reviewed BIN2 SQL columns. Otherwise two physically distinct actors can
/// collide in one unit of work before SQL Server sees them.
/// </summary>
internal static class IdentifierValueComparers
{
    internal static ValueComparer<string> OrdinalKey { get; } = new(
        (left, right) => string.Equals(left, right, StringComparison.Ordinal),
        value => value.GetHashCode(StringComparison.Ordinal),
        value => value);
}
