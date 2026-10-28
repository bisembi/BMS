using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Application.Integration;

public sealed class DatabaseReadinessMonitorService(
    IDatabaseReadinessMonitorReader reader,
    ICurrentActor currentActor,
    IClock clock)
{
    private const int ActualsYearsRequiredMigrationCount = 50;
    private const int CashFlowDisplayOrderRequiredMigrationCount = 51;
    private const int NumberReservationsRequiredMigrationCount = 52;
    private const int PrivateAttachmentsRequiredMigrationCount = 53;

    private static readonly HashSet<string> AllowedMissingComponents = new(StringComparer.Ordinal)
    {
        "schema-version-table",
        "migration-001",
        "migration-002",
        "migration-003",
        "migration-004",
        "migration-005",
        "migration-006",
        "migration-007",
        "migration-008",
        "migration-009",
        "migration-010",
        "migration-011",
        "migration-012",
        "migration-013",
        "migration-014",
        "migration-015",
        "migration-016",
        "migration-017",
        "migration-018",
        "migration-019",
        "migration-020",
        "migration-021",
        "migration-022",
        "migration-023",
        "migration-024",
        "migration-025",
        "migration-026",
        "migration-027",
        "migration-028",
        "migration-029",
        "migration-030",
        "migration-031",
        "migration-032",
        "migration-033",
        "migration-034",
        "migration-035",
        "migration-036",
        "migration-037",
        "migration-038",
        "migration-039",
        "migration-040",
        "migration-041",
        "migration-042",
        "migration-043",
        "migration-045",
        "migration-046",
        "migration-047",
        "migration-048",
        "migration-049",
        "migration-050",
        "migration-051",
        "migration-052",
        "migration-053",
        "migration-054",
        "cash-flow-display-order-compatibility",
        "identifier-semantics",
        "cash-flow-items",
        "expense-items",
        "expense-request-line-accrual-snapshot",
        "money-precision",
        "counterparties",
        "contracts",
        "budget-projection",
        "employee-functional-roles",
        "currencies-and-rates",
        "expense-request-currency-rate-snapshot",
        "expense-request-line-currency-conversion",
        "expense-request-urgency",
        "overpayment-deficit-storage",
        "treasury-overpayment-storage",
        "budget-owner-cost-center-assignments",
        "approval-route-snapshots",
        "employee-actor-mappings",
        "expense-request-workflow-activation",
        "accrual-closing-cycle-storage",
        "expense-request-closing-activation",
        "accrual-notifications",
        "expense-request-substitutions",
        "expense-request-task-scope-index",
        "expense-request-attachment-storage",
        "budget-instance-storage",
        "budget-instance-line-storage",
        "budget-directory-storage",
        "identity-reference-import-watermark-storage",
        "expense-request-numbering-storage"
    };

    public async Task<DatabaseReadinessMonitorDto> GetAsync(
        CancellationToken cancellationToken)
    {
        ActorIdentity.Require(currentActor);
        if (!currentActor.IsBmsAdministrator
            && !currentActor.IsBmsIntegrationAdministrator)
        {
            throw new ActorForbiddenException();
        }

        var record = await reader.ReadAsync(cancellationToken)
            ?? throw InvalidReaderState();
        var missingComponents = record.MissingComponents?.ToArray()
            ?? throw InvalidReaderState();
        var uniqueMissingComponents = missingComponents.ToHashSet(StringComparer.Ordinal);
        var missingMigrationCount = missingComponents.Count(component =>
            component.StartsWith("migration-", StringComparison.Ordinal));
        if (record.RequiredMigrationCount is not (ActualsYearsRequiredMigrationCount
                or CashFlowDisplayOrderRequiredMigrationCount or NumberReservationsRequiredMigrationCount
                or PrivateAttachmentsRequiredMigrationCount)
            || record.AppliedMigrationCount < 0 || record.AppliedMigrationCount > record.RequiredMigrationCount
            || missingComponents.Length != uniqueMissingComponents.Count
            || uniqueMissingComponents.Any(component => !AllowedMissingComponents.Contains(component))
            || uniqueMissingComponents.Any(component => component.StartsWith("migration-", StringComparison.Ordinal)
                && int.Parse(component.AsSpan(10), System.Globalization.CultureInfo.InvariantCulture) > record.RequiredMigrationCount + 1)
            || record.AppliedMigrationCount != record.RequiredMigrationCount - missingMigrationCount
            || record.DatabaseReady != (missingComponents.Length == 0))
        {
            throw InvalidReaderState();
        }

        return new DatabaseReadinessMonitorDto(
            record.DatabaseReady,
            record.AppliedMigrationCount,
            record.RequiredMigrationCount,
            clock.UtcNow,
            Array.AsReadOnly(missingComponents));
    }

    private static InvalidOperationException InvalidReaderState() =>
        new("Database readiness reader returned an invalid safe state.");
}
