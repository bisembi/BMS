namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Periodic authority for a deputy to act on existing expense requests of one
/// principal. The immutable request initiator is never rewritten.
/// </summary>
public sealed class ExpenseRequestSubstitution
{
    public Guid Id { get; set; }
    public Guid PrincipalEmployeeId { get; set; }
    public Guid DeputyEmployeeId { get; set; }
    public DateTimeOffset ValidFromUtc { get; set; }
    public DateTimeOffset ValidThroughUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
