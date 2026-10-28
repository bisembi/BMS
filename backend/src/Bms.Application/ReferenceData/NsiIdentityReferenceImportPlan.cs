using System.Collections.ObjectModel;

namespace Bms.Application.ReferenceData;

public sealed class NsiIdentityFunctionalRoleProjectionItem
{
    private NsiIdentityFunctionalRoleProjectionItem(
        Guid id,
        string code,
        string name,
        bool isActive)
    {
        Id = id;
        Code = code;
        Name = name;
        IsActive = isActive;
    }

    public Guid Id { get; }

    public string Code { get; }

    public string Name { get; }

    public bool IsActive { get; }

    public static NsiIdentityFunctionalRoleProjectionItem Create(
        Guid id,
        string code,
        string name,
        bool isActive)
    {
        NsiIdentityReferenceImportValidation.Identifier(id, nameof(id), "Functional-role id");
        return new NsiIdentityFunctionalRoleProjectionItem(
            id,
            NsiImportText.Validate(code, 30, nameof(code), "Functional-role code"),
            NsiImportText.Validate(name, 100, nameof(name), "Functional-role name"),
            isActive);
    }
}

public sealed class NsiIdentityCostCenterProjectionItem
{
    private NsiIdentityCostCenterProjectionItem(
        Guid id,
        string code,
        string name,
        Guid? parentId,
        bool isGroup,
        bool isActive,
        bool ownerManagedLocally)
    {
        Id = id;
        Code = code;
        Name = name;
        ParentId = parentId;
        IsGroup = isGroup;
        IsActive = isActive;
        OwnerManagedLocally = ownerManagedLocally;
    }

    public Guid Id { get; }

    public string Code { get; }

    public string Name { get; }

    public Guid? ParentId { get; }

    public bool IsGroup { get; }

    public bool IsActive { get; }
    public bool OwnerManagedLocally { get; }

    public static NsiIdentityCostCenterProjectionItem Create(
        Guid id,
        string code,
        string name,
        Guid? parentId,
        bool isGroup,
        bool isActive,
        bool ownerManagedLocally = false)
    {
        NsiIdentityReferenceImportValidation.Identifier(id, nameof(id), "Cost-centre id");
        if (parentId == Guid.Empty)
        {
            throw new ArgumentException("Cost-centre parent id must be null or non-empty.", nameof(parentId));
        }

        return new NsiIdentityCostCenterProjectionItem(
            id,
            NsiImportText.Validate(code, 100, nameof(code), "Projected cost-centre code"),
            NsiImportText.Validate(name, 500, nameof(name), "Projected cost-centre name"),
            parentId,
            isGroup,
            isActive, ownerManagedLocally);
    }
}

public sealed class NsiIdentityBudgetOwnerProjectionItem
{
    private NsiIdentityBudgetOwnerProjectionItem(
        Guid employeeId,
        Guid costCenterId,
        bool isActive)
    {
        EmployeeId = employeeId;
        CostCenterId = costCenterId;
        IsActive = isActive;
    }

    public Guid EmployeeId { get; }

    public Guid CostCenterId { get; }

    public bool IsActive { get; }

    public static NsiIdentityBudgetOwnerProjectionItem Create(
        Guid employeeId,
        Guid costCenterId,
        bool isActive)
    {
        NsiIdentityReferenceImportValidation.Identifier(
            employeeId,
            nameof(employeeId),
            "Budget-owner employee id");
        NsiIdentityReferenceImportValidation.Identifier(
            costCenterId,
            nameof(costCenterId),
            "Budget-owner cost-centre id");
        return new NsiIdentityBudgetOwnerProjectionItem(employeeId, costCenterId, isActive);
    }
}

public sealed class NsiIdentityReferenceImportWatermark
{
    private NsiIdentityReferenceImportWatermark(
        DateOnly asOf,
        DateTimeOffset capturedAtUtc,
        string sourceConfigurationSha256,
        string sourceSnapshotSha256)
    {
        AsOf = asOf;
        CapturedAtUtc = capturedAtUtc;
        SourceConfigurationSha256 = sourceConfigurationSha256;
        SourceSnapshotSha256 = sourceSnapshotSha256;
    }

    public DateOnly AsOf { get; }

    public DateTimeOffset CapturedAtUtc { get; }

    public string SourceConfigurationSha256 { get; }

    public string SourceSnapshotSha256 { get; }

    public static NsiIdentityReferenceImportWatermark Create(
        DateOnly asOf,
        DateTimeOffset capturedAtUtc,
        string sourceConfigurationSha256,
        string sourceSnapshotSha256)
    {
        if (asOf == default)
        {
            throw new ArgumentException("Snapshot date is required.", nameof(asOf));
        }

        return new NsiIdentityReferenceImportWatermark(
            asOf,
            NsiIdentityReferenceImportValidation.CapturedAtUtc(
                capturedAtUtc,
                nameof(capturedAtUtc)),
            NsiIdentityReferenceImportValidation.Sha256(
                sourceConfigurationSha256,
                nameof(sourceConfigurationSha256)),
            NsiIdentityReferenceImportValidation.Sha256(
                sourceSnapshotSha256,
                nameof(sourceSnapshotSha256)));
    }
}

internal static class NsiIdentityReferenceImportIssueCodes
{
    internal const string ProjectionWatermarkMissing = "Projection.WatermarkMissing";
    internal const string SnapshotOlderThanProjection = "Snapshot.OlderThanProjection";
    internal const string SnapshotRevisionConflict = "Snapshot.RevisionConflict";
    internal const string EmployeeCodeConflict = "Employee.CodeConflict";
    internal const string FunctionalRoleCodeConflict = "FunctionalRole.CodeConflict";
    internal const string CostCenterCodeConflict = "CostCenter.CodeConflict";

    internal static bool IsKnown(string value) =>
        value is ProjectionWatermarkMissing or
            SnapshotOlderThanProjection or
            SnapshotRevisionConflict or
            EmployeeCodeConflict or
            FunctionalRoleCodeConflict or
            CostCenterCodeConflict;
}

internal readonly record struct NsiIdentityRelationKey(Guid FirstId, Guid SecondId);

internal sealed class NsiIdentityImportAssignment<TItem>
    where TItem : class
{
    internal NsiIdentityImportAssignment(NsiReferenceImportOperation operation, TItem item)
    {
        if (operation is not (
            NsiReferenceImportOperation.Insert or
            NsiReferenceImportOperation.Update or
            NsiReferenceImportOperation.Unchanged))
        {
            throw new ArgumentException("An identity import assignment operation is invalid.", nameof(operation));
        }

        Operation = operation;
        Item = item ?? throw new ArgumentNullException(nameof(item));
    }

    internal NsiReferenceImportOperation Operation { get; }

    internal TItem Item { get; }
}

internal sealed class NsiIdentityReferenceProjectionSnapshot
{
    private NsiIdentityReferenceProjectionSnapshot(
        NsiIdentityReferenceImportWatermark? importWatermark,
        IReadOnlyList<NsiIdentityEmployeeImportItem> employees,
        IReadOnlyList<NsiIdentityFunctionalRoleProjectionItem> functionalRoles,
        IReadOnlyList<NsiIdentityCostCenterProjectionItem> costCenters,
        IReadOnlyList<NsiIdentityEmployeeRoleImportItem> employeeRoles,
        IReadOnlyList<NsiIdentityBudgetOwnerProjectionItem> budgetOwners)
    {
        ImportWatermark = importWatermark;
        Employees = employees;
        FunctionalRoles = functionalRoles;
        CostCenters = costCenters;
        EmployeeRoles = employeeRoles;
        BudgetOwners = budgetOwners;
    }

    internal NsiIdentityReferenceImportWatermark? ImportWatermark { get; }

    internal IReadOnlyList<NsiIdentityEmployeeImportItem> Employees { get; }

    internal IReadOnlyList<NsiIdentityFunctionalRoleProjectionItem> FunctionalRoles { get; }

    internal IReadOnlyList<NsiIdentityCostCenterProjectionItem> CostCenters { get; }

    internal IReadOnlyList<NsiIdentityEmployeeRoleImportItem> EmployeeRoles { get; }

    internal IReadOnlyList<NsiIdentityBudgetOwnerProjectionItem> BudgetOwners { get; }

    internal static NsiIdentityReferenceProjectionSnapshot Create(
        NsiIdentityReferenceImportWatermark? importWatermark,
        IEnumerable<NsiIdentityEmployeeImportItem> employees,
        IEnumerable<NsiIdentityFunctionalRoleProjectionItem> functionalRoles,
        IEnumerable<NsiIdentityCostCenterProjectionItem> costCenters,
        IEnumerable<NsiIdentityEmployeeRoleImportItem> employeeRoles,
        IEnumerable<NsiIdentityBudgetOwnerProjectionItem> budgetOwners)
    {
        var employeeSnapshot = Snapshot(employees, nameof(employees));
        var roleSnapshot = Snapshot(functionalRoles, nameof(functionalRoles));
        var costCenterSnapshot = Snapshot(costCenters, nameof(costCenters));
        var employeeRoleSnapshot = Snapshot(employeeRoles, nameof(employeeRoles));
        var budgetOwnerSnapshot = Snapshot(budgetOwners, nameof(budgetOwners));

        ValidateUnique(employeeSnapshot, item => item.Id, "Employee ids must be unique.");
        ValidateUnique(employeeSnapshot, item => item.Code, "Employee codes must be ordinal-unique.");
        ValidateUnique(roleSnapshot, item => item.Id, "Functional-role ids must be unique.");
        ValidateUnique(roleSnapshot, item => item.Code, "Functional-role codes must be ordinal-unique.");
        ValidateUnique(costCenterSnapshot, item => item.Id, "Cost-centre ids must be unique.");
        NsiIdentityReferenceImportValidation.Unique(
            costCenterSnapshot,
            item => item.Code,
            StringComparer.OrdinalIgnoreCase,
            "Cost-centre codes must be case-insensitively unique for the SQL projection.");
        ValidateUnique(
            employeeRoleSnapshot,
            item => new NsiIdentityRelationKey(item.EmployeeId, item.FunctionalRoleId),
            "Employee-role pairs must be unique.");
        ValidateUnique(
            budgetOwnerSnapshot,
            item => new NsiIdentityRelationKey(item.EmployeeId, item.CostCenterId),
            "Budget-owner pairs must be unique.");
        ValidateRelations(
            employeeSnapshot,
            roleSnapshot,
            costCenterSnapshot,
            employeeRoleSnapshot,
            budgetOwnerSnapshot);

        return new NsiIdentityReferenceProjectionSnapshot(
            importWatermark,
            ReadOnly(employeeSnapshot),
            ReadOnly(roleSnapshot),
            ReadOnly(costCenterSnapshot),
            ReadOnly(employeeRoleSnapshot),
            ReadOnly(budgetOwnerSnapshot));
    }

    private static T[] Snapshot<T>(IEnumerable<T> values, string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var snapshot = values.ToArray();
        if (snapshot.Any(item => item is null))
        {
            throw new ArgumentException("Projection collection contains a null item.", parameterName);
        }

        return snapshot;
    }

    private static IReadOnlyList<T> ReadOnly<T>(T[] values) =>
        new ReadOnlyCollection<T>(values);

    private static void ValidateUnique<TItem, TKey>(
        IEnumerable<TItem> values,
        Func<TItem, TKey> keySelector,
        string message)
        where TKey : notnull
    {
        var comparer = typeof(TKey) == typeof(string)
            ? (IEqualityComparer<TKey>)(object)StringComparer.Ordinal
            : EqualityComparer<TKey>.Default;
        NsiIdentityReferenceImportValidation.Unique(values, keySelector, comparer, message);
    }

    private static void ValidateRelations(
        IReadOnlyList<NsiIdentityEmployeeImportItem> employees,
        IReadOnlyList<NsiIdentityFunctionalRoleProjectionItem> roles,
        IReadOnlyList<NsiIdentityCostCenterProjectionItem> costCenters,
        IReadOnlyList<NsiIdentityEmployeeRoleImportItem> employeeRoles,
        IReadOnlyList<NsiIdentityBudgetOwnerProjectionItem> budgetOwners)
    {
        var employeeIds = employees.Select(item => item.Id).ToHashSet();
        var roleIds = roles.Select(item => item.Id).ToHashSet();
        var costCentersById = costCenters.ToDictionary(item => item.Id);

        foreach (var costCenter in costCenters)
        {
            if (costCenter.ParentId is Guid parentId &&
                (!costCentersById.TryGetValue(parentId, out var parent) || !parent.IsGroup))
            {
                throw new ArgumentException(
                    "A cost-centre parent must reference a group in the same projection.",
                    nameof(costCenters));
            }

            var visited = new HashSet<Guid> { costCenter.Id };
            var next = costCenter.ParentId;
            while (next is Guid nextId)
            {
                if (!visited.Add(nextId))
                {
                    throw new ArgumentException(
                        "Cost-centre hierarchy must be acyclic.",
                        nameof(costCenters));
                }
                next = costCentersById[nextId].ParentId;
            }
        }

        if (employeeRoles.Any(item =>
                !employeeIds.Contains(item.EmployeeId) ||
                !roleIds.Contains(item.FunctionalRoleId)))
        {
            throw new ArgumentException(
                "Employee-role rows must reference the same projection.",
                nameof(employeeRoles));
        }
        if (budgetOwners.Any(item =>
                !employeeIds.Contains(item.EmployeeId) ||
                !costCentersById.ContainsKey(item.CostCenterId)))
        {
            throw new ArgumentException(
                "Budget-owner rows must reference the same projection.",
                nameof(budgetOwners));
        }
    }
}

internal sealed class NsiIdentityReferenceImportPlan
{
    internal NsiIdentityReferenceImportPlan(
        DateOnly asOf,
        DateTimeOffset capturedAtUtc,
        string sourceConfigurationSha256,
        string sourceSnapshotSha256,
        IEnumerable<NsiIdentityImportAssignment<NsiIdentityEmployeeImportItem>> employees,
        IEnumerable<Guid> employeeDeactivations,
        IEnumerable<NsiIdentityImportAssignment<NsiIdentityFunctionalRoleImportItem>> functionalRoles,
        IEnumerable<Guid> functionalRoleDeactivations,
        IEnumerable<NsiIdentityImportAssignment<NsiIdentityCostCenterImportItem>> costCenters,
        IEnumerable<Guid> costCenterDeactivations,
        IEnumerable<NsiIdentityImportAssignment<NsiIdentityEmployeeRoleImportItem>> employeeRoles,
        IEnumerable<NsiIdentityRelationKey> employeeRoleDeactivations,
        IEnumerable<NsiIdentityImportAssignment<NsiIdentityBudgetOwnerImportItem>> budgetOwners,
        IEnumerable<NsiIdentityRelationKey> budgetOwnerDeactivations)
    {
        if (asOf == default)
        {
            throw new ArgumentException("Snapshot date is required.", nameof(asOf));
        }

        AsOf = asOf;
        CapturedAtUtc = NsiIdentityReferenceImportValidation.CapturedAtUtc(
            capturedAtUtc,
            nameof(capturedAtUtc));
        SourceConfigurationSha256 = NsiIdentityReferenceImportValidation.Sha256(
            sourceConfigurationSha256,
            nameof(sourceConfigurationSha256));
        SourceSnapshotSha256 = NsiIdentityReferenceImportValidation.Sha256(
            sourceSnapshotSha256,
            nameof(sourceSnapshotSha256));
        Employees = ReadOnly(employees, nameof(employees));
        EmployeeDeactivations = ReadOnly(employeeDeactivations, nameof(employeeDeactivations));
        FunctionalRoles = ReadOnly(functionalRoles, nameof(functionalRoles));
        FunctionalRoleDeactivations = ReadOnly(
            functionalRoleDeactivations,
            nameof(functionalRoleDeactivations));
        CostCenters = ReadOnly(costCenters, nameof(costCenters));
        CostCenterDeactivations = ReadOnly(costCenterDeactivations, nameof(costCenterDeactivations));
        EmployeeRoles = ReadOnly(employeeRoles, nameof(employeeRoles));
        EmployeeRoleDeactivations = ReadOnly(
            employeeRoleDeactivations,
            nameof(employeeRoleDeactivations));
        BudgetOwners = ReadOnly(budgetOwners, nameof(budgetOwners));
        BudgetOwnerDeactivations = ReadOnly(
            budgetOwnerDeactivations,
            nameof(budgetOwnerDeactivations));
    }

    internal DateOnly AsOf { get; }

    internal DateTimeOffset CapturedAtUtc { get; }

    internal string SourceConfigurationSha256 { get; }

    internal string SourceSnapshotSha256 { get; }

    internal IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityEmployeeImportItem>> Employees { get; }

    internal IReadOnlyList<Guid> EmployeeDeactivations { get; }

    internal IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityFunctionalRoleImportItem>> FunctionalRoles { get; }

    internal IReadOnlyList<Guid> FunctionalRoleDeactivations { get; }

    internal IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityCostCenterImportItem>> CostCenters { get; }

    internal IReadOnlyList<Guid> CostCenterDeactivations { get; }

    internal IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityEmployeeRoleImportItem>> EmployeeRoles { get; }

    internal IReadOnlyList<NsiIdentityRelationKey> EmployeeRoleDeactivations { get; }

    internal IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityBudgetOwnerImportItem>> BudgetOwners { get; }

    internal IReadOnlyList<NsiIdentityRelationKey> BudgetOwnerDeactivations { get; }

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        return new ReadOnlyCollection<T>(values.ToArray());
    }
}

internal sealed class NsiIdentityReferenceImportPlanResolution
{
    private NsiIdentityReferenceImportPlanResolution(
        NsiIdentityReferenceImportPlan? plan,
        string? issueCode)
    {
        Plan = plan;
        IssueCode = issueCode;
    }

    internal NsiIdentityReferenceImportPlan? Plan { get; }

    internal string? IssueCode { get; }

    internal bool IsReady => Plan is not null;

    internal static NsiIdentityReferenceImportPlanResolution Ready(
        NsiIdentityReferenceImportPlan plan) =>
        new(plan ?? throw new ArgumentNullException(nameof(plan)), null);

    internal static NsiIdentityReferenceImportPlanResolution Blocked(string issueCode)
    {
        if (string.IsNullOrWhiteSpace(issueCode) ||
            !NsiIdentityReferenceImportIssueCodes.IsKnown(issueCode))
        {
            throw new ArgumentException("A known identity import issue code is required.", nameof(issueCode));
        }

        return new NsiIdentityReferenceImportPlanResolution(null, issueCode);
    }
}

internal static class NsiIdentityReferenceImportPlanBuilder
{
    internal static NsiIdentityReferenceImportPlanResolution Resolve(
        NsiIdentityReferenceImportBatch batch,
        NsiIdentityReferenceProjectionSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(current);

        var bootstrapRequiresExactProjection =
            current.ImportWatermark is null && HasProjectionRows(current);
        if (current.ImportWatermark is { } watermark)
        {
            if (batch.AsOf < watermark.AsOf ||
                batch.CapturedAtUtc < watermark.CapturedAtUtc)
            {
                return NsiIdentityReferenceImportPlanResolution.Blocked(
                    NsiIdentityReferenceImportIssueCodes.SnapshotOlderThanProjection);
            }
            if (batch.CapturedAtUtc == watermark.CapturedAtUtc &&
                (batch.AsOf != watermark.AsOf ||
                 !string.Equals(
                    batch.SourceConfigurationSha256,
                    watermark.SourceConfigurationSha256,
                    StringComparison.Ordinal) ||
                 !string.Equals(
                    batch.SourceSnapshotSha256,
                    watermark.SourceSnapshotSha256,
                    StringComparison.Ordinal)))
            {
                return NsiIdentityReferenceImportPlanResolution.Blocked(
                    NsiIdentityReferenceImportIssueCodes.SnapshotRevisionConflict);
            }
        }

        if (HasEmployeeCodeConflict(batch, current))
        {
            return NsiIdentityReferenceImportPlanResolution.Blocked(
                NsiIdentityReferenceImportIssueCodes.EmployeeCodeConflict);
        }
        if (HasRoleCodeConflict(batch, current))
        {
            return NsiIdentityReferenceImportPlanResolution.Blocked(
                NsiIdentityReferenceImportIssueCodes.FunctionalRoleCodeConflict);
        }
        if (HasCostCenterCodeConflict(batch, current))
        {
            return NsiIdentityReferenceImportPlanResolution.Blocked(
                NsiIdentityReferenceImportIssueCodes.CostCenterCodeConflict);
        }

        var currentEmployees = current.Employees.ToDictionary(item => item.Id);
        var currentRoles = current.FunctionalRoles.ToDictionary(item => item.Id);
        var currentCostCenters = current.CostCenters.ToDictionary(item => item.Id);
        var currentEmployeeRoles = current.EmployeeRoles.ToDictionary(
            item => new NsiIdentityRelationKey(item.EmployeeId, item.FunctionalRoleId));
        var currentBudgetOwners = current.BudgetOwners.ToDictionary(
            item => new NsiIdentityRelationKey(item.EmployeeId, item.CostCenterId));
        // Local CFO owner choices survive complete NSI snapshots. Source items remain in
        // the plan as Unchanged, retaining the exact source hash and canonical replay.
        var localCenters = current.CostCenters.Where(c => c.OwnerManagedLocally).Select(c => c.Id).ToHashSet();
        var localEmployees = current.BudgetOwners.Where(a => a.IsActive && localCenters.Contains(a.CostCenterId))
            .Select(a => a.EmployeeId).ToHashSet();
        var ownerRole = current.FunctionalRoles.SingleOrDefault(r => r.Code == Bms.Domain.Workflow.ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode);
        // The salary calculator and its grants are managed in BMS. A complete
        // 1C identity snapshot must neither revoke them nor grant them again.
        var payrollRole = current.FunctionalRoles.SingleOrDefault(r => r.Code == Bms.Domain.Workflow.PayrollAdvanceWorkflow372.CalculatorRole);
        var ownerRoleId = ownerRole?.Id ?? batch.FunctionalRoles.SingleOrDefault(r =>
            r.TargetCode == Bms.Domain.Workflow.ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode)?.Id;
        // Compute the complete postimage, not the intermediate deactivate phase.
        // Locally managed CFO links remain authoritative. Any previous owner-link
        // history keeps a former owner from being re-granted without real scope.
        // Unrelated manual Owner-first employees have no such lost-scope provenance.
        var postEffectiveCenters = batch.CostCenters.Where(c => c.IsActive && !c.IsGroup)
            .Select(c => c.Id).ToHashSet();
        var postScopeEmployees = current.BudgetOwners
            .Where(a => a.IsActive && localCenters.Contains(a.CostCenterId) && postEffectiveCenters.Contains(a.CostCenterId))
            .Select(a => a.EmployeeId)
            .Concat(batch.BudgetOwners.Where(a => !localCenters.Contains(a.CostCenterId)
                    && postEffectiveCenters.Contains(a.CostCenterId)).Select(a => a.EmployeeId))
            .ToHashSet();
        var removedScopeEmployees = current.BudgetOwners
            .Select(a => a.EmployeeId).Where(id => !postScopeEmployees.Contains(id)).ToHashSet();
        bool SuppressRemovedOwnerRole(Guid employeeId, Guid roleId) =>
            ownerRoleId == roleId && removedScopeEmployees.Contains(employeeId);
        if (localEmployees.Count > 0 && ownerRole is not null && batch.FunctionalRoles.Any(r =>
            r.TargetCode == Bms.Domain.Workflow.ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode && r.Id != ownerRole.Id))
            return NsiIdentityReferenceImportPlanResolution.Blocked(NsiIdentityReferenceImportIssueCodes.FunctionalRoleCodeConflict);
        // Provenance includes manual revocation. NSI must neither grant nor revoke
        // this role for the locally selected owner, even when the role is absent/inactive.
        bool LocalRole(Guid employeeId, Guid roleId) => payrollRole?.Id == roleId
            || localEmployees.Contains(employeeId) && ownerRole?.Id == roleId;

        var employeeAssignments = batch.Employees
            .OrderBy(item => item.Id)
            .Select(item => Assignment(
                item,
                currentEmployees.TryGetValue(item.Id, out var existing) ? existing : null,
                EmployeeEquals))
            .ToArray();
        var roleAssignments = batch.FunctionalRoles
            .OrderBy(item => item.Id)
            .Select(item => payrollRole?.Id == item.Id || localEmployees.Count > 0 && ownerRole?.Id == item.Id
                ? new NsiIdentityImportAssignment<NsiIdentityFunctionalRoleImportItem>(NsiReferenceImportOperation.Unchanged, item)
                : Assignment(
                item,
                currentRoles.TryGetValue(item.Id, out var existing) ? existing : null,
                RoleEquals))
            .ToArray();
        var costCenterAssignments = batch.CostCenters
            .OrderBy(item => item.Id)
            .Select(item => Assignment(
                item,
                currentCostCenters.TryGetValue(item.Id, out var existing) ? existing : null,
                CostCenterEquals))
            .ToArray();
        var employeeRoleAssignments = batch.EmployeeRoles
            .OrderBy(item => item.EmployeeId)
            .ThenBy(item => item.FunctionalRoleId)
            .Select(item => LocalRole(item.EmployeeId, item.FunctionalRoleId)
                || SuppressRemovedOwnerRole(item.EmployeeId, item.FunctionalRoleId)
                ? new NsiIdentityImportAssignment<NsiIdentityEmployeeRoleImportItem>(NsiReferenceImportOperation.Unchanged, item)
                : Assignment(
                item,
                currentEmployeeRoles.TryGetValue(
                    new NsiIdentityRelationKey(item.EmployeeId, item.FunctionalRoleId),
                    out var existing)
                    ? existing
                    : null,
                EmployeeRoleEquals))
            .ToArray();
        var budgetOwnerAssignments = batch.BudgetOwners
            .OrderBy(item => item.EmployeeId)
            .ThenBy(item => item.CostCenterId)
            .Select(item => new NsiIdentityImportAssignment<NsiIdentityBudgetOwnerImportItem>(
                localCenters.Contains(item.CostCenterId) ? NsiReferenceImportOperation.Unchanged : !currentBudgetOwners.TryGetValue(
                    new NsiIdentityRelationKey(item.EmployeeId, item.CostCenterId),
                    out var existing)
                    ? NsiReferenceImportOperation.Insert
                    : existing.IsActive
                        ? NsiReferenceImportOperation.Unchanged
                        : NsiReferenceImportOperation.Update,
                item))
            .ToArray();

        var plan = new NsiIdentityReferenceImportPlan(
                batch.AsOf,
                batch.CapturedAtUtc,
                batch.SourceConfigurationSha256,
                batch.SourceSnapshotSha256,
                employeeAssignments,
                DeactivatedIds(
                    current.Employees,
                    batch.Employees,
                    item => item.Id,
                    item => item.Id,
                    item => item.IsActive),
                roleAssignments,
                DeactivatedIds(
                    current.FunctionalRoles.Where(r => payrollRole?.Id != r.Id && (localEmployees.Count == 0 || ownerRole?.Id != r.Id)),
                    batch.FunctionalRoles,
                    item => item.Id,
                    item => item.Id,
                    item => item.IsActive),
                costCenterAssignments,
                DeactivatedIds(
                    current.CostCenters,
                    batch.CostCenters,
                    item => item.Id,
                    item => item.Id,
                    item => item.IsActive),
                employeeRoleAssignments,
                DeactivatedRelations(
                    current.EmployeeRoles.Where(r => !LocalRole(r.EmployeeId, r.FunctionalRoleId)),
                    batch.EmployeeRoles,
                    item => new NsiIdentityRelationKey(item.EmployeeId, item.FunctionalRoleId),
                    item => new NsiIdentityRelationKey(item.EmployeeId, item.FunctionalRoleId),
                    item => item.IsActive)
                    .Concat(current.EmployeeRoles.Where(r => r.IsActive
                            && SuppressRemovedOwnerRole(r.EmployeeId, r.FunctionalRoleId))
                        .Select(r => new NsiIdentityRelationKey(r.EmployeeId, r.FunctionalRoleId)))
                    .Distinct().OrderBy(key => key.FirstId).ThenBy(key => key.SecondId).ToArray(),
                budgetOwnerAssignments,
                DeactivatedRelations(
                    current.BudgetOwners.Where(a => !localCenters.Contains(a.CostCenterId)),
                    batch.BudgetOwners,
                    item => new NsiIdentityRelationKey(item.EmployeeId, item.CostCenterId),
                    item => new NsiIdentityRelationKey(item.EmployeeId, item.CostCenterId),
                    item => item.IsActive));

        if (bootstrapRequiresExactProjection && HasProjectionChanges(plan))
        {
            return NsiIdentityReferenceImportPlanResolution.Blocked(
                NsiIdentityReferenceImportIssueCodes.ProjectionWatermarkMissing);
        }

        return NsiIdentityReferenceImportPlanResolution.Ready(plan);
    }

    private static bool HasProjectionRows(NsiIdentityReferenceProjectionSnapshot current) =>
        current.Employees.Count > 0 ||
        current.FunctionalRoles.Count > 0 ||
        current.CostCenters.Count > 0 ||
        current.EmployeeRoles.Count > 0 ||
        current.BudgetOwners.Count > 0;

    private static bool HasProjectionChanges(NsiIdentityReferenceImportPlan plan) =>
        plan.Employees.Any(item => item.Operation != NsiReferenceImportOperation.Unchanged) ||
        plan.EmployeeDeactivations.Count > 0 ||
        plan.FunctionalRoles.Any(item =>
            item.Operation != NsiReferenceImportOperation.Unchanged) ||
        plan.FunctionalRoleDeactivations.Count > 0 ||
        plan.CostCenters.Any(item => item.Operation != NsiReferenceImportOperation.Unchanged) ||
        plan.CostCenterDeactivations.Count > 0 ||
        plan.EmployeeRoles.Any(item => item.Operation != NsiReferenceImportOperation.Unchanged) ||
        plan.EmployeeRoleDeactivations.Count > 0 ||
        plan.BudgetOwners.Any(item => item.Operation != NsiReferenceImportOperation.Unchanged) ||
        plan.BudgetOwnerDeactivations.Count > 0;

    private static NsiIdentityImportAssignment<TCandidate> Assignment<TCandidate, TCurrent>(
        TCandidate candidate,
        TCurrent? current,
        Func<TCurrent, TCandidate, bool> equals)
        where TCandidate : class
        where TCurrent : class =>
        new(
            current is null
                ? NsiReferenceImportOperation.Insert
                : equals(current, candidate)
                    ? NsiReferenceImportOperation.Unchanged
                    : NsiReferenceImportOperation.Update,
            candidate);

    private static IReadOnlyList<Guid> DeactivatedIds<TCurrent, TCandidate>(
        IEnumerable<TCurrent> current,
        IEnumerable<TCandidate> candidates,
        Func<TCurrent, Guid> currentKey,
        Func<TCandidate, Guid> candidateKey,
        Func<TCurrent, bool> isActive)
    {
        var candidateIds = candidates.Select(candidateKey).ToHashSet();
        return current
            .Where(item => isActive(item) && !candidateIds.Contains(currentKey(item)))
            .Select(currentKey)
            .Order()
            .ToArray();
    }

    private static IReadOnlyList<NsiIdentityRelationKey> DeactivatedRelations<TCurrent, TCandidate>(
        IEnumerable<TCurrent> current,
        IEnumerable<TCandidate> candidates,
        Func<TCurrent, NsiIdentityRelationKey> currentKey,
        Func<TCandidate, NsiIdentityRelationKey> candidateKey,
        Func<TCurrent, bool> isActive)
    {
        var candidateKeys = candidates.Select(candidateKey).ToHashSet();
        return current
            .Where(item => isActive(item) && !candidateKeys.Contains(currentKey(item)))
            .Select(currentKey)
            .OrderBy(item => item.FirstId)
            .ThenBy(item => item.SecondId)
            .ToArray();
    }

    private static bool HasEmployeeCodeConflict(
        NsiIdentityReferenceImportBatch batch,
        NsiIdentityReferenceProjectionSnapshot current) =>
        HasCodeConflict(
            current.Employees.Select(item => (item.Id, item.Code)),
            batch.Employees.Select(item => (item.Id, item.Code)),
            StringComparer.Ordinal);

    private static bool HasRoleCodeConflict(
        NsiIdentityReferenceImportBatch batch,
        NsiIdentityReferenceProjectionSnapshot current) =>
        HasCodeConflict(
            current.FunctionalRoles.Select(item => (item.Id, item.Code)),
            batch.FunctionalRoles.Select(item => (item.Id, item.TargetCode)),
            StringComparer.Ordinal);

    private static bool HasCostCenterCodeConflict(
        NsiIdentityReferenceImportBatch batch,
        NsiIdentityReferenceProjectionSnapshot current) =>
        HasCodeConflict(
            current.CostCenters.Select(item => (item.Id, item.Code)),
            batch.CostCenters.Select(item => (item.Id, item.Code)),
            StringComparer.OrdinalIgnoreCase);

    private static bool HasCodeConflict(
        IEnumerable<(Guid Id, string Code)> current,
        IEnumerable<(Guid Id, string Code)> candidates,
        IEqualityComparer<string> comparer)
    {
        var final = current.ToDictionary(item => item.Id, item => item.Code);
        foreach (var candidate in candidates)
        {
            final[candidate.Id] = candidate.Code;
        }

        return final.Values.Distinct(comparer).Count() != final.Count;
    }

    private static bool EmployeeEquals(
        NsiIdentityEmployeeImportItem current,
        NsiIdentityEmployeeImportItem candidate) =>
        current.Id == candidate.Id &&
        string.Equals(current.Code, candidate.Code, StringComparison.Ordinal) &&
        string.Equals(current.Name, candidate.Name, StringComparison.Ordinal) &&
        string.Equals(
            current.SourceDatabaseUserName,
            candidate.SourceDatabaseUserName,
            StringComparison.Ordinal) &&
        current.IsActive == candidate.IsActive;

    private static bool RoleEquals(
        NsiIdentityFunctionalRoleProjectionItem current,
        NsiIdentityFunctionalRoleImportItem candidate) =>
        current.Id == candidate.Id &&
        string.Equals(current.Code, candidate.TargetCode, StringComparison.Ordinal) &&
        string.Equals(current.Name, candidate.Name, StringComparison.Ordinal) &&
        current.IsActive == candidate.IsActive;

    private static bool CostCenterEquals(
        NsiIdentityCostCenterProjectionItem current,
        NsiIdentityCostCenterImportItem candidate) =>
        current.Id == candidate.Id &&
        string.Equals(current.Code, candidate.Code, StringComparison.Ordinal) &&
        string.Equals(current.Name, candidate.Name, StringComparison.Ordinal) &&
        current.ParentId == candidate.ParentId &&
        current.IsGroup == candidate.IsGroup &&
        current.IsActive == candidate.IsActive;

    private static bool EmployeeRoleEquals(
        NsiIdentityEmployeeRoleImportItem current,
        NsiIdentityEmployeeRoleImportItem candidate) =>
        current.EmployeeId == candidate.EmployeeId &&
        current.FunctionalRoleId == candidate.FunctionalRoleId &&
        current.IsActive == candidate.IsActive;
}
