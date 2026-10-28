namespace Bms.Application.ReferenceData;

internal sealed class NsiIdentityReferenceProjectionWriteUnavailableException : Exception
{
    private const string SafeMessage =
        "The identity reference projection write could not be completed consistently.";

    internal NsiIdentityReferenceProjectionWriteUnavailableException()
        : base(SafeMessage)
    {
    }

}

internal interface INsiIdentityReferenceProjectionWriteSink
{
    bool HasSerializableTransaction { get; }

    bool HasPendingChanges { get; }

    bool HasTrackedProjectionEntities { get; }

    Task<bool> CodesMatchDatabaseSemanticsAsync(
        NsiIdentityReferenceProjectionWritePlan plan,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken);

    Task<int> DeactivateBudgetOwnersAsync(
        IReadOnlyList<NsiIdentityRelationKey> keys,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken);

    Task<int> DeactivateEmployeeRolesAsync(
        IReadOnlyList<NsiIdentityRelationKey> keys,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken);

    Task<int> DeactivateEmployeesAsync(
        IReadOnlyList<Guid> ids,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken);

    Task<int> DeactivateFunctionalRolesAsync(
        IReadOnlyList<Guid> ids,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken);

    Task<int> DeactivateCostCentersAsync(
        IReadOnlyList<Guid> ids,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken);

    Task<int> VacateEmployeeCodesAsync(
        IReadOnlyList<NsiIdentityCodeVacate> vacates,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken);

    Task<int> WriteEmployeesAsync(
        IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityEmployeeImportItem>> writes,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken);

    Task<int> VacateFunctionalRoleCodesAsync(
        IReadOnlyList<NsiIdentityCodeVacate> vacates,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken);

    Task<int> WriteFunctionalRolesAsync(
        IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityFunctionalRoleImportItem>> writes,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken);

    Task<int> VacateCostCenterCodesAsync(
        IReadOnlyList<NsiIdentityCodeVacate> vacates,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken);

    Task<int> WriteCostCentersAsync(
        IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityCostCenterImportItem>> writes,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken);

    Task<int> WriteEmployeeRolesAsync(
        IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityEmployeeRoleImportItem>> writes,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken);

    Task<int> WriteBudgetOwnersAsync(
        IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityBudgetOwnerImportItem>> writes,
        NsiIdentityReferenceProjectionSnapshot current,
        CancellationToken cancellationToken);

    Task<int> WriteWatermarkAsync(
        NsiIdentityReferenceImportWatermark watermark,
        NsiIdentityReferenceImportWatermark? current,
        CancellationToken cancellationToken);
}

internal sealed class NsiIdentityReferenceProjectionWriteResult
{
    private readonly IReadOnlyDictionary<string, int> affectedRows;

    internal NsiIdentityReferenceProjectionWriteResult(IEnumerable<KeyValuePair<string, int>> affectedRows)
    {
        ArgumentNullException.ThrowIfNull(affectedRows);
        var values = affectedRows.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (values.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0))
        {
            throw new ArgumentException("Write result phases and counts must be valid.", nameof(affectedRows));
        }

        this.affectedRows = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(values);
    }

    internal IReadOnlyDictionary<string, int> AffectedRows => affectedRows;

    internal long TotalAffectedRows => affectedRows.Values.Sum(value => (long)value);
}

internal static class NsiIdentityReferenceProjectionWriter
{
    internal const string CodeSemanticsPhase = "code-semantics";
    internal const string BudgetOwnerDeactivatePhase = "budget-owner-deactivate";
    internal const string EmployeeRoleDeactivatePhase = "employee-role-deactivate";
    internal const string EmployeeDeactivatePhase = "employee-deactivate";
    internal const string FunctionalRoleDeactivatePhase = "functional-role-deactivate";
    internal const string CostCenterDeactivatePhase = "cost-center-deactivate";
    internal const string EmployeeCodeVacatePhase = "employee-code-vacate";
    internal const string EmployeeWritePhase = "employee-write";
    internal const string FunctionalRoleCodeVacatePhase = "functional-role-code-vacate";
    internal const string FunctionalRoleWritePhase = "functional-role-write";
    internal const string CostCenterCodeVacatePhase = "cost-center-code-vacate";
    internal const string CostCenterWritePhase = "cost-center-write";
    internal const string EmployeeRoleWritePhase = "employee-role-write";
    internal const string BudgetOwnerWritePhase = "budget-owner-write";
    internal const string WatermarkWritePhase = "watermark-write";

    internal static async Task<NsiIdentityReferenceProjectionWriteResult> ApplyAsync(
        NsiIdentityReferenceImportPlan plan,
        NsiIdentityReferenceProjectionSnapshot current,
        INsiIdentityReferenceProjectionWriteSink sink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(sink);

        try
        {
            EnsureReady(sink, cancellationToken);
            var writePlan = CreateWritePlan(plan, current);
            if (!await sink.CodesMatchDatabaseSemanticsAsync(
                writePlan,
                current,
                cancellationToken))
            {
                throw new NsiIdentityReferenceProjectionWriteUnavailableException();
            }
            EnsureReady(sink, cancellationToken);

            var result = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [BudgetOwnerDeactivatePhase] = await ApplyPhaseAsync(
                    writePlan.BudgetOwnerDeactivations.Count,
                    token => sink.DeactivateBudgetOwnersAsync(
                        writePlan.BudgetOwnerDeactivations,
                        current,
                        token),
                    sink,
                    cancellationToken),
                [EmployeeRoleDeactivatePhase] = await ApplyPhaseAsync(
                    writePlan.EmployeeRoleDeactivations.Count,
                    token => sink.DeactivateEmployeeRolesAsync(
                        writePlan.EmployeeRoleDeactivations,
                        current,
                        token),
                    sink,
                    cancellationToken),
                [EmployeeDeactivatePhase] = await ApplyPhaseAsync(
                    writePlan.EmployeeDeactivations.Count,
                    token => sink.DeactivateEmployeesAsync(
                        writePlan.EmployeeDeactivations,
                        current,
                        token),
                    sink,
                    cancellationToken),
                [FunctionalRoleDeactivatePhase] = await ApplyPhaseAsync(
                    writePlan.FunctionalRoleDeactivations.Count,
                    token => sink.DeactivateFunctionalRolesAsync(
                        writePlan.FunctionalRoleDeactivations,
                        current,
                        token),
                    sink,
                    cancellationToken),
                [CostCenterDeactivatePhase] = await ApplyPhaseAsync(
                    writePlan.CostCenterDeactivations.Count,
                    token => sink.DeactivateCostCentersAsync(
                        writePlan.CostCenterDeactivations,
                        current,
                        token),
                    sink,
                    cancellationToken),
                [EmployeeCodeVacatePhase] = await ApplyPhaseAsync(
                    writePlan.EmployeeCodeVacates.Count,
                    token => sink.VacateEmployeeCodesAsync(
                        writePlan.EmployeeCodeVacates,
                        current,
                        token),
                    sink,
                    cancellationToken),
                [EmployeeWritePhase] = await ApplyPhaseAsync(
                    writePlan.EmployeeWrites.Count,
                    token => sink.WriteEmployeesAsync(writePlan.EmployeeWrites, current, token),
                    sink,
                    cancellationToken),
                [FunctionalRoleCodeVacatePhase] = await ApplyPhaseAsync(
                    writePlan.FunctionalRoleCodeVacates.Count,
                    token => sink.VacateFunctionalRoleCodesAsync(
                        writePlan.FunctionalRoleCodeVacates,
                        current,
                        token),
                    sink,
                    cancellationToken),
                [FunctionalRoleWritePhase] = await ApplyPhaseAsync(
                    writePlan.FunctionalRoleWrites.Count,
                    token => sink.WriteFunctionalRolesAsync(
                        writePlan.FunctionalRoleWrites,
                        current,
                        token),
                    sink,
                    cancellationToken),
                [CostCenterCodeVacatePhase] = await ApplyPhaseAsync(
                    writePlan.CostCenterCodeVacates.Count,
                    token => sink.VacateCostCenterCodesAsync(
                        writePlan.CostCenterCodeVacates,
                        current,
                        token),
                    sink,
                    cancellationToken),
                [CostCenterWritePhase] = await ApplyPhaseAsync(
                    writePlan.CostCenterWrites.Count,
                    token => sink.WriteCostCentersAsync(writePlan.CostCenterWrites, current, token),
                    sink,
                    cancellationToken),
                [EmployeeRoleWritePhase] = await ApplyPhaseAsync(
                    writePlan.EmployeeRoleWrites.Count,
                    token => sink.WriteEmployeeRolesAsync(
                        writePlan.EmployeeRoleWrites,
                        current,
                        token),
                    sink,
                    cancellationToken),
                [BudgetOwnerWritePhase] = await ApplyPhaseAsync(
                    writePlan.BudgetOwnerWrites.Count,
                    token => sink.WriteBudgetOwnersAsync(writePlan.BudgetOwnerWrites, current, token),
                    sink,
                    cancellationToken),
                [WatermarkWritePhase] = await ApplyPhaseAsync(
                    writePlan.WriteWatermark ? 1 : 0,
                    token => sink.WriteWatermarkAsync(
                        writePlan.Watermark,
                        current.ImportWatermark,
                        token),
                    sink,
                    cancellationToken)
            };

            EnsureReady(sink, cancellationToken);
            return new NsiIdentityReferenceProjectionWriteResult(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NsiIdentityReferenceProjectionWriteUnavailableException)
        {
            throw;
        }
    }

    private static NsiIdentityReferenceProjectionWritePlan CreateWritePlan(
        NsiIdentityReferenceImportPlan plan,
        NsiIdentityReferenceProjectionSnapshot current)
    {
        try
        {
            return NsiIdentityReferenceProjectionWritePlan.Create(plan, current);
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidOperationException)
        {
            throw new NsiIdentityReferenceProjectionWriteUnavailableException();
        }
    }

    private static async Task<int> ApplyPhaseAsync(
        int expectedAffectedRows,
        Func<CancellationToken, Task<int>> apply,
        INsiIdentityReferenceProjectionWriteSink sink,
        CancellationToken cancellationToken)
    {
        if (expectedAffectedRows == 0)
        {
            return 0;
        }

        EnsureReady(sink, cancellationToken);
        var affectedRows = await apply(cancellationToken);
        EnsureReady(sink, cancellationToken);
        if (affectedRows != expectedAffectedRows)
        {
            throw new NsiIdentityReferenceProjectionWriteUnavailableException();
        }
        return affectedRows;
    }

    private static void EnsureReady(
        INsiIdentityReferenceProjectionWriteSink sink,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!sink.HasSerializableTransaction ||
            sink.HasPendingChanges ||
            sink.HasTrackedProjectionEntities)
        {
            throw new NsiIdentityReferenceProjectionWriteUnavailableException();
        }
    }
}

internal sealed class NsiIdentityCodeVacate
{
    internal NsiIdentityCodeVacate(Guid id, string temporaryCode, int maximumLength)
    {
        NsiIdentityReferenceImportValidation.Identifier(id, nameof(id), "Code-vacate id");
        TemporaryCode = NsiImportText.Validate(
            temporaryCode,
            maximumLength,
            nameof(temporaryCode),
            "Temporary code");
        Id = id;
    }

    internal Guid Id { get; }

    internal string TemporaryCode { get; }
}

internal sealed class NsiIdentityReferenceProjectionWritePlan
{
    private NsiIdentityReferenceProjectionWritePlan(
        NsiIdentityReferenceImportPlan plan,
        NsiIdentityReferenceProjectionSnapshot current)
    {
        BudgetOwnerDeactivations = plan.BudgetOwnerDeactivations;
        var currentEmployeeRoles = current.EmployeeRoles.ToDictionary(item =>
            new NsiIdentityRelationKey(item.EmployeeId, item.FunctionalRoleId));
        EmployeeRoleDeactivations = plan.EmployeeRoleDeactivations
            .Concat(plan.EmployeeRoles
                .Where(item =>
                    item.Operation is NsiReferenceImportOperation.Update &&
                    !item.Item.IsActive &&
                    currentEmployeeRoles[
                        new NsiIdentityRelationKey(
                            item.Item.EmployeeId,
                            item.Item.FunctionalRoleId)].IsActive)
                .Select(item => new NsiIdentityRelationKey(
                    item.Item.EmployeeId,
                    item.Item.FunctionalRoleId)))
            .Distinct()
            .OrderBy(item => item.FirstId)
            .ThenBy(item => item.SecondId)
            .ToArray();
        EmployeeDeactivations = plan.EmployeeDeactivations;
        FunctionalRoleDeactivations = plan.FunctionalRoleDeactivations;
        CostCenterDeactivations = plan.CostCenterDeactivations;
        ValidateCanonicalPlan(plan, current);
        EmployeeWrites = Writes(plan.Employees, item => item.Item.Id);
        FunctionalRoleWrites = Writes(plan.FunctionalRoles, item => item.Item.Id);
        CostCenterWrites = CostCenterWritesParentFirst(plan.CostCenters);
        var earlyEmployeeRoleDeactivations = EmployeeRoleDeactivations.ToHashSet();
        EmployeeRoleWrites = Writes(
            plan.EmployeeRoles.Where(item => !earlyEmployeeRoleDeactivations.Contains(
                new NsiIdentityRelationKey(
                    item.Item.EmployeeId,
                    item.Item.FunctionalRoleId))),
            item => item.Item.EmployeeId,
            item => item.Item.FunctionalRoleId);
        BudgetOwnerWrites = Writes(
            plan.BudgetOwners,
            item => item.Item.CostCenterId,
            item => item.Item.EmployeeId);

        var employees = current.Employees.ToDictionary(item => item.Id);
        EmployeeCodeVacates = BuildCodeVacates(
            EmployeeWrites
            .Where(item => item.Operation is NsiReferenceImportOperation.Update &&
                !string.Equals(employees[item.Item.Id].Code, item.Item.Code, StringComparison.Ordinal))
            .Select(item => item.Item.Id)
            .Order(),
            current.Employees.Select(item => item.Code)
                .Concat(plan.Employees.Select(item => item.Item.Code)),
            StringComparer.Ordinal,
            maximumLength: 20);

        var roles = current.FunctionalRoles.ToDictionary(item => item.Id);
        FunctionalRoleCodeVacates = BuildCodeVacates(
            FunctionalRoleWrites
            .Where(item => item.Operation is NsiReferenceImportOperation.Update &&
                !string.Equals(roles[item.Item.Id].Code, item.Item.TargetCode, StringComparison.Ordinal))
            .Select(item => item.Item.Id)
            .Order(),
            current.FunctionalRoles.Select(item => item.Code)
                .Concat(plan.FunctionalRoles.Select(item => item.Item.TargetCode)),
            StringComparer.Ordinal,
            maximumLength: 30);

        var costCenters = current.CostCenters.ToDictionary(item => item.Id);
        CostCenterCodeVacates = BuildCodeVacates(
            CostCenterWrites
            .Where(item => item.Operation is NsiReferenceImportOperation.Update &&
                !string.Equals(costCenters[item.Item.Id].Code, item.Item.Code, StringComparison.Ordinal))
            .Select(item => item.Item.Id)
            .Order(),
            current.CostCenters.Select(item => item.Code)
                .Concat(plan.CostCenters.Select(item => item.Item.Code)),
            StringComparer.OrdinalIgnoreCase,
            maximumLength: 100);

        Watermark = NsiIdentityReferenceImportWatermark.Create(
            plan.AsOf,
            plan.CapturedAtUtc,
            plan.SourceConfigurationSha256,
            plan.SourceSnapshotSha256);
        WriteWatermark = !WatermarksEqual(Watermark, current.ImportWatermark);
    }

    internal IReadOnlyList<NsiIdentityRelationKey> BudgetOwnerDeactivations { get; }
    internal IReadOnlyList<NsiIdentityRelationKey> EmployeeRoleDeactivations { get; }
    internal IReadOnlyList<Guid> EmployeeDeactivations { get; }
    internal IReadOnlyList<Guid> FunctionalRoleDeactivations { get; }
    internal IReadOnlyList<Guid> CostCenterDeactivations { get; }
    internal IReadOnlyList<NsiIdentityCodeVacate> EmployeeCodeVacates { get; }
    internal IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityEmployeeImportItem>> EmployeeWrites { get; }
    internal IReadOnlyList<NsiIdentityCodeVacate> FunctionalRoleCodeVacates { get; }
    internal IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityFunctionalRoleImportItem>> FunctionalRoleWrites { get; }
    internal IReadOnlyList<NsiIdentityCodeVacate> CostCenterCodeVacates { get; }
    internal IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityCostCenterImportItem>> CostCenterWrites { get; }
    internal IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityEmployeeRoleImportItem>> EmployeeRoleWrites { get; }
    internal IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityBudgetOwnerImportItem>> BudgetOwnerWrites { get; }
    internal NsiIdentityReferenceImportWatermark Watermark { get; }
    internal bool WriteWatermark { get; }

    internal static NsiIdentityReferenceProjectionWritePlan Create(
        NsiIdentityReferenceImportPlan plan,
        NsiIdentityReferenceProjectionSnapshot current) => new(plan, current);

    private static void ValidateCanonicalPlan(
        NsiIdentityReferenceImportPlan plan,
        NsiIdentityReferenceProjectionSnapshot current)
    {
        const string message =
            "The identity reference write plan is inconsistent with the current projection.";
        try
        {
            var batch = NsiIdentityReferenceImportBatch.CreateCompleteSnapshot(
                plan.AsOf,
                plan.CapturedAtUtc,
                plan.SourceConfigurationSha256,
                true,
                plan.Employees.Select(item => item.Item),
                plan.FunctionalRoles.Select(item => item.Item),
                plan.CostCenters.Select(item => item.Item),
                plan.EmployeeRoles.Select(item => item.Item),
                plan.BudgetOwners.Select(item => item.Item));
            var canonical = NsiIdentityReferenceImportPlanBuilder.Resolve(batch, current);
            if (!canonical.IsReady || canonical.Plan is null ||
                !PlanMatches(plan, canonical.Plan))
            {
                throw new InvalidOperationException(message);
            }
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(message, exception);
        }
    }

    private static bool PlanMatches(
        NsiIdentityReferenceImportPlan candidate,
        NsiIdentityReferenceImportPlan canonical) =>
        candidate.AsOf == canonical.AsOf &&
        candidate.CapturedAtUtc == canonical.CapturedAtUtc &&
        string.Equals(
            candidate.SourceConfigurationSha256,
            canonical.SourceConfigurationSha256,
            StringComparison.Ordinal) &&
        string.Equals(
            candidate.SourceSnapshotSha256,
            canonical.SourceSnapshotSha256,
            StringComparison.Ordinal) &&
        AssignmentsMatch(candidate.Employees, canonical.Employees) &&
        candidate.EmployeeDeactivations.SequenceEqual(canonical.EmployeeDeactivations) &&
        AssignmentsMatch(candidate.FunctionalRoles, canonical.FunctionalRoles) &&
        candidate.FunctionalRoleDeactivations.SequenceEqual(canonical.FunctionalRoleDeactivations) &&
        AssignmentsMatch(candidate.CostCenters, canonical.CostCenters) &&
        candidate.CostCenterDeactivations.SequenceEqual(canonical.CostCenterDeactivations) &&
        AssignmentsMatch(candidate.EmployeeRoles, canonical.EmployeeRoles) &&
        candidate.EmployeeRoleDeactivations.SequenceEqual(canonical.EmployeeRoleDeactivations) &&
        AssignmentsMatch(candidate.BudgetOwners, canonical.BudgetOwners) &&
        candidate.BudgetOwnerDeactivations.SequenceEqual(canonical.BudgetOwnerDeactivations);

    private static bool AssignmentsMatch<T>(
        IReadOnlyList<NsiIdentityImportAssignment<T>> candidate,
        IReadOnlyList<NsiIdentityImportAssignment<T>> canonical)
        where T : class
    {
        if (candidate.Count != canonical.Count) return false;
        for (var index = 0; index < candidate.Count; index++)
        {
            if (candidate[index].Operation != canonical[index].Operation ||
                !ReferenceEquals(candidate[index].Item, canonical[index].Item))
            {
                return false;
            }
        }
        return true;
    }

    private static IReadOnlyList<NsiIdentityCodeVacate> BuildCodeVacates(
        IEnumerable<Guid> ids,
        IEnumerable<string> reservedCodes,
        IEqualityComparer<string> comparer,
        int maximumLength)
    {
        var reserved = new HashSet<string>(reservedCodes, comparer);
        ulong sequence = 0;
        var result = new List<NsiIdentityCodeVacate>();
        foreach (var id in ids)
        {
            while (true)
            {
                var candidate = $"~{sequence:X16}";
                if (reserved.Add(candidate))
                {
                    result.Add(new NsiIdentityCodeVacate(id, candidate, maximumLength));
                    if (sequence != ulong.MaxValue) sequence++;
                    break;
                }
                if (sequence == ulong.MaxValue)
                {
                    throw new InvalidOperationException(
                        "The temporary identity-code namespace is exhausted.");
                }
                sequence++;
            }
        }
        return result;
    }

    private static IReadOnlyList<NsiIdentityImportAssignment<T>> Writes<T>(
        IEnumerable<NsiIdentityImportAssignment<T>> assignments,
        Func<NsiIdentityImportAssignment<T>, Guid> first,
        Func<NsiIdentityImportAssignment<T>, Guid>? second = null)
        where T : class
    {
        var writes = assignments.Where(item => item.Operation is not NsiReferenceImportOperation.Unchanged);
        return (second is null
                ? writes.OrderBy(first)
                : writes.OrderBy(first).ThenBy(second))
            .ToArray();
    }

    private static IReadOnlyList<NsiIdentityImportAssignment<NsiIdentityCostCenterImportItem>>
        CostCenterWritesParentFirst(
            IEnumerable<NsiIdentityImportAssignment<NsiIdentityCostCenterImportItem>> assignments)
    {
        var writes = assignments
            .Where(item => item.Operation is not NsiReferenceImportOperation.Unchanged)
            .ToArray();
        var byId = assignments.ToDictionary(item => item.Item.Id, item => item.Item);
        var depth = new Dictionary<Guid, int>();

        int ResolveDepth(Guid id)
        {
            if (depth.TryGetValue(id, out var known)) return known;
            var item = byId[id];
            var value = item.ParentId is Guid parentId ? checked(ResolveDepth(parentId) + 1) : 0;
            depth.Add(id, value);
            return value;
        }

        return writes
            .OrderBy(item => ResolveDepth(item.Item.Id))
            .ThenBy(item => item.Item.Id)
            .ToArray();
    }

    private static bool WatermarksEqual(
        NsiIdentityReferenceImportWatermark left,
        NsiIdentityReferenceImportWatermark? right) =>
        right is not null &&
        left.AsOf == right.AsOf &&
        left.CapturedAtUtc == right.CapturedAtUtc &&
        string.Equals(
            left.SourceConfigurationSha256,
            right.SourceConfigurationSha256,
            StringComparison.Ordinal) &&
        string.Equals(
            left.SourceSnapshotSha256,
            right.SourceSnapshotSha256,
            StringComparison.Ordinal);
}
