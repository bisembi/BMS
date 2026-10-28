using System.Data;
using Bms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bms.Infrastructure.Health;

public sealed record DatabaseSchemaReadiness(
    bool SchemaVersionTableExists,
    bool Version1Applied,
    bool Version2Applied,
    bool Version3Applied,
    bool Version4Applied,
    bool Version5Applied,
    bool Version6Applied,
    bool Version7Applied,
    bool Version8Applied,
    bool IdentifierSemanticsReady,
    bool Version9Applied = false,
    bool CashFlowItemsReady = false,
    bool Version10Applied = false,
    bool ExpenseItemsReady = false,
    bool Version11Applied = false,
    bool ExpenseRequestLineAccrualSnapshotReady = false,
    bool Version12Applied = false,
    bool MoneyPrecisionReady = false,
    bool Version13Applied = false,
    bool CounterpartiesReady = false,
    bool Version14Applied = false,
    bool ContractsReady = false,
    bool BudgetProjectionReady = false,
    bool Version15Applied = false,
    bool EmployeeFunctionalRolesReady = false,
    bool Version16Applied = false,
    bool CurrencyProjectionReady = false,
    bool Version17Applied = false,
    bool ExpenseRequestCurrencyRateSnapshotReady = false,
    bool Version18Applied = false,
    bool ExpenseRequestLineCurrencyConversionReady = false,
    bool ExpenseRequestUrgencyInvariantReady = false,
    bool Version19Applied = false,
    bool OverpaymentDeficitStorageReady = false,
    bool Version20Applied = false,
    bool TreasuryOverpaymentStorageReady = false,
    bool Version21Applied = false,
    bool BudgetOwnerCostCenterAssignmentsReady = false,
    bool Version22Applied = false,
    bool ApprovalRouteSnapshotsReady = false,
    bool Version23Applied = false,
    bool EmployeeActorMappingsReady = false,
    bool Version24Applied = false,
    bool ExpenseRequestWorkflowActivationReady = false,
    bool Version25Applied = false,
    bool AccrualClosingCycleStorageReady = false,
    bool Version26Applied = false,
    bool ExpenseRequestClosingActivationReady = false,
    bool Version27Applied = false,
    bool AccrualNotificationsReady = false,
    bool Version28Applied = false,
    bool Version29Applied = false,
    bool ExpenseRequestSubstitutionsReady = false,
    bool Version30Applied = false,
    bool ExpenseRequestTaskScopeIndexReady = false,
    bool Version31Applied = false,
    bool ExpenseRequestAttachmentStorageReady = false,
    bool Version32Applied = false,
    bool BudgetInstanceStorageReady = false,
    bool BudgetInstanceLineStorageReady = false,
    bool Version33Applied = false,
    bool Version34Applied = false,
    bool BudgetDirectoryStorageReady = false,
    bool Version35Applied = false,
    bool IdentityReferenceImportWatermarkStorageReady = false,
    bool Version36Applied = false,
    bool Version37Applied = false,
    bool ExpenseRequestNumberingStorageReady = false,
    bool Version38Applied = false,
    bool Version39Applied = false,
    bool Version40Applied = false,
    bool Version41Applied = false,
    bool Version42Applied = false,
    bool Version43Applied = false,
    bool Version45Applied = false,
    bool Version46Applied = false,
    bool Version47Applied = false,
    bool Version48Applied = false,
    bool Version49Applied = false,
    bool Version50Applied = false,
    bool Version51Applied = false,
    bool CashFlowDisplayOrderCompatible = false,
    bool Version52Applied = false,
    bool Version53Applied = false,
    bool Version54Applied = false);

public interface IDatabaseReadinessProbe
{
    Task<DatabaseSchemaReadiness> InspectAsync(CancellationToken cancellationToken);
}

public sealed class SqlServerDatabaseReadinessProbe(IServiceScopeFactory scopeFactory) :
    IDatabaseReadinessProbe
{
    // Share only the currently executing physical inspection. A finished
    // verdict is never reused; individual caller cancellation stops its wait.
    private readonly ReadinessSingleFlight387 singleFlight = new(TimeSpan.FromSeconds(70));
    // The probe verifies the complete physical schema. The current 83-field
    // metadata batch is deliberately fail-closed, but a cold compilation after
    // owned DDL must not turn a ready database into a spurious dependency
    // outage.  Keep the outer health-check budget strictly larger.
    internal static TimeSpan CommandTimeout { get; } = TimeSpan.FromSeconds(60);

    public Task<DatabaseSchemaReadiness> InspectAsync(
        CancellationToken cancellationToken) =>
        singleFlight.RunAsync(InspectPhysicalSchemaAsync, cancellationToken);

    private async Task<DatabaseSchemaReadiness> InspectPhysicalSchemaAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BmsDbContext>();
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandTimeout = checked((int)CommandTimeout.TotalSeconds);
        command.CommandText = ReadinessCatalogSnapshot407.Text;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "BMS database readiness query returned no result row.");
        }

        return new DatabaseSchemaReadiness(
            reader.GetBoolean(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4),
            reader.GetBoolean(5),
            reader.GetBoolean(6),
            reader.GetBoolean(7),
            reader.GetBoolean(8),
            reader.GetBoolean(12),
            reader.GetBoolean(9),
            reader.GetBoolean(13),
            reader.GetBoolean(10),
            reader.GetBoolean(14),
            reader.GetBoolean(11),
            reader.GetBoolean(15),
            reader.GetBoolean(16),
            reader.GetBoolean(17),
            reader.GetBoolean(18),
            reader.GetBoolean(19),
            reader.GetBoolean(20),
            reader.GetBoolean(21),
            reader.GetBoolean(22),
            reader.GetBoolean(23),
            reader.GetBoolean(24),
            reader.GetBoolean(25),
            reader.GetBoolean(26),
            reader.GetBoolean(27),
            reader.GetBoolean(28),
            reader.GetBoolean(29),
            reader.GetBoolean(30),
            reader.GetBoolean(31),
            reader.GetBoolean(32),
            reader.GetBoolean(33),
            reader.GetBoolean(34),
            reader.GetBoolean(35),
            reader.GetBoolean(36),
            reader.GetBoolean(37),
            reader.GetBoolean(38),
            reader.GetBoolean(39),
            reader.GetBoolean(40),
            reader.GetBoolean(41),
            reader.GetBoolean(42),
            reader.GetBoolean(43),
            reader.GetBoolean(44),
            reader.GetBoolean(45),
            reader.GetBoolean(46),
            reader.GetBoolean(47),
            reader.GetBoolean(48),
            reader.GetBoolean(49),
            reader.GetBoolean(50),
            reader.GetBoolean(51),
            reader.GetBoolean(52),
            reader.GetBoolean(53),
            reader.GetBoolean(54),
            reader.GetBoolean(55),
            reader.GetBoolean(56),
            reader.GetBoolean(57),
            reader.GetBoolean(58),
            reader.GetBoolean(59),
            reader.GetBoolean(60),
            reader.GetBoolean(61),
            reader.GetBoolean(62),
            reader.GetBoolean(63),
            reader.GetBoolean(64),
            reader.GetBoolean(65),
            reader.GetBoolean(66),
            reader.GetBoolean(67),
            reader.GetBoolean(68),
            reader.GetBoolean(69),
            reader.GetBoolean(70),
            reader.GetBoolean(71),
            reader.GetBoolean(72),
            reader.GetBoolean(73),
            reader.GetBoolean(74),
            reader.GetBoolean(75),
            reader.GetBoolean(76),
            reader.GetBoolean(77),
            reader.GetBoolean(78),
            reader.GetBoolean(79),
            reader.GetBoolean(80),
            reader.GetBoolean(81),
            reader.GetBoolean(82),
            reader.GetBoolean(83),
            reader.GetBoolean(84));
    }
}

public sealed class DatabaseReadinessHealthCheck(IDatabaseReadinessProbe probe) : IHealthCheck
{
    // This outer budget must exceed the probe's connection/command budget.
    // Otherwise a physically ready cold database is cancelled by the host
    // before the probe can return its exact schema verdict.
    public static TimeSpan RegistrationTimeout { get; } = TimeSpan.FromSeconds(75);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var readiness = await probe.InspectAsync(cancellationToken);
            if (readiness is
                {
                    SchemaVersionTableExists: true,
                    Version1Applied: true,
                    Version2Applied: true,
                    Version3Applied: true,
                    Version4Applied: true,
                    Version5Applied: true,
                    Version6Applied: true,
                    Version7Applied: true,
                    Version8Applied: true,
                    Version9Applied: true,
                    Version10Applied: true,
                    Version11Applied: true,
                    Version12Applied: true,
                    Version13Applied: true,
                    Version14Applied: true,
                    Version15Applied: true,
                    Version16Applied: true,
                    Version17Applied: true,
                    Version18Applied: true,
                    Version19Applied: true,
                    Version20Applied: true,
                    Version21Applied: true,
                    Version22Applied: true,
                    Version23Applied: true,
                    Version24Applied: true,
                    Version25Applied: true,
                    Version26Applied: true,
                    Version27Applied: true,
                    Version28Applied: true,
                    Version29Applied: true,
                    Version30Applied: true,
                    Version31Applied: true,
                    Version32Applied: true,
                    IdentifierSemanticsReady: true,
                    CashFlowItemsReady: true,
                    ExpenseItemsReady: true,
                    ExpenseRequestLineAccrualSnapshotReady: true,
                    MoneyPrecisionReady: true,
                    CounterpartiesReady: true,
                    ContractsReady: true,
                    BudgetProjectionReady: true,
                    EmployeeFunctionalRolesReady: true,
                    CurrencyProjectionReady: true,
                    ExpenseRequestCurrencyRateSnapshotReady: true,
                    ExpenseRequestLineCurrencyConversionReady: true,
                    OverpaymentDeficitStorageReady: true,
                    TreasuryOverpaymentStorageReady: true,
                    BudgetOwnerCostCenterAssignmentsReady: true,
                    ApprovalRouteSnapshotsReady: true,
                    EmployeeActorMappingsReady: true,
                    ExpenseRequestWorkflowActivationReady: true,
                    AccrualClosingCycleStorageReady: true,
                    ExpenseRequestClosingActivationReady: true,
                    AccrualNotificationsReady: true,
                    ExpenseRequestUrgencyInvariantReady: true,
                    ExpenseRequestSubstitutionsReady: true,
                    ExpenseRequestTaskScopeIndexReady: true,
                    ExpenseRequestAttachmentStorageReady: true,
                    BudgetInstanceStorageReady: true,
                    BudgetInstanceLineStorageReady: true,
                    Version33Applied: true,
                    Version34Applied: true,
                    BudgetDirectoryStorageReady: true,
                    Version35Applied: true,
                    IdentityReferenceImportWatermarkStorageReady: true,
                    Version36Applied: true,
                    Version37Applied: true,
                    ExpenseRequestNumberingStorageReady: true,
                    Version38Applied: true,
                    Version39Applied: true,
                    Version40Applied: true,
                    Version41Applied: true,
                    Version42Applied: true,
                    Version43Applied: true,
                    Version45Applied: true,
                    Version46Applied: true,
                    Version47Applied: true,
                    Version48Applied: true,
                    Version49Applied: true,
                    Version50Applied: true,
                    Version51Applied: true,
                    CashFlowDisplayOrderCompatible: true,
                    Version53Applied: true,
                    Version54Applied: true
                })
            {
                return HealthCheckResult.Healthy(
                "BMS database is reachable, schema versions 1 through 43, 45 through 51 are applied, the additive schema-52 cash-flow order is compatible, " +
                    "and identifier/budget/cash-flow/expense-item/line-accrual-snapshot/money/counterparty/contract/employee-role/currency/currency-rate-snapshot/line-currency-conversion/expense-request-urgency/overpayment-deficit-storage/treasury-overpayment-storage/budget-owner-cost-center-assignment/approval-route-snapshot/employee-actor-mapping/workflow-activation/accrual-closing-cycle-storage/expense-request-closing-activation/substitution/task-scope-index/attachment-storage/budget-instance-storage/budget-instance-line-storage/expense-request-numbering schema semantics are ready.");
            }

            var missing = new List<string>();
            if (!readiness.Version54Applied) missing.Add("54:private-attachment-files");
            if (!readiness.Version53Applied) missing.Add("53:expense-request-number-reservations");
            if (!readiness.CashFlowDisplayOrderCompatible)
                missing.Add("51/52:cash-flow-item-display-order-compatibility");
            if (!readiness.Version51Applied) missing.Add("51:051_cash_flow_actuals_supported_years.sql");
            if (!readiness.Version50Applied) missing.Add("50:050_cash_flow_catalog_direction.sql");
            if (!readiness.Version49Applied) missing.Add("49:049_cost_center_directory_metadata.sql");
            if (!readiness.SchemaVersionTableExists)
            {
                missing.Add("bms_web.schema_version");
            }

            if (!readiness.Version1Applied)
            {
                missing.Add("1:001_bootstrap_bms_web.sql");
            }

            if (!readiness.Version2Applied)
            {
                missing.Add("2:002_budget_projection.sql");
            }

            if (!readiness.Version3Applied)
            {
                missing.Add("3:003_access_scope.sql");
            }

            if (!readiness.Version4Applied)
            {
                missing.Add("4:004_workflow_matrix.sql");
            }

            if (!readiness.Version5Applied)
            {
                missing.Add("5:005_expense_request_evidence.sql");
            }

            if (!readiness.Version6Applied)
            {
                missing.Add("6:006_expense_request_copy_source.sql");
            }

            if (!readiness.Version7Applied)
            {
                missing.Add("7:007_expense_request_line_order.sql");
            }

            if (!readiness.Version8Applied)
            {
                missing.Add("8:008_actor_idempotency_collation.sql");
            }

            if (!readiness.Version9Applied)
            {
                missing.Add("9:009_cash_flow_items_accrual_source.sql");
            }

            if (!readiness.Version10Applied)
            {
                missing.Add("10:010_expense_items_accrual_mapping.sql");
            }

            if (!readiness.Version11Applied)
            {
                missing.Add("11:011_expense_request_line_accrual_snapshot.sql");
            }

            if (!readiness.Version12Applied)
            {
                missing.Add("12:012_money_decimal18_scale2.sql");
            }

            if (!readiness.Version13Applied)
            {
                missing.Add("13:013_counterparties_reference.sql");
            }

            if (!readiness.Version14Applied)
            {
                missing.Add("14:014_contracts_reference.sql");
            }

            if (!readiness.Version15Applied)
            {
                missing.Add("15:015_employee_functional_roles.sql");
            }

            if (!readiness.Version16Applied)
            {
                missing.Add("16:016_currencies_and_daily_rates.sql");
            }

            if (!readiness.Version17Applied)
            {
                missing.Add("17:017_expense_request_currency_rate_snapshot.sql");
            }

            if (!readiness.Version18Applied)
            {
                missing.Add("18:018_expense_request_line_currency_conversion.sql");
            }

            if (!readiness.Version19Applied)
            {
                missing.Add("19:019_overpayment_deficit_storage.sql");
            }

            if (!readiness.Version20Applied)
            {
                missing.Add("20:020_treasury_payment_overpayment_storage.sql");
            }

            if (!readiness.Version21Applied)
            {
                missing.Add("21:021_budget_owner_cost_center_assignments.sql");
            }

            if (!readiness.Version22Applied)
            {
                missing.Add("22:022_expense_request_approval_route_snapshots.sql");
            }

            if (!readiness.Version23Applied)
            {
                missing.Add("23:023_employee_actor_mappings.sql");
            }

            if (!readiness.Version24Applied)
            {
                missing.Add("24:024_expense_request_workflow_activation.sql");
            }

            if (!readiness.Version25Applied)
            {
                missing.Add("25:025_expense_request_accrual_closing_cycles.sql");
            }

            if (!readiness.Version26Applied)
            {
                missing.Add("26:026_expense_request_closing_activation.sql");
            }

            if (!readiness.Version27Applied)
            {
                missing.Add("27:027_accrual_red_zone_notifications.sql");
            }

            if (!readiness.Version28Applied)
            {
                missing.Add("28:028_cash_flow_accrual_settings_version.sql");
            }

            if (!readiness.Version29Applied)
            {
                missing.Add("29:029_expense_request_substitutions.sql");
            }

            if (!readiness.Version30Applied)
            {
                missing.Add("30:030_expense_request_task_scope_index.sql");
            }

            if (!readiness.Version31Applied)
            {
                missing.Add("31:031_expense_request_attachments.sql");
            }

            if (!readiness.Version32Applied)
            {
                missing.Add("32:032_budget_instance_draft.sql");
            }

            if (!readiness.Version33Applied)
            {
                missing.Add("33:033_budget_instance_monthly_period.sql");
            }

            if (!readiness.Version34Applied)
            {
                missing.Add("34:034_budget_cost_center_directory.sql");
            }

            if (!readiness.Version35Applied)
            {
                missing.Add("35:035_budget_instance_approval.sql");
            }

            if (!readiness.Version36Applied)
            {
                missing.Add("36:036_identity_reference_import_watermark.sql");
            }

            if (!readiness.Version37Applied)
            {
                missing.Add("37:037_budget_instance_line_comment.sql");
            }

            if (!readiness.Version38Applied)
            {
                missing.Add("38:038_expense_request_numbering.sql");
            }

            if (!readiness.Version39Applied)
            {
                missing.Add("39:039_counterparty_bin.sql");
            }

            if (!readiness.Version40Applied)
            {
                missing.Add("40:040_cash_flow_activity_type.sql");
            }

            if (!readiness.Version41Applied)
            {
                missing.Add("41:041_bdds_snapshot_and_flow_direction.sql");
            }

            if (!readiness.Version42Applied)
            {
                missing.Add("42:042_cash_flow_actuals_2026.sql");
            }
            if (!readiness.Version43Applied)
                missing.Add("43:043_cash_flow_item_cost_center.sql");
            if (!readiness.Version45Applied)
                missing.Add("45:045_cfo_initiators_and_budget_approval.sql");
            if (!readiness.Version46Applied)
                missing.Add("46:046_budget_instance_operation_rows.sql");
            if (!readiness.Version47Applied)
                missing.Add("47:047_budget_instance_line_list_covering.sql");
            if (!readiness.Version48Applied)
                missing.Add("48:048_company_budget_versions.sql/company-version-physical-and-active-invariants");

            if (!readiness.BudgetDirectoryStorageReady)
            {
                missing.Add("budget-directory-storage:columns/PK/UQ/index/FK/CHECK/no-trigger");
            }

            if (!readiness.IdentityReferenceImportWatermarkStorageReady)
            {
                missing.Add("identity-reference-import-watermark-storage:columns/PK/CHECK/no-FK/no-trigger");
            }

            if (!readiness.ExpenseRequestNumberingStorageReady)
            {
                missing.Add("expense-request-numbering-storage:request-number/filtered-UQ/counter/PK/FK/CHECK/no-trigger");
            }

            if (!readiness.IdentifierSemanticsReady)
            {
                missing.Add("identifier-semantics:BIN2/PK/CHECK/index");
            }

            if (!readiness.BudgetProjectionReady)
            {
                missing.Add("budget-projection:columns/PK/UQ/index/FK/CHECK/default/no-trigger");
            }


            if (!readiness.CashFlowItemsReady)
            {
                missing.Add("cash-flow-items:columns/BIN2/PK/UQ/FK/CHECK");
            }

            if (!readiness.ExpenseItemsReady)
            {
                missing.Add("expense-items:columns/BIN2/PK/UQ/no-FK/no-CHECK/no-trigger");
            }

            if (!readiness.ExpenseRequestLineAccrualSnapshotReady)
            {
                missing.Add("expense-request-line-accrual-snapshot:nullable-columns/CASE/no-index/no-FK/no-trigger");
            }

            if (!readiness.MoneyPrecisionReady)
            {
                missing.Add("money:decimal(18,2)/nullability/defaults/checks");
            }

            if (!readiness.CounterpartiesReady)
            {
                missing.Add("counterparties:columns/BIN2/PK/UQ/no-FK/no-CHECK/no-trigger");
            }

            if (!readiness.ContractsReady)
            {
                missing.Add("contracts:scoped-columns/BIN2/PK/UQ/index/no-FK/no-CHECK/no-trigger");
            }

            if (!readiness.EmployeeFunctionalRolesReady)
            {
                missing.Add("employee-functional-roles:columns/BIN2/PK/UQ/index/FK/no-CHECK/no-trigger");
            }

            if (!readiness.CurrencyProjectionReady)
            {
                missing.Add("currencies-and-rates:columns/BIN2/precision/PK/UQ/index/FK/nonnegative-CHECK/no-trigger");
            }

            if (!readiness.ExpenseRequestCurrencyRateSnapshotReady)
            {
                missing.Add("expense-request-currency-rate-snapshot:nullable-columns/CASE/no-index/no-FK/no-trigger");
            }

            if (!readiness.ExpenseRequestLineCurrencyConversionReady)
            {
                missing.Add("expense-request-line-currency-conversion:nullable-decimal(18,2)/CASE/no-index/no-FK/no-trigger");
            }

            if (!readiness.ExpenseRequestUrgencyInvariantReady)
            {
                missing.Add("expense-request-urgency:bit/nvarchar(1000)/default/trusted-CHECK/no-trigger");
            }

            if (!readiness.OverpaymentDeficitStorageReady)
            {
                missing.Add("overpayment-deficit-storage:tables/columns/PK/UQ/index/FK/CHECK/no-trigger");
            }

            if (!readiness.TreasuryOverpaymentStorageReady)
            {
                missing.Add("treasury-overpayment-storage:tables/columns/PK/UQ/index/FK/CHECK/no-trigger");
            }

            if (!readiness.BudgetOwnerCostCenterAssignmentsReady)
            {
                missing.Add("budget-owner-cost-center-assignments:columns/PK/index/FK/no-CHECK/no-trigger");
            }

            if (!readiness.ApprovalRouteSnapshotsReady)
            {
                missing.Add("approval-route-snapshots:tables/columns/PK/index/FK/no-CHECK/no-trigger");
            }

            if (!readiness.EmployeeActorMappingsReady)
            {
                missing.Add("employee-actor-mappings:columns/BIN2/PK/index/FK/CHECK/no-trigger");
            }

            if (!readiness.ExpenseRequestWorkflowActivationReady)
            {
                missing.Add("expense-request-workflow-activation:route-pointers/CHECK/exact-registry");
            }

            if (!readiness.AccrualClosingCycleStorageReady)
            {
                missing.Add("accrual-closing-cycle-storage:tables/columns/PK/UQ/index/FK/CHECK/no-trigger");
            }

            if (!readiness.ExpenseRequestClosingActivationReady)
            {
                missing.Add("expense-request-closing-activation:state-columns/amounts/current-cycle-FK/CHECK/no-trigger");
            }

            if (!readiness.AccrualNotificationsReady)
            {
                missing.Add("accrual-notifications:columns/BIN2/PK/UQ/index/FK/CHECK/no-trigger");
            }

            if (!readiness.ExpenseRequestSubstitutionsReady)
            {
                missing.Add("expense-request-substitutions:columns/PK/index/FK/CHECK/no-trigger");
            }

            if (!readiness.ExpenseRequestTaskScopeIndexReady)
            {
                missing.Add("expense-request-task-scope-index:nonunique/OrganizationId-Id/no-filter/no-include");
            }

            if (!readiness.ExpenseRequestAttachmentStorageReady)
            {
                missing.Add("expense-request-attachment-storage:columns/PK/index/FK/CHECK/no-trigger");
            }

            if (!readiness.BudgetInstanceStorageReady)
            {
                missing.Add("budget-instance-storage:columns/PK/UQ/index/FK/CHECK/no-trigger");
            }

            if (!readiness.BudgetInstanceLineStorageReady)
            {
                missing.Add("budget-instance-line-storage:columns/PK/UQ/FK/CHECK/no-trigger");
            }

            return HealthCheckResult.Unhealthy(
                $"BMS database schema is not ready. Missing: {string.Join(", ", missing)}.");
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy(
                "BMS database readiness check failed.");
        }
    }
}
