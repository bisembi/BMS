using Bms.Application.Integration;

namespace Bms.Infrastructure.Health;

public sealed class DatabaseReadinessMonitorReader(IDatabaseReadinessProbe probe) :
    IDatabaseReadinessMonitorReader
{
    private const int ActualsYearsRequiredMigrationCount = 50;
    private const int CashFlowDisplayOrderRequiredMigrationCount = 51;

    public async Task<DatabaseReadinessMonitorRecord> ReadAsync(
        CancellationToken cancellationToken)
    {
        var readiness = await probe.InspectAsync(cancellationToken);
        var missing = new List<string>();

        AddMissing(missing, readiness.SchemaVersionTableExists, "schema-version-table");

        var versions = new[]
        {
            readiness.Version1Applied,
            readiness.Version2Applied,
            readiness.Version3Applied,
            readiness.Version4Applied,
            readiness.Version5Applied,
            readiness.Version6Applied,
            readiness.Version7Applied,
            readiness.Version8Applied,
            readiness.Version9Applied,
            readiness.Version10Applied,
            readiness.Version11Applied,
            readiness.Version12Applied,
            readiness.Version13Applied,
            readiness.Version14Applied,
            readiness.Version15Applied,
            readiness.Version16Applied,
            readiness.Version17Applied,
            readiness.Version18Applied,
            readiness.Version19Applied,
            readiness.Version20Applied,
            readiness.Version21Applied,
            readiness.Version22Applied,
            readiness.Version23Applied,
            readiness.Version24Applied,
            readiness.Version25Applied,
            readiness.Version26Applied,
            readiness.Version27Applied,
            readiness.Version28Applied,
            readiness.Version29Applied,
            readiness.Version30Applied,
            readiness.Version31Applied,
            readiness.Version32Applied,
            readiness.Version33Applied,
            readiness.Version34Applied,
            readiness.Version35Applied,
            readiness.Version36Applied,
            readiness.Version37Applied,
            readiness.Version38Applied,
            readiness.Version39Applied,
            readiness.Version40Applied,
            readiness.Version41Applied,
            readiness.Version42Applied,
            readiness.Version43Applied
        };
        for (var index = 0; index < versions.Length; index++)
        {
            AddMissing(missing, versions[index], $"migration-{index + 1:D3}");
        }

        AddMissing(missing, readiness.IdentifierSemanticsReady, "identifier-semantics");
        AddMissing(missing, readiness.Version45Applied, "migration-045");
        AddMissing(missing, readiness.Version46Applied, "migration-046");
        AddMissing(missing, readiness.Version47Applied, "migration-047");
        AddMissing(missing, readiness.Version48Applied, "migration-048");
        AddMissing(missing, readiness.Version49Applied, "migration-049");
        AddMissing(missing, readiness.Version50Applied, "migration-050");
        AddMissing(missing, readiness.Version51Applied, "migration-051");
        AddMissing(missing, readiness.CashFlowDisplayOrderCompatible, "cash-flow-display-order-compatibility");
        AddMissing(missing, readiness.CashFlowItemsReady, "cash-flow-items");
        AddMissing(missing, readiness.ExpenseItemsReady, "expense-items");
        AddMissing(
            missing,
            readiness.ExpenseRequestLineAccrualSnapshotReady,
            "expense-request-line-accrual-snapshot");
        AddMissing(missing, readiness.MoneyPrecisionReady, "money-precision");
        AddMissing(missing, readiness.CounterpartiesReady, "counterparties");
        AddMissing(missing, readiness.ContractsReady, "contracts");
        AddMissing(missing, readiness.BudgetProjectionReady, "budget-projection");
        AddMissing(
            missing,
            readiness.EmployeeFunctionalRolesReady,
            "employee-functional-roles");
        AddMissing(missing, readiness.CurrencyProjectionReady, "currencies-and-rates");
        AddMissing(
            missing,
            readiness.ExpenseRequestCurrencyRateSnapshotReady,
            "expense-request-currency-rate-snapshot");
        AddMissing(
            missing,
            readiness.ExpenseRequestLineCurrencyConversionReady,
            "expense-request-line-currency-conversion");
        AddMissing(
            missing,
            readiness.ExpenseRequestUrgencyInvariantReady,
            "expense-request-urgency");
        AddMissing(
            missing,
            readiness.OverpaymentDeficitStorageReady,
            "overpayment-deficit-storage");
        AddMissing(
            missing,
            readiness.TreasuryOverpaymentStorageReady,
            "treasury-overpayment-storage");
        AddMissing(
            missing,
            readiness.BudgetOwnerCostCenterAssignmentsReady,
            "budget-owner-cost-center-assignments");
        AddMissing(
            missing,
            readiness.ApprovalRouteSnapshotsReady,
            "approval-route-snapshots");
        AddMissing(
            missing,
            readiness.EmployeeActorMappingsReady,
            "employee-actor-mappings");
        AddMissing(
            missing,
            readiness.ExpenseRequestWorkflowActivationReady,
            "expense-request-workflow-activation");
        AddMissing(
            missing,
            readiness.AccrualClosingCycleStorageReady,
            "accrual-closing-cycle-storage");
        AddMissing(
            missing,
            readiness.ExpenseRequestClosingActivationReady,
            "expense-request-closing-activation");
        AddMissing(missing, readiness.AccrualNotificationsReady, "accrual-notifications");
        AddMissing(
            missing,
            readiness.ExpenseRequestSubstitutionsReady,
            "expense-request-substitutions");
        AddMissing(
            missing,
            readiness.ExpenseRequestTaskScopeIndexReady,
            "expense-request-task-scope-index");
        AddMissing(
            missing,
            readiness.ExpenseRequestAttachmentStorageReady,
            "expense-request-attachment-storage");
        AddMissing(
            missing,
            readiness.BudgetInstanceStorageReady,
            "budget-instance-storage");
        AddMissing(
            missing,
            readiness.BudgetInstanceLineStorageReady,
            "budget-instance-line-storage");

        AddMissing(missing, readiness.BudgetDirectoryStorageReady, "budget-directory-storage");
        AddMissing(
            missing,
            readiness.IdentityReferenceImportWatermarkStorageReady,
            "identity-reference-import-watermark-storage");
        AddMissing(
            missing,
            readiness.ExpenseRequestNumberingStorageReady,
            "expense-request-numbering-storage");

        AddMissing(missing, readiness.Version52Applied, "migration-052");
        AddMissing(missing, readiness.Version53Applied, "migration-053");
        AddMissing(missing, readiness.Version54Applied, "migration-054");
        return new DatabaseReadinessMonitorRecord(
            missing.Count == 0,
            versions.Count(applied => applied) + (readiness.Version45Applied ? 1 : 0) + (readiness.Version46Applied ? 1 : 0) + (readiness.Version47Applied ? 1 : 0) + (readiness.Version48Applied ? 1 : 0) + (readiness.Version49Applied ? 1 : 0) + (readiness.Version50Applied ? 1 : 0) + (readiness.Version51Applied ? 1 : 0) + (readiness.Version52Applied ? 1 : 0) + (readiness.Version53Applied ? 1 : 0) + (readiness.Version54Applied ? 1 : 0),
            CashFlowDisplayOrderRequiredMigrationCount + 2,
            Array.AsReadOnly(missing.ToArray()));
    }

    private static void AddMissing(
        ICollection<string> missing,
        bool ready,
        string component)
    {
        if (!ready)
        {
            missing.Add(component);
        }
    }
}
