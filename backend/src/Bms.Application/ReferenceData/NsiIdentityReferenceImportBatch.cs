using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Bms.Domain.Workflow;

namespace Bms.Application.ReferenceData;

public sealed class NsiIdentityEmployeeImportItem
{
    private NsiIdentityEmployeeImportItem(
        Guid id,
        string code,
        string name,
        string? sourceDatabaseUserName,
        bool isActive)
    {
        Id = id;
        Code = code;
        Name = name;
        SourceDatabaseUserName = sourceDatabaseUserName;
        IsActive = isActive;
    }

    public Guid Id { get; }

    public string Code { get; }

    public string Name { get; }

    public string? SourceDatabaseUserName { get; }

    public bool IsActive { get; }

    public static NsiIdentityEmployeeImportItem Create(
        Guid id,
        string code,
        string name,
        string? sourceDatabaseUserName,
        bool isActive)
    {
        NsiIdentityReferenceImportValidation.Identifier(id, nameof(id), "Employee id");
        return new NsiIdentityEmployeeImportItem(
            id,
            NsiImportText.Validate(code, 20, nameof(code), "Employee code"),
            NsiImportText.Validate(name, 150, nameof(name), "Employee name"),
            NsiIdentityReferenceImportValidation.OptionalText(
                sourceDatabaseUserName,
                100,
                nameof(sourceDatabaseUserName),
                "Employee source database username"),
            isActive);
    }
}

public sealed class NsiIdentityFunctionalRoleImportItem
{
    private NsiIdentityFunctionalRoleImportItem(
        Guid id,
        string sourceCode,
        string targetCode,
        string name,
        bool isActive)
    {
        Id = id;
        SourceCode = sourceCode;
        TargetCode = targetCode;
        Name = name;
        IsActive = isActive;
    }

    public Guid Id { get; }

    public string SourceCode { get; }

    public string TargetCode { get; }

    public string Name { get; }

    public bool IsActive { get; }

    public static NsiIdentityFunctionalRoleImportItem Create(
        Guid id,
        string sourceCode,
        string targetCode,
        string name,
        bool isActive)
    {
        NsiIdentityReferenceImportValidation.Identifier(id, nameof(id), "Functional-role id");
        return new NsiIdentityFunctionalRoleImportItem(
            id,
            NsiImportText.Validate(
                sourceCode,
                30,
                nameof(sourceCode),
                "Functional-role source code"),
            NsiImportText.Validate(
                targetCode,
                30,
                nameof(targetCode),
                "Functional-role target code"),
            NsiImportText.Validate(name, 100, nameof(name), "Functional-role name"),
            isActive);
    }
}

public sealed class NsiIdentityCostCenterImportItem
{
    private NsiIdentityCostCenterImportItem(
        Guid id,
        string code,
        string name,
        Guid? parentId,
        bool isGroup,
        bool isActive)
    {
        Id = id;
        Code = code;
        Name = name;
        ParentId = parentId;
        IsGroup = isGroup;
        IsActive = isActive;
    }

    public Guid Id { get; }

    public string Code { get; }

    public string Name { get; }

    public Guid? ParentId { get; }

    public bool IsGroup { get; }

    public bool IsActive { get; }

    public static NsiIdentityCostCenterImportItem Create(
        Guid id,
        string code,
        string name,
        Guid? parentId,
        bool isGroup,
        bool isActive)
    {
        NsiIdentityReferenceImportValidation.Identifier(id, nameof(id), "Cost-centre id");
        if (parentId == Guid.Empty)
        {
            throw new ArgumentException("Cost-centre parent id must be null or non-empty.", nameof(parentId));
        }

        return new NsiIdentityCostCenterImportItem(
            id,
            NsiImportText.Validate(code, 20, nameof(code), "Cost-centre code"),
            NsiImportText.Validate(name, 150, nameof(name), "Cost-centre name"),
            parentId,
            isGroup,
            isActive);
    }
}

public sealed class NsiIdentityEmployeeRoleImportItem
{
    private NsiIdentityEmployeeRoleImportItem(
        Guid employeeId,
        Guid functionalRoleId,
        bool isActive)
    {
        EmployeeId = employeeId;
        FunctionalRoleId = functionalRoleId;
        IsActive = isActive;
    }

    public Guid EmployeeId { get; }

    public Guid FunctionalRoleId { get; }

    public bool IsActive { get; }

    public static NsiIdentityEmployeeRoleImportItem Create(
        Guid employeeId,
        Guid functionalRoleId,
        bool isActive)
    {
        NsiIdentityReferenceImportValidation.Identifier(
            employeeId,
            nameof(employeeId),
            "Employee-role employee id");
        NsiIdentityReferenceImportValidation.Identifier(
            functionalRoleId,
            nameof(functionalRoleId),
            "Employee-role functional-role id");
        return new NsiIdentityEmployeeRoleImportItem(employeeId, functionalRoleId, isActive);
    }
}

public sealed class NsiIdentityBudgetOwnerImportItem
{
    private NsiIdentityBudgetOwnerImportItem(
        Guid employeeId,
        Guid costCenterId,
        DateOnly effectiveFrom)
    {
        EmployeeId = employeeId;
        CostCenterId = costCenterId;
        EffectiveFrom = effectiveFrom;
    }

    public Guid EmployeeId { get; }

    public Guid CostCenterId { get; }

    public DateOnly EffectiveFrom { get; }

    public static NsiIdentityBudgetOwnerImportItem Create(
        Guid employeeId,
        Guid costCenterId,
        DateOnly effectiveFrom)
    {
        NsiIdentityReferenceImportValidation.Identifier(
            employeeId,
            nameof(employeeId),
            "Budget-owner employee id");
        NsiIdentityReferenceImportValidation.Identifier(
            costCenterId,
            nameof(costCenterId),
            "Budget-owner cost-centre id");
        if (effectiveFrom == default)
        {
            throw new ArgumentException(
                "Budget-owner effective date is required.",
                nameof(effectiveFrom));
        }

        return new NsiIdentityBudgetOwnerImportItem(employeeId, costCenterId, effectiveFrom);
    }
}

/// <summary>
/// Immutable full current-state snapshot of the external identity/authority objects
/// required by BMS. The batch is transport-neutral and performs no write or DI
/// registration. Missing rows may be interpreted as deactivation only after
/// this complete-snapshot contract has been validated.
/// </summary>
public sealed class NsiIdentityReferenceImportBatch
{
    private NsiIdentityReferenceImportBatch(
        DateOnly asOf,
        DateTimeOffset capturedAtUtc,
        string sourceConfigurationSha256,
        IReadOnlyList<NsiIdentityEmployeeImportItem> employees,
        IReadOnlyList<NsiIdentityFunctionalRoleImportItem> functionalRoles,
        IReadOnlyList<NsiIdentityCostCenterImportItem> costCenters,
        IReadOnlyList<NsiIdentityEmployeeRoleImportItem> employeeRoles,
        IReadOnlyList<NsiIdentityBudgetOwnerImportItem> budgetOwners)
    {
        AsOf = asOf;
        CapturedAtUtc = capturedAtUtc;
        SourceConfigurationSha256 = sourceConfigurationSha256;
        SourceSnapshotSha256 = NsiIdentityReferenceSnapshotHash.Compute(
            asOf,
            employees,
            functionalRoles,
            costCenters,
            employeeRoles,
            budgetOwners);
        Employees = employees;
        FunctionalRoles = functionalRoles;
        CostCenters = costCenters;
        EmployeeRoles = employeeRoles;
        BudgetOwners = budgetOwners;
    }

    public DateOnly AsOf { get; }

    public DateTimeOffset CapturedAtUtc { get; }

    public string SourceConfigurationSha256 { get; }

    public string SourceSnapshotSha256 { get; }

    public IReadOnlyList<NsiIdentityEmployeeImportItem> Employees { get; }

    public IReadOnlyList<NsiIdentityFunctionalRoleImportItem> FunctionalRoles { get; }

    public IReadOnlyList<NsiIdentityCostCenterImportItem> CostCenters { get; }

    public IReadOnlyList<NsiIdentityEmployeeRoleImportItem> EmployeeRoles { get; }

    public IReadOnlyList<NsiIdentityBudgetOwnerImportItem> BudgetOwners { get; }

    public static NsiIdentityReferenceImportBatch CreateCompleteSnapshot(
        DateOnly asOf,
        DateTimeOffset capturedAtUtc,
        string sourceConfigurationSha256,
        bool sourceDeclaresCompleteSnapshot,
        IEnumerable<NsiIdentityEmployeeImportItem> employees,
        IEnumerable<NsiIdentityFunctionalRoleImportItem> functionalRoles,
        IEnumerable<NsiIdentityCostCenterImportItem> costCenters,
        IEnumerable<NsiIdentityEmployeeRoleImportItem> employeeRoles,
        IEnumerable<NsiIdentityBudgetOwnerImportItem> budgetOwners)
    {
        if (asOf == default)
        {
            throw new ArgumentException("Snapshot date is required.", nameof(asOf));
        }
        if (!sourceDeclaresCompleteSnapshot)
        {
            throw new ArgumentException(
                "Identity reference import requires an explicitly complete source snapshot.",
                nameof(sourceDeclaresCompleteSnapshot));
        }

        var capturedAt = NsiIdentityReferenceImportValidation.CapturedAtUtc(
            capturedAtUtc,
            nameof(capturedAtUtc));

        var configurationHash =
            NsiIdentityReferenceImportValidation.Sha256(
                sourceConfigurationSha256,
                nameof(sourceConfigurationSha256));
        var employeeSnapshot = Snapshot(employees, nameof(employees));
        var roleSnapshot = Snapshot(functionalRoles, nameof(functionalRoles));
        var costCenterSnapshot = Snapshot(costCenters, nameof(costCenters));
        var employeeRoleSnapshot = Snapshot(employeeRoles, nameof(employeeRoles));
        var budgetOwnerSnapshot = Snapshot(budgetOwners, nameof(budgetOwners));

        ValidateUniqueEmployees(employeeSnapshot);
        ValidateUniqueRoles(roleSnapshot);
        ValidateCostCenters(costCenterSnapshot);
        ValidateRelations(
            asOf,
            employeeSnapshot,
            roleSnapshot,
            costCenterSnapshot,
            employeeRoleSnapshot,
            budgetOwnerSnapshot);

        return new NsiIdentityReferenceImportBatch(
            asOf,
            capturedAt,
            configurationHash,
            ReadOnly(employeeSnapshot),
            ReadOnly(roleSnapshot),
            ReadOnly(costCenterSnapshot),
            ReadOnly(employeeRoleSnapshot),
            ReadOnly(budgetOwnerSnapshot));
    }

    private static T[] Snapshot<T>(IEnumerable<T> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var snapshot = values.ToArray();
        if (snapshot.Any(item => item is null))
        {
            throw new ArgumentException("Import collection contains a null item.", parameterName);
        }

        return snapshot;
    }

    private static IReadOnlyList<T> ReadOnly<T>(T[] values) =>
        new ReadOnlyCollection<T>(values);

    private static void ValidateUniqueEmployees(
        IReadOnlyList<NsiIdentityEmployeeImportItem> employees)
    {
        NsiIdentityReferenceImportValidation.Unique(
            employees,
            item => item.Id,
            EqualityComparer<Guid>.Default,
            "Employee ids must be unique.");
        NsiIdentityReferenceImportValidation.Unique(
            employees,
            item => item.Code,
            StringComparer.Ordinal,
            "Employee codes must be ordinal-unique.");
    }

    private static void ValidateUniqueRoles(
        IReadOnlyList<NsiIdentityFunctionalRoleImportItem> roles)
    {
        NsiIdentityReferenceImportValidation.Unique(
            roles,
            item => item.Id,
            EqualityComparer<Guid>.Default,
            "Functional-role ids must be unique.");
        NsiIdentityReferenceImportValidation.Unique(
            roles,
            item => item.SourceCode,
            StringComparer.Ordinal,
            "Functional-role source codes must be ordinal-unique.");
        NsiIdentityReferenceImportValidation.Unique(
            roles,
            item => item.TargetCode,
            StringComparer.Ordinal,
            "Functional-role target codes must be ordinal-unique.");

        foreach (var requiredCode in new[]
                 {
                     ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode,
                     ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode
                 })
        {
            if (roles.Count(item => item.IsActive &&
                    string.Equals(item.TargetCode, requiredCode, StringComparison.Ordinal)) != 1)
            {
                throw new ArgumentException(
                    "A complete identity snapshot requires exactly one active required BMS role mapping.",
                    nameof(roles));
            }
        }
    }

    private static void ValidateCostCenters(
        IReadOnlyList<NsiIdentityCostCenterImportItem> costCenters)
    {
        NsiIdentityReferenceImportValidation.Unique(
            costCenters,
            item => item.Id,
            EqualityComparer<Guid>.Default,
            "Cost-centre ids must be unique.");
        NsiIdentityReferenceImportValidation.Unique(
            costCenters,
            item => item.Code,
            StringComparer.OrdinalIgnoreCase,
            "Cost-centre codes must be case-insensitively unique for the SQL projection.");

        var byId = costCenters.ToDictionary(item => item.Id);
        foreach (var item in costCenters)
        {
            if (item.ParentId is Guid parentId &&
                (!byId.TryGetValue(parentId, out var parent) || !parent.IsGroup))
            {
                throw new ArgumentException(
                    "A cost-centre parent must reference a group in the same snapshot.",
                    nameof(costCenters));
            }

            var visited = new HashSet<Guid> { item.Id };
            var next = item.ParentId;
            while (next is Guid nextId)
            {
                if (!visited.Add(nextId))
                {
                    throw new ArgumentException(
                        "Cost-centre hierarchy must be acyclic.",
                        nameof(costCenters));
                }
                next = byId[nextId].ParentId;
            }
        }
    }

    private static void ValidateRelations(
        DateOnly asOf,
        IReadOnlyList<NsiIdentityEmployeeImportItem> employees,
        IReadOnlyList<NsiIdentityFunctionalRoleImportItem> roles,
        IReadOnlyList<NsiIdentityCostCenterImportItem> costCenters,
        IReadOnlyList<NsiIdentityEmployeeRoleImportItem> employeeRoles,
        IReadOnlyList<NsiIdentityBudgetOwnerImportItem> budgetOwners)
    {
        var employeesById = employees.ToDictionary(item => item.Id);
        var rolesById = roles.ToDictionary(item => item.Id);
        var costCentersById = costCenters.ToDictionary(item => item.Id);

        NsiIdentityReferenceImportValidation.Unique(
            employeeRoles,
            item => (item.EmployeeId, item.FunctionalRoleId),
            EqualityComparer<(Guid, Guid)>.Default,
            "Employee-role pairs must be unique.");
        foreach (var assignment in employeeRoles)
        {
            if (!employeesById.TryGetValue(assignment.EmployeeId, out var employee) ||
                !rolesById.TryGetValue(assignment.FunctionalRoleId, out var role))
            {
                throw new ArgumentException(
                    "Employee-role assignments must reference the same complete snapshot.",
                    nameof(employeeRoles));
            }
            if (assignment.IsActive && (!employee.IsActive || !role.IsActive))
            {
                throw new ArgumentException(
                    "An active employee-role assignment requires active source rows.",
                    nameof(employeeRoles));
            }
        }

        NsiIdentityReferenceImportValidation.Unique(
            budgetOwners,
            item => item.CostCenterId,
            EqualityComparer<Guid>.Default,
            "The current source budget-owner slice must contain at most one owner per cost centre.");
        foreach (var assignment in budgetOwners)
        {
            if (assignment.EffectiveFrom > asOf ||
                !employeesById.TryGetValue(assignment.EmployeeId, out var employee) ||
                !costCentersById.TryGetValue(assignment.CostCenterId, out var costCenter) ||
                !employee.IsActive ||
                !costCenter.IsActive ||
                costCenter.IsGroup)
            {
                throw new ArgumentException(
                    "A current budget-owner assignment must reference active non-group source rows and an effective date not after the snapshot.",
                    nameof(budgetOwners));
            }

            var hasBudgetOwnerRole = employeeRoles.Any(employeeRole =>
                employeeRole.EmployeeId == assignment.EmployeeId &&
                employeeRole.IsActive &&
                rolesById.TryGetValue(employeeRole.FunctionalRoleId, out var role) &&
                role.IsActive &&
                string.Equals(
                    role.TargetCode,
                    ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode,
                    StringComparison.Ordinal));
            if (!hasBudgetOwnerRole)
            {
                throw new ArgumentException(
                    "A current budget owner must have an active role explicitly mapped to the BMS budget-owner role.",
                    nameof(budgetOwners));
            }
        }
    }
}

internal static class NsiIdentityReferenceSnapshotHash
{
    private const string ContractVersion = "BMS-NSI-IDENTITY-SNAPSHOT-V1";

    internal static string Compute(
        DateOnly asOf,
        IEnumerable<NsiIdentityEmployeeImportItem> employees,
        IEnumerable<NsiIdentityFunctionalRoleImportItem> functionalRoles,
        IEnumerable<NsiIdentityCostCenterImportItem> costCenters,
        IEnumerable<NsiIdentityEmployeeRoleImportItem> employeeRoles,
        IEnumerable<NsiIdentityBudgetOwnerImportItem> budgetOwners)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, ContractVersion);
        Append(hash, asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        AppendCollection(
            hash,
            "EMPLOYEES",
            employees.OrderBy(item => item.Id),
            (state, item) =>
            {
                Append(state, item.Id);
                Append(state, item.Code);
                Append(state, item.Name);
                AppendNullable(state, item.SourceDatabaseUserName);
                Append(state, item.IsActive);
            });
        AppendCollection(
            hash,
            "FUNCTIONAL_ROLES",
            functionalRoles.OrderBy(item => item.Id),
            (state, item) =>
            {
                Append(state, item.Id);
                Append(state, item.SourceCode);
                Append(state, item.TargetCode);
                Append(state, item.Name);
                Append(state, item.IsActive);
            });
        AppendCollection(
            hash,
            "COST_CENTERS",
            costCenters.OrderBy(item => item.Id),
            (state, item) =>
            {
                Append(state, item.Id);
                Append(state, item.Code);
                Append(state, item.Name);
                AppendNullable(state, item.ParentId);
                Append(state, item.IsGroup);
                Append(state, item.IsActive);
            });
        AppendCollection(
            hash,
            "EMPLOYEE_ROLES",
            employeeRoles
                .OrderBy(item => item.EmployeeId)
                .ThenBy(item => item.FunctionalRoleId),
            (state, item) =>
            {
                Append(state, item.EmployeeId);
                Append(state, item.FunctionalRoleId);
                Append(state, item.IsActive);
            });
        AppendCollection(
            hash,
            "BUDGET_OWNERS",
            budgetOwners
                .OrderBy(item => item.CostCenterId)
                .ThenBy(item => item.EmployeeId),
            (state, item) =>
            {
                Append(state, item.EmployeeId);
                Append(state, item.CostCenterId);
                Append(state, item.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            });

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendCollection<T>(
        IncrementalHash hash,
        string name,
        IEnumerable<T> items,
        Action<IncrementalHash, T> appendItem)
    {
        var snapshot = items.ToArray();
        Append(hash, name);
        Append(hash, snapshot.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var item in snapshot)
        {
            appendItem(hash, item);
        }
    }

    private static void Append(IncrementalHash hash, Guid value) =>
        Append(hash, value.ToString("D", CultureInfo.InvariantCulture));

    private static void Append(IncrementalHash hash, bool value) =>
        Append(hash, value ? "1" : "0");

    private static void AppendNullable(IncrementalHash hash, Guid? value)
    {
        if (value is Guid identifier)
        {
            Append(hash, "VALUE");
            Append(hash, identifier);
            return;
        }

        Append(hash, "NULL");
    }

    private static void AppendNullable(IncrementalHash hash, string? value)
    {
        if (value is not null)
        {
            Append(hash, "VALUE");
            Append(hash, value);
            return;
        }

        Append(hash, "NULL");
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}

internal static class NsiIdentityReferenceImportValidation
{
    internal static DateTimeOffset CapturedAtUtc(
        DateTimeOffset value,
        string parameterName)
    {
        if (value == default || value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Source capture time must be a non-default UTC timestamp.",
                parameterName);
        }

        return value;
    }

    internal static void Identifier(Guid value, string parameterName, string fieldName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{fieldName} is required.", parameterName);
        }
    }

    internal static string? OptionalText(
        string? value,
        int maxLength,
        string parameterName,
        string fieldName)
    {
        if (value is null)
        {
            return null;
        }

        return NsiImportText.Validate(value, maxLength, parameterName, fieldName);
    }

    internal static string Sha256(string value, string parameterName)
    {
        if (value is null || value.Length != 64 || value.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
        {
            throw new ArgumentException(
                "Source SHA-256 must contain exactly 64 uppercase hexadecimal characters.",
                parameterName);
        }

        return value;
    }

    internal static void Unique<TItem, TKey>(
        IEnumerable<TItem> items,
        Func<TItem, TKey> keySelector,
        IEqualityComparer<TKey> comparer,
        string errorMessage)
        where TKey : notnull
    {
        var keys = new HashSet<TKey>(comparer);
        if (items.Any(item => !keys.Add(keySelector(item))))
        {
            throw new ArgumentException(errorMessage, nameof(items));
        }
    }
}
