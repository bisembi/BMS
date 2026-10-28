using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using Bms.Application.Abstractions;
using Bms.Application.Budgeting;
using Bms.Application.Common;
using Bms.Application.Authorization;
using System.Text.Json;
using Bms.Domain.Budgeting;
using Bms.Domain.ExpenseRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class BudgetRepository(BmsDbContext dbContext,
    IBudgetCashConsumptionReader? cashConsumption = null) : IBudgetRepository
{
    private const int ApplicationLockTimeoutMilliseconds = 15_000;

    public async Task<IReadOnlyDictionary<BudgetKey,RequestBudgetControl343Dto>?> ReadRequestControl343Async(Guid requestId,CancellationToken token)
    {
        if(!dbContext.BudgetRegisterEnabled343)return null;
        var frame=await new BudgetExecutionFrameReader343(dbContext).ReadAsync(null,null,false,token);
        if(!frame.Evaluation.Decisions.TryGetValue(requestId,out var decision))return new Dictionary<BudgetKey,RequestBudgetControl343Dto>();
        var saved=decision.Frozen?frame.State.Frozen.Single(f=>f.RequestId==requestId&&f.FinancialFingerprint==frame.Fingerprints[requestId]):null;
        var historic=saved is null?[]:JsonSerializer.Deserialize<RegisterFrozenPosition343[]>(saved.PositionsJson,new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ??throw new InvalidOperationException("Frozen budget position evidence missing.");
        return decision.Lines.ToDictionary(l=>l.Key,l=>new RequestBudgetControl343Dto(l.AvailableBeforeKzt,l.AvailableAfterKzt,
            decision.Frozen?decision.PositiveDeficitKzt==0m?0m:historic.SingleOrDefault(h=>h.CostCenterId==l.Key.CostCenterId
                &&h.CashFlowItemId==l.Key.CashFlowItemId&&h.BudgetMonth==l.Key.BudgetMonth)?.PositiveDeficitKzt:Math.Max(0m,-l.AvailableAfterKzt),decision.Frozen));
    }

    public async Task<IReadOnlyDictionary<Guid,BudgetExecutionDecision343>?> ReadFifoDecisions343Async(
        IReadOnlyCollection<Guid> requestIds,CancellationToken token)
    {
        if(!dbContext.BudgetRegisterEnabled343)return null;
        if(requestIds.Any(id=>id==Guid.Empty)||requestIds.Distinct().Count()!=requestIds.Count)
            throw new ArgumentException("Distinct request identities required.");
        var frame=await new BudgetExecutionFrameReader343(dbContext).ReadAsync(null,null,false,token);
        var ids=requestIds.ToHashSet();return frame.Evaluation.Decisions.Where(p=>ids.Contains(p.Key)).ToDictionary(p=>p.Key,p=>p.Value);
    }

    public async Task<IReadOnlyList<BudgetPosition>> ListConsumptionPositionsAsync(
        IReadOnlyCollection<BudgetKey> keys, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(keys);
        token.ThrowIfCancellationRequested();
        var distinct = keys.Distinct().ToArray();
        if (distinct.Length == 0) return [];
        if(dbContext.BudgetRegisterEnabled343)
        {
            var state=await new BudgetExecutionStorage343(dbContext).ReadAsync(token);
            var reserved=state.Values.Where(v=>v.Kind=="Reserve").GroupBy(v=>v.Key).ToDictionary(g=>g.Key,g=>g.Sum(v=>v.AmountKzt));
            var paid=state.Values.Where(v=>v.Kind=="Paid").GroupBy(v=>v.Key).ToDictionary(g=>g.Key,g=>g.Sum(v=>v.AmountKzt));
            return distinct.Select(key=>{var p=BudgetPosition.Create(key);p.SetReservedAmount(reserved.GetValueOrDefault(key));p.SetPaidAmount(paid.GetValueOrDefault(key));return p;}).ToArray();
        }
        var stored = await dbContext.BudgetPositions.AsNoTracking()
            .Where(BuildKeyPredicate(distinct)).ToDictionaryAsync(p => p.Key, token);
        var usage = cashConsumption is null ? null : await cashConsumption.ReadAsync(distinct, token);
        return distinct.Select(key =>
        {
            stored.TryGetValue(key, out var existing);
            // Deduct source cash and only the BMS settlement not represented
            // in that exact capture. Remaining request reserves stay separate.
            var projected = BudgetPosition.Create(key);
            projected.SetReservedAmount(existing?.ReservedAmountKzt ?? 0m);
            var paid = existing?.PaidAmountKzt ?? 0m;
            projected.SetPaidAmount(usage is null ? paid : usage[key].EffectivePaid(paid));
            return projected;
        }).ToArray();
    }

    public async Task<IReadOnlyList<BudgetPosition>> ListPositionsAsync(
        IReadOnlyCollection<BudgetKey> keys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var distinctKeys = keys.Distinct().ToArray();
        if (distinctKeys.Length == 0)
        {
            return [];
        }

        if(dbContext.BudgetRegisterEnabled343)
        {
            var frame=await new BudgetExecutionFrameReader343(dbContext).ReadAsync(null,null,false,cancellationToken,requestedKeys:distinctKeys);
            var byKey=frame.Openings.ToDictionary(p=>p.Key);
            var reserved=frame.Inputs.Where(r=>r.Submitted&&r.Active).SelectMany(r=>r.RemainingReservations)
                .GroupBy(p=>p.Key).ToDictionary(g=>g.Key,g=>g.Sum(p=>p.Value));
            return distinctKeys.Select(key=>
            {
                var p=byKey.GetValueOrDefault(key)??new BudgetExecutionOpening343(key,0m,0m);
                var value=BudgetPosition.Create(key,p.LimitAmountKzt);value.SetReservedAmount(reserved.GetValueOrDefault(key));value.SetPaidAmount(p.PaidAmountKzt);return value;
            }).ToArray();
        }

        var predicate = BuildKeyPredicate(distinctKeys);
        var positions = await dbContext.BudgetPositions
            .AsNoTracking()
            .Where(predicate)
            .OrderBy(position => position.BudgetMonth)
            .ThenBy(position => position.CostCenterId)
            .ThenBy(position => position.CashFlowItemId)
            .ToListAsync(cancellationToken);

        var existing = positions.ToDictionary(x => x.Key);
        var complete = distinctKeys.Select(key => existing.TryGetValue(key, out var position)
            ? position : BudgetPosition.Create(key, 0m)).ToArray();
        return await ProjectEffectiveLimitsAsync(complete, cancellationToken);
    }

    public async Task<IReadOnlyList<BudgetPosition>> ListPositionsAsync(
        Guid? costCenterId,
        Guid? cashFlowItemId,
        DateOnly? budgetMonth,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        var slice = await ReadExecutionSliceAsync(costCenterId, cashFlowItemId, budgetMonth, skip, take, cancellationToken);
        return slice.Page.Select(row =>
        {
            var p = BudgetPosition.Create(BudgetKey.Create(row.CostCenterId, row.CashFlowItemId, row.BudgetMonth), row.LimitAmountKzt);
            p.SetReservedAmount(row.ReservedAmountKzt); p.SetPaidAmount(row.PaidAmountKzt); return p;
        }).ToArray();
    }

    private async Task<IReadOnlyList<BudgetPosition>> ProjectEffectiveLimitsAsync(
        IReadOnlyList<BudgetPosition> positions,
        CancellationToken cancellationToken)
    {
        if (positions.Count == 0)
        {
            return positions;
        }

        // Every request-control surface projects the same published limit. The legacy row
        // remains untouched; the latest approved version is selected at read
        // time and a key outside all approved versions is zero.
        var costCenterIds = positions.Select(position => position.CostCenterId).Distinct().ToArray();
        var versions = await CompanyBudgetPlanReader.ReadActiveLimitsAsync(dbContext, costCenterIds, cancellationToken);
        var consumption = cashConsumption is null ? null
            : await cashConsumption.ReadAsync(positions.Select(p => p.Key).ToArray(), cancellationToken);

        return positions.Select(position =>
        {
            var limit = BudgetEffectiveLimitResolver.Resolve(
                position.LimitAmountKzt,
                position.CostCenterId,
                position.CashFlowItemId,
                position.BudgetMonth,
                versions);
            var projected = BudgetPosition.Create(position.Key, limit);
            projected.SetReservedAmount(position.ReservedAmountKzt);
            projected.SetPaidAmount(consumption is null ? position.PaidAmountKzt
                : consumption[position.Key].EffectivePaid(position.PaidAmountKzt));
            return projected;
        }).ToArray();
    }

    internal static BudgetEffectiveVersion ToEffectiveVersion(BudgetInstance instance) =>
        new(
            instance.Id,
            instance.CostCenterId,
            instance.ValidFrom,
            instance.ValidTo,
            instance.ApprovalOrdinal
                ?? throw new InvalidOperationException("Approved budget has no approval ordinal."),
            instance.ApprovedAtUtc
                ?? throw new InvalidOperationException("Approved budget has no approval timestamp."),
            instance.Lines.Where(line => line.FlowDirection == BudgetFlowDirection.Payment)
                .Select(line => new BudgetEffectiveLine(
                line.CashFlowItemId,
                line.BudgetMonth,
                line.LimitAmountKzt)).ToArray());

    public async Task<int> CountPositionsAsync(
        Guid? costCenterId,
        Guid? cashFlowItemId,
        DateOnly? budgetMonth,
        CancellationToken cancellationToken) =>
        (await ReadExecutionSliceAsync(costCenterId, cashFlowItemId, budgetMonth, 0, 1, cancellationToken)).Total;

    /// <summary>
    /// One captured slice of the filtered execution area. A single batch
    /// materialises the matching positions once, and the count, the sums and the
    /// page are all read out of that one table variable, so a page can never
    /// describe a different set from the totals beside it.
    ///
    /// The table variable contains the effective published limit: the latest
    /// approved version wins, an omitted line is zero, and an unset legacy
    /// value is the agreed zero. Nullable columns remain in the transport shape
    /// so a future fail-closed source cannot silently change the totals rule.
    /// </summary>
    public Task<BudgetPositionSlice> ReadExecutionSliceAsync(
        Guid? costCenterId,
        Guid? cashFlowItemId,
        DateOnly? budgetMonth,
        int skip,
        int take,
        CancellationToken cancellationToken) =>
        ReadExecutionSliceAsync(costCenterId, cashFlowItemId, budgetMonth, skip, take, BusinessDataReadScope.All, cancellationToken);

    public async Task<BudgetPositionSlice> ReadExecutionSliceAsync(
        Guid? costCenterId,
        Guid? cashFlowItemId,
        DateOnly? budgetMonth,
        int skip,
        int take,
        BusinessDataReadScope scope, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);

        if (cashConsumption is not null)
        {
            // Compute source consumption for the captured area BEFORE paging;
            // totals cannot be rebuilt from a single page.
            var full = await ReadExecutionExportAsync(costCenterId,cashFlowItemId,budgetMonth,scope,cancellationToken);
            return new(full.Rows.Skip(skip).Take(take).Select(r=>new BudgetPositionSnapshot(r.CostCenterId,
                r.CashFlowItemId,r.BudgetMonth,r.LimitAmountKzt,r.ReservedAmountKzt,r.PaidAmountKzt)).ToArray(),
                full.Total,full.ConfiguredLimitCount,full.UnconfiguredLimitCount,full.ConfiguredLimitAmountKzt,
                full.ReservedAmountKzt,full.PaidAmountKzt) { FactSource=full.FactSource };
        }

        _ = await CompanyBudgetPlanReader.ActiveAsync(dbContext, cancellationToken);

        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = ExecutionSliceBatch;
            if (dbContext.Database.CurrentTransaction is { } ambient)
            {
                command.Transaction = ambient.GetDbTransaction();
            }

            AddNullableParameter(command, "@readScopeCfoIds", DbType.String,
                scope.CanReadAll ? null : JsonSerializer.Serialize(scope.CostCenterIds));
            AddNullableParameter(command, "@costCenterId", DbType.Guid, costCenterId);
            AddNullableParameter(command, "@cashFlowItemId", DbType.Guid, cashFlowItemId);
            AddNullableParameter(
                command,
                "@budgetMonth",
                DbType.Date,
                budgetMonth.HasValue
                    ? new DateOnly(budgetMonth.Value.Year, budgetMonth.Value.Month, 1)
                        .ToDateTime(TimeOnly.MinValue)
                    : null);
            AddParameter(command, "@skip", DbType.Int32, skip);
            AddParameter(command, "@take", DbType.Int32, take);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "The budget execution slice returned no aggregate row.");
            }

            var total = reader.GetInt32(0);
            var configuredLimitCount = reader.GetInt32(1);
            var unconfiguredLimitCount = reader.GetInt32(2);
            var configuredLimitAmount = reader.GetDecimal(3);
            var reserved = reader.GetDecimal(4);
            var paid = reader.GetDecimal(5);

            if (!await reader.NextResultAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "The budget execution slice returned no page.");
            }

            var page = new List<BudgetPositionSnapshot>(take);
            while (await reader.ReadAsync(cancellationToken))
            {
                page.Add(new BudgetPositionSnapshot(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    DateOnly.FromDateTime(reader.GetDateTime(2)),
                    reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                    reader.GetDecimal(4),
                    reader.GetDecimal(5)));
            }

            return new BudgetPositionSlice(
                page,
                total,
                configuredLimitCount,
                unconfiguredLimitCount,
                configuredLimitAmount,
                reserved,
                paid);
        }
        finally
        {
            if (openedHere)
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    public Task<BudgetExecutionExportSlice> ReadExecutionExportAsync(
        Guid? costCenterId,
        Guid? cashFlowItemId,
        DateOnly? budgetMonth,
        CancellationToken cancellationToken) =>
        ReadExecutionExportAsync(costCenterId, cashFlowItemId, budgetMonth, BusinessDataReadScope.All, cancellationToken);

    public async Task<BudgetExecutionExportSlice> ReadExecutionExportAsync(
        Guid? costCenterId,
        Guid? cashFlowItemId,
        DateOnly? budgetMonth,
        BusinessDataReadScope scope, CancellationToken cancellationToken)
    {
        _ = await CompanyBudgetPlanReader.ActiveAsync(dbContext, cancellationToken);
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = ExecutionExportBatch;
            if (dbContext.Database.CurrentTransaction is { } ambient)
            {
                command.Transaction = ambient.GetDbTransaction();
            }

            AddNullableParameter(command, "@readScopeCfoIds", DbType.String,
                scope.CanReadAll ? null : JsonSerializer.Serialize(scope.CostCenterIds));
            AddNullableParameter(command, "@costCenterId", DbType.Guid, costCenterId);
            AddNullableParameter(command, "@cashFlowItemId", DbType.Guid, cashFlowItemId);
            AddNullableParameter(
                command,
                "@budgetMonth",
                DbType.Date,
                budgetMonth.HasValue
                    ? new DateOnly(budgetMonth.Value.Year, budgetMonth.Value.Month, 1)
                        .ToDateTime(TimeOnly.MinValue)
                    : null);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "The budget execution export returned no aggregate row.");
            }

            var total = reader.GetInt32(0);
            var configuredLimitCount = reader.GetInt32(1);
            var unconfiguredLimitCount = reader.GetInt32(2);
            var configuredLimitAmount = reader.GetDecimal(3);
            var reserved = reader.GetDecimal(4);
            var paid = reader.GetDecimal(5);

            // The aggregate row arrives before the row result so an oversized
            // export is refused before a list is allocated or row data is read.
            if (total > BudgetExecutionExportPolicy.MaximumRows)
            {
                throw new BudgetExecutionExportTooLargeException(
                    total,
                    BudgetExecutionExportPolicy.MaximumRows);
            }

            if (!await reader.NextResultAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "The budget execution export returned no rows.");
            }

            var rows = new List<BudgetExecutionExportRow>(total);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new BudgetExecutionExportRow(
                    DateOnly.FromDateTime(reader.GetDateTime(0)),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                    reader.GetDecimal(6),
                    reader.GetDecimal(7))
                {
                    CostCenterId = reader.GetGuid(8),
                    CashFlowItemId = reader.GetGuid(9)
                });
            }

            IReadOnlyList<BudgetExecutionExportRow> resultRows=rows;
            if(cashConsumption is not null)
            {
                await reader.DisposeAsync();
                var usage=await cashConsumption.ReadAsync(rows.Select(r=>r.Key).ToArray(),cancellationToken);
                resultRows=rows.Select(r=>ExecutionPaidExclusionPolicy.IsExcluded(r.CashFlowItemId)?r
                    :r with{PaidAmountKzt=usage[r.Key].EffectivePaid(r.PaidAmountKzt)}).ToArray();
                paid=resultRows.Sum(r=>r.PaidAmountKzt);
            }
            return new BudgetExecutionExportSlice(
                resultRows,
                total,
                configuredLimitCount,
                unconfiguredLimitCount,
                configuredLimitAmount,
                reserved,
                paid) { IncludesSourceCashConsumption=cashConsumption is not null && resultRows.Count>0,FactSource=cashConsumption?.CapturedSource };
        }
        finally
        {
            if (openedHere)
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    /// <summary>
    /// The export reads the same filtered area as the list, in the same order,
    /// with no window over it. The two catalogue joins are on the primary key of
    /// each catalogue and are outer joins, so they can only add readable text to
    /// a captured row - never drop one, never multiply one.
    /// </summary>
    private const string ExecutionExportBatch = """
        SET NOCOUNT ON;

        DECLARE @area TABLE
        (
            BudgetMonth date NOT NULL,
            CostCenterId uniqueidentifier NOT NULL,
            CashFlowItemId uniqueidentifier NOT NULL,
            LimitAmountKzt decimal(18, 2) NULL,
            ReservedAmountKzt decimal(18, 2) NOT NULL,
            PaidAmountKzt decimal(18, 2) NOT NULL,
            PRIMARY KEY (BudgetMonth, CostCenterId, CashFlowItemId)
        );

        INSERT INTO @area
            (BudgetMonth, CostCenterId, CashFlowItemId,
             LimitAmountKzt, ReservedAmountKzt, PaidAmountKzt)
        SELECT p.BudgetMonth, p.CostCenterId, p.CashFlowItemId,
               p.LimitAmountKzt, p.ReservedAmountKzt, p.PaidAmountKzt
        FROM
        (
            SELECT
            p.BudgetMonth,
            p.CostCenterId,
            p.CashFlowItemId,
            p.LimitAmountKzt,
            p.ReservedAmountKzt,
            p.PaidAmountKzt
            FROM bms_web.v_CompanyBudgetExecutionPositions AS p
            WHERE (@readScopeCfoIds IS NULL OR p.CostCenterId IN
                (SELECT [value] FROM OPENJSON(@readScopeCfoIds) WITH ([value] uniqueidentifier '$')))
              AND (@costCenterId IS NULL OR p.CostCenterId = @costCenterId)
              AND (@cashFlowItemId IS NULL OR p.CashFlowItemId = @cashFlowItemId)
              AND (@budgetMonth IS NULL OR p.BudgetMonth = @budgetMonth)
        ) AS p;

        SELECT
            COUNT(*),
            ISNULL(SUM(CASE WHEN a.LimitAmountKzt IS NULL THEN 0 ELSE 1 END), 0),
            ISNULL(SUM(CASE WHEN a.LimitAmountKzt IS NULL THEN 1 ELSE 0 END), 0),
            ISNULL(SUM(a.LimitAmountKzt), CAST(0 AS decimal(18, 2))),
            ISNULL(SUM(a.ReservedAmountKzt), CAST(0 AS decimal(18, 2))),
            ISNULL(SUM(a.PaidAmountKzt), CAST(0 AS decimal(18, 2)))
        FROM @area AS a;

        SELECT
            a.BudgetMonth,
            cc.Code,
            cc.Name,
            cfi.Code,
            cfi.Name,
            a.LimitAmountKzt,
            a.ReservedAmountKzt,
            a.PaidAmountKzt,
            a.CostCenterId,
            a.CashFlowItemId
        FROM @area AS a
        LEFT JOIN bms_web.CostCenters AS cc ON cc.Id = a.CostCenterId
        LEFT JOIN bms_web.CashFlowItems AS cfi ON cfi.Id = a.CashFlowItemId
        ORDER BY a.BudgetMonth DESC, a.CostCenterId, a.CashFlowItemId;
        """;

    /// <summary>
    /// The page order is the one the execution list has always used: newest
    /// budget month first, then cost centre, then cash flow item. The key triple
    /// is unique, so that order is total and needs no tie-break.
    /// </summary>
    private const string ExecutionSliceBatch = """
        SET NOCOUNT ON;

        DECLARE @scope TABLE
        (
            BudgetMonth date NOT NULL,
            CostCenterId uniqueidentifier NOT NULL,
            CashFlowItemId uniqueidentifier NOT NULL,
            LimitAmountKzt decimal(18, 2) NULL,
            ReservedAmountKzt decimal(18, 2) NOT NULL,
            PaidAmountKzt decimal(18, 2) NOT NULL,
            PRIMARY KEY (BudgetMonth, CostCenterId, CashFlowItemId)
        );

        INSERT INTO @scope
            (BudgetMonth, CostCenterId, CashFlowItemId,
             LimitAmountKzt, ReservedAmountKzt, PaidAmountKzt)
        SELECT p.BudgetMonth, p.CostCenterId, p.CashFlowItemId,
               p.LimitAmountKzt, p.ReservedAmountKzt, p.PaidAmountKzt
        FROM
        (
            SELECT
            p.BudgetMonth,
            p.CostCenterId,
            p.CashFlowItemId,
            p.LimitAmountKzt,
            p.ReservedAmountKzt,
            p.PaidAmountKzt
            FROM bms_web.v_CompanyBudgetExecutionPositions AS p
            WHERE (@readScopeCfoIds IS NULL OR p.CostCenterId IN
                (SELECT [value] FROM OPENJSON(@readScopeCfoIds) WITH ([value] uniqueidentifier '$')))
              AND (@costCenterId IS NULL OR p.CostCenterId = @costCenterId)
              AND (@cashFlowItemId IS NULL OR p.CashFlowItemId = @cashFlowItemId)
              AND (@budgetMonth IS NULL OR p.BudgetMonth = @budgetMonth)
        ) AS p;

        SELECT
            COUNT(*),
            ISNULL(SUM(CASE WHEN s.LimitAmountKzt IS NULL THEN 0 ELSE 1 END), 0),
            ISNULL(SUM(CASE WHEN s.LimitAmountKzt IS NULL THEN 1 ELSE 0 END), 0),
            ISNULL(SUM(s.LimitAmountKzt), CAST(0 AS decimal(18, 2))),
            ISNULL(SUM(s.ReservedAmountKzt), CAST(0 AS decimal(18, 2))),
            ISNULL(SUM(s.PaidAmountKzt), CAST(0 AS decimal(18, 2)))
        FROM @scope AS s;

        SELECT
            s.CostCenterId,
            s.CashFlowItemId,
            s.BudgetMonth,
            s.LimitAmountKzt,
            s.ReservedAmountKzt,
            s.PaidAmountKzt
        FROM @scope AS s
        ORDER BY s.BudgetMonth DESC, s.CostCenterId, s.CashFlowItemId
        OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
        """;

    private static void AddNullableParameter(
        DbCommand command,
        string name,
        DbType type,
        object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    public Task<IReadOnlyDictionary<Guid, bool>> ReadCancelledSubmissionBudgetIndicatorsAsync(
        IReadOnlyCollection<Guid> expenseRequestIds, CancellationToken cancellationToken) =>
        CancelledRequestBudgetIndicatorReader.ReadAsync(dbContext, expenseRequestIds, cancellationToken);

    public async Task<IReadOnlyList<BudgetRequestProjection>> ListRequestProjectionsAsync(
        IReadOnlyCollection<Guid> expenseRequestIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expenseRequestIds);
        if (expenseRequestIds.Count == 0)
        {
            return [];
        }

        var requestIds = expenseRequestIds.Distinct().ToArray();
        return await dbContext.BudgetRequestProjections
            .AsNoTracking()
            .Where(projection => requestIds.Contains(projection.ExpenseRequestId))
            .OrderBy(projection => projection.ExpenseRequestId)
            .ThenBy(projection => projection.ExpenseRequestLineId)
            .ToListAsync(cancellationToken);
    }

    public Task<IReadOnlyList<BudgetRequestProjection>> ReplaceForRequestAsync(
        Guid expenseRequestId,IReadOnlyCollection<BudgetRequestProjection> desired,CancellationToken cancellationToken) =>
        ReplaceCoreAsync(expenseRequestId,desired,false,cancellationToken);

    public Task<IReadOnlyList<BudgetRequestProjection>> RedistributeForApprovalAsync(
        Guid expenseRequestId,IReadOnlyCollection<BudgetRequestProjection> desired,CancellationToken cancellationToken) =>
        ReplaceCoreAsync(expenseRequestId,desired,true,cancellationToken);

    private async Task<IReadOnlyList<BudgetRequestProjection>> ReplaceCoreAsync(
        Guid expenseRequestId,
        IReadOnlyCollection<BudgetRequestProjection> desired,
        bool preserveBudgetTotals,CancellationToken cancellationToken)
    {
        if (expenseRequestId == Guid.Empty)
        {
            throw new ArgumentException("Expense request id is required.", nameof(expenseRequestId));
        }

        ArgumentNullException.ThrowIfNull(desired);
        EnsureSerializableTransaction();
        ValidateDesired(expenseRequestId, desired);

        await AcquireApplicationLockAsync(
            BusinessManagedLockResources.ExpenseRequest(expenseRequestId),
            cancellationToken);

        // The request lock makes the existing key set stable for cooperating
        // writers. Use a statement-scoped planning read so two different
        // requests that share a budget key do not each retain an S range lock
        // while waiting for that same budget application's lock. The strict
        // read below remains inside the Serializable transaction.
        var existingForLockPlanning = await ReadExistingForLockPlanningAsync(
            expenseRequestId,
            cancellationToken);
        var existingByKey = SumByKey(existingForLockPlanning);
        var desiredByKey = SumByKey(desired);
        var affectedKeys = existingByKey.Keys
            .Concat(desiredByKey.Keys)
            .Distinct()
            .OrderBy(key => key.CostCenterId.ToString("N"), StringComparer.Ordinal)
            .ThenBy(key => key.CashFlowItemId.ToString("N"), StringComparer.Ordinal)
            .ThenBy(key => key.BudgetMonth)
            .ToArray();

        // Application locks make absent-row locking deterministic as well. The table
        // locks below remain the database invariant boundary and protect against
        // writers that use the same EF repository but a different application node.
        var budgetLockResources = affectedKeys
            .Select(BusinessManagedLockResources.Budget)
            .ToArray();
        foreach (var resource in budgetLockResources.Length == 0
                     ? []
                     : BusinessManagedLockResources.Order(budgetLockResources))
        {
            await AcquireApplicationLockAsync(resource, cancellationToken);
        }

        // Re-read under the settled budget locks. If a writer outside the
        // protocol changed this request between planning and locking, fail
        // closed rather than applying a replacement without every required
        // budget lock.
        var existing = await ReadExistingLockedAsync(expenseRequestId, cancellationToken);
        existingByKey = SumByKey(existing);
        if(preserveBudgetTotals && (existingByKey.Count!=desiredByKey.Count
            || existingByKey.Any(p=>!desiredByKey.TryGetValue(p.Key,out var amount)||amount!=p.Value)))
            throw new BudgetProjectionDriftException("Accrual split cannot change budget-key reservation totals.");
        var lockedAffectedKeys = existingByKey.Keys
            .Concat(desiredByKey.Keys)
            .Distinct()
            .OrderBy(key => key.CostCenterId.ToString("N"), StringComparer.Ordinal)
            .ThenBy(key => key.CashFlowItemId.ToString("N"), StringComparer.Ordinal)
            .ThenBy(key => key.BudgetMonth)
            .ToArray();
        if (!affectedKeys.SequenceEqual(lockedAffectedKeys))
        {
            throw new BudgetProjectionDriftException(
                "The budget projection key set changed while budget locks were being acquired. " +
                "No replacement was written.");
        }

        foreach (var key in affectedKeys)
        {
            var positionRows = await dbContext.BudgetPositions
                .FromSqlInterpolated($$"""
                    SELECT
                        [CostCenterId], [CashFlowItemId], [BudgetMonth],
                        [LimitAmountKzt], [ReservedAmountKzt], [PaidAmountKzt],
                        [ConcurrencyStamp]
                    FROM [bms_web].[BudgetPositions]
                        WITH (UPDLOCK, HOLDLOCK, INDEX([PK_bms_web_BudgetPositions]))
                    WHERE [CostCenterId] = {{key.CostCenterId}}
                      AND [CashFlowItemId] = {{key.CashFlowItemId}}
                      AND [BudgetMonth] = {{key.BudgetMonth}}
                    """)
                .ToListAsync(cancellationToken);
            var position = positionRows.SingleOrDefault();

            var persistedForKey = await dbContext.BudgetRequestProjections
                .FromSqlInterpolated($$"""
                    SELECT
                        [Id], [ExpenseRequestId], [ExpenseRequestLineId],
                        [CostCenterId], [CashFlowItemId], [BudgetMonth], [ReservedAmountKzt]
                    FROM [bms_web].[BudgetRequestProjections]
                        WITH (UPDLOCK, HOLDLOCK, INDEX([IX_BudgetRequestProjections_BudgetKey]))
                    WHERE [CostCenterId] = {{key.CostCenterId}}
                      AND [CashFlowItemId] = {{key.CashFlowItemId}}
                      AND [BudgetMonth] = {{key.BudgetMonth}}
                    """)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            var persistedProjectionTotal = persistedForKey.Sum(
                projection => projection.ReservedAmountKzt);

            if (position is null)
            {
                if (persistedProjectionTotal != 0m)
                {
                    throw CreateDriftException(key, null, persistedProjectionTotal);
                }

                if (desiredByKey.GetValueOrDefault(key) > 0m)
                {
                    position = BudgetPosition.Create(key);
                    dbContext.BudgetPositions.Add(position);
                }
            }
            else if (position.ReservedAmountKzt != persistedProjectionTotal)
            {
                throw CreateDriftException(
                    key,
                    position.ReservedAmountKzt,
                    persistedProjectionTotal);
            }

            if (position is null)
            {
                continue;
            }

            var replacementTotal = persistedProjectionTotal
                - existingByKey.GetValueOrDefault(key)
                + desiredByKey.GetValueOrDefault(key);
            if (replacementTotal < 0m)
            {
                throw new BudgetProjectionDriftException(
                    $"Budget projection replacement would make the reserved amount negative " +
                    $"for {FormatKey(key)}. No balancing record was written.");
            }

            if(!preserveBudgetTotals)position.SetReservedAmount(replacementTotal);
        }

        // ExecuteDelete is intentional: an updated request can reuse a line id with a
        // changed key/amount. Deleting inside the active transaction avoids tracking
        // two immutable projection instances with the same primary key and also removes
        // NO ACTION dependants before obsolete request lines are deleted at SaveChanges.
        await dbContext.BudgetRequestProjections
            .Where(projection => projection.ExpenseRequestId == expenseRequestId)
            .ExecuteDeleteAsync(cancellationToken);

        // Updating a tracked aggregate can introduce a child with an application-assigned
        // Guid. Because the aggregate itself is already tracked, EF can otherwise infer
        // that child as Modified rather than Added and issue an update for a row that does
        // not exist. Resolve the state from the database under the request lock before the
        // intermediate save; this also covers incomplete drafts with no budget projection.
        dbContext.ChangeTracker.DetectChanges();
        var trackedRequestLines = dbContext.ChangeTracker
            .Entries<ExpenseRequestLine>()
            .Where(entry => entry.Entity.ExpenseRequestId == expenseRequestId)
            .ToArray();
        var persistedRequestLineIds = await dbContext.ExpenseRequestLines
            .AsNoTracking()
            .Where(line => line.ExpenseRequestId == expenseRequestId)
            .Select(line => line.Id)
            .ToHashSetAsync(cancellationToken);
        foreach (var entry in trackedRequestLines.Where(entry =>
                     entry.State != EntityState.Deleted &&
                     !persistedRequestLineIds.Contains(entry.Entity.Id)))
        {
            entry.State = EntityState.Added;
        }

        // Updating a request may replace its line collection. Hold EF's deletes for
        // lines removed from that collection until the new projections have been
        // staged: the direct projection deletion above is outside EF's change tracker,
        // so splitting this ordering explicitly avoids a NO ACTION dependency cycle.
        var deferredLineDeletes = dbContext.ChangeTracker
            .Entries<ExpenseRequestLine>()
            .Where(entry => entry.State == EntityState.Deleted &&
                entry.Entity.ExpenseRequestId == expenseRequestId)
            .ToArray();
        foreach (var entry in deferredLineDeletes)
        {
            entry.State = EntityState.Unchanged;
        }

        // A projection has NO ACTION foreign keys to both a request line and a budget
        // position. Persist their pending inserts/updates first so SQL Server never sees
        // a projection before its principals. This is still one caller-owned serializable
        // transaction: the later save of projections, audit, outbox and idempotency can
        // fail and the transaction runner will roll this intermediate flush back.
        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var entry in deferredLineDeletes)
        {
            entry.State = EntityState.Deleted;
        }
        dbContext.BudgetRequestProjections.AddRange(desired);
        return existing;
    }

    private void EnsureSerializableTransaction()
    {
        var transaction = dbContext.Database.CurrentTransaction;
        if (transaction is null ||
            transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
        {
            throw new InvalidOperationException(
                "Budget projection replacement requires an active Serializable transaction.");
        }
    }

    private Task<List<BudgetRequestProjection>> ReadExistingForLockPlanningAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken) => dbContext.BudgetRequestProjections
        .FromSqlInterpolated($$"""
            SELECT
                [Id], [ExpenseRequestId], [ExpenseRequestLineId],
                [CostCenterId], [CashFlowItemId], [BudgetMonth], [ReservedAmountKzt]
            FROM [bms_web].[BudgetRequestProjections]
                WITH (READCOMMITTEDLOCK, INDEX([IX_BudgetRequestProjections_ExpenseRequestId]))
            WHERE [ExpenseRequestId] = {{expenseRequestId}}
            """)
        .AsNoTracking()
        .ToListAsync(cancellationToken);

    private Task<List<BudgetRequestProjection>> ReadExistingLockedAsync(
        Guid expenseRequestId,
        CancellationToken cancellationToken) => dbContext.BudgetRequestProjections
        .FromSqlInterpolated($$"""
            SELECT
                [Id], [ExpenseRequestId], [ExpenseRequestLineId],
                [CostCenterId], [CashFlowItemId], [BudgetMonth], [ReservedAmountKzt]
            FROM [bms_web].[BudgetRequestProjections]
                WITH (HOLDLOCK, INDEX([IX_BudgetRequestProjections_ExpenseRequestId]))
            WHERE [ExpenseRequestId] = {{expenseRequestId}}
            """)
        .AsNoTracking()
        .ToListAsync(cancellationToken);

    private static void ValidateDesired(
        Guid expenseRequestId,
        IReadOnlyCollection<BudgetRequestProjection> desired)
    {
        if (desired.Any(projection => projection.ExpenseRequestId != expenseRequestId))
        {
            throw new ArgumentException(
                "Every desired budget projection must belong to the replaced request.",
                nameof(desired));
        }

        if (desired.Select(projection => projection.ExpenseRequestLineId).Distinct().Count()
            != desired.Count)
        {
            throw new ArgumentException(
                "A request line can have only one current budget projection.",
                nameof(desired));
        }
    }

    private static Dictionary<BudgetKey, decimal> SumByKey(
        IEnumerable<BudgetRequestProjection> projections) => projections
        .GroupBy(projection => projection.Key)
        .ToDictionary(
            group => group.Key,
            group => group.Sum(projection => projection.ReservedAmountKzt));

    private IQueryable<BudgetPosition> FilterPositions(
        Guid? costCenterId,
        Guid? cashFlowItemId,
        DateOnly? budgetMonth)
    {
        var query = dbContext.BudgetPositions.AsQueryable();
        if (costCenterId.HasValue)
        {
            query = query.Where(position => position.CostCenterId == costCenterId.Value);
        }

        if (cashFlowItemId.HasValue)
        {
            query = query.Where(position => position.CashFlowItemId == cashFlowItemId.Value);
        }

        if (budgetMonth.HasValue)
        {
            var monthStart = new DateOnly(budgetMonth.Value.Year, budgetMonth.Value.Month, 1);
            query = query.Where(position => position.BudgetMonth == monthStart);
        }

        return query;
    }

    private static Expression<Func<BudgetPosition, bool>> BuildKeyPredicate(
        IReadOnlyCollection<BudgetKey> keys)
    {
        var position = Expression.Parameter(typeof(BudgetPosition), "position");
        Expression body = Expression.Constant(false);
        foreach (var key in keys)
        {
            var costCenterMatch = Expression.Equal(
                Expression.Property(position, nameof(BudgetPosition.CostCenterId)),
                Expression.Constant(key.CostCenterId));
            var cashFlowItemMatch = Expression.Equal(
                Expression.Property(position, nameof(BudgetPosition.CashFlowItemId)),
                Expression.Constant(key.CashFlowItemId));
            var monthMatch = Expression.Equal(
                Expression.Property(position, nameof(BudgetPosition.BudgetMonth)),
                Expression.Constant(key.BudgetMonth));
            body = Expression.OrElse(
                body,
                Expression.AndAlso(
                    costCenterMatch,
                    Expression.AndAlso(cashFlowItemMatch, monthMatch)));
        }

        return Expression.Lambda<Func<BudgetPosition, bool>>(body, position);
    }

    private async Task AcquireApplicationLockAsync(
        string resource,
        CancellationToken cancellationToken)
    {
        var transaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("An active database transaction is required.");
        var connection = dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @lock_result int;
            EXEC @lock_result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = @lock_timeout;
            SELECT @lock_result;
            """;
        AddParameter(command, "@resource", DbType.String, resource);
        AddParameter(
            command,
            "@lock_timeout",
            DbType.Int32,
            ApplicationLockTimeoutMilliseconds);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null || result is DBNull || Convert.ToInt32(result) < 0)
        {
            throw new TimeoutException(
                $"Could not acquire the SQL Server transaction lock '{resource}'.");
        }
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        DbType type,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static BudgetProjectionDriftException CreateDriftException(
        BudgetKey key,
        decimal? reservedAmountKzt,
        decimal projectionTotalKzt) => new(
        $"Budget aggregate drift detected for {FormatKey(key)}: " +
        $"position reserved = {reservedAmountKzt?.ToString() ?? "<missing>"}, " +
        $"projection total = {projectionTotalKzt}. No balancing record was written.");

    private static string FormatKey(BudgetKey key) =>
        $"costCenter={key.CostCenterId:N}, cashFlowItem={key.CashFlowItemId:N}, " +
        $"month={key.BudgetMonth:yyyy-MM-dd}";
}
