using System.Data;
using System.Data.Common;
using Bms.Application.Abstractions;
using Bms.Application.Budgeting;
using Bms.Application.Common;
using Bms.Application.Authorization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Reads one budget position and the composition of its current reserve.
///
/// The position, the count, the sums and the page all come from a single
/// parameterised batch that captures the area once. Reading them separately is
/// exactly how a page comes to describe a different set of requests than the
/// totals printed beside it.
///
/// The reserve of a position is by construction the sum of the projections of
/// its key: the writer reads both under UPDLOCK/HOLDLOCK and refuses to write
/// when they disagree. This reader demands the same equality of the capture it
/// just took, and fails closed rather than showing a composition that does not
/// add up to the number the screen already showed.
/// </summary>
public sealed class EfCoreBudgetReservationReader(BmsDbContext dbContext,
    IBudgetCashConsumptionReader? cashConsumption=null) : IBudgetReservationReader
{
    public Task<BudgetReservationSlice?> ReadAsync(Guid costCenterId, Guid cashFlowItemId,
        DateOnly budgetMonth, int skip, int take, CancellationToken cancellationToken) =>
        ReadAsync(costCenterId, cashFlowItemId, budgetMonth, skip, take, BusinessDataReadScope.All, cancellationToken);

    public async Task<BudgetReservationSlice?> ReadAsync(
        Guid costCenterId,
        Guid cashFlowItemId,
        DateOnly budgetMonth,
        int skip,
        int take,
        BusinessDataReadScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        if (!scope.Contains(costCenterId)) throw new ActorForbiddenException();
        if (dbContext.BudgetRegisterEnabled343)
            return await new BudgetExecutionReportCapture386(dbContext).ReadReservationAsync(
                Bms.Domain.Budgeting.BudgetKey.Create(costCenterId, cashFlowItemId, budgetMonth), skip, take, scope, cancellationToken);
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
            command.CommandText = ReservationBatch;
            AddNullableParameter(command, "@readScopeCfoIds", DbType.String,
                scope.CanReadAll ? null : JsonSerializer.Serialize(scope.CostCenterIds));
            if (dbContext.Database.CurrentTransaction is { } ambient)
            {
                command.Transaction = ambient.GetDbTransaction();
            }

            AddParameter(command, "@costCenterId", DbType.Guid, costCenterId);
            AddParameter(command, "@cashFlowItemId", DbType.Guid, cashFlowItemId);
            AddParameter(
                command,
                "@budgetMonth",
                DbType.Date,
                new DateOnly(budgetMonth.Year, budgetMonth.Month, 1).ToDateTime(TimeOnly.MinValue));
            AddParameter(command, "@skip", DbType.Int32, skip);
            AddParameter(command, "@take", DbType.Int32, take);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            // First result set: the position itself, or nothing at all.
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var limit = reader.IsDBNull(0) ? (decimal?)null : reader.GetDecimal(0);
            var reserved = reader.GetDecimal(1);
            var paid = reader.GetDecimal(2);
            if (await reader.ReadAsync(cancellationToken))
            {
                throw new BudgetProjectionDriftException(
                    "More than one budget position answered one exact key.");
            }

            if (!await reader.NextResultAsync(cancellationToken)
                || !await reader.ReadAsync(cancellationToken))
            {
                throw new BudgetProjectionDriftException(
                    "The reservation capture returned no aggregate row.");
            }

            var total = reader.GetInt32(0);
            var totalLineCount = reader.GetInt32(1);
            var totalReserved = reader.GetDecimal(2);
            var completeTotal = reader.GetInt32(3);
            var completeLineCount = reader.GetInt32(4);
            var completeReserved = reader.GetDecimal(5);
            if (await reader.ReadAsync(cancellationToken))
                throw new BudgetProjectionDriftException("The reservation capture returned multiple aggregate rows.");

            if (!await reader.NextResultAsync(cancellationToken))
            {
                throw new BudgetProjectionDriftException(
                    "The reservation capture returned no participants.");
            }

            var page = new List<BudgetReservationParticipant>(take);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(3))
                {
                    // A projection exists only for a request that has a planned
                    // payment date - the budget month is derived from it. A row
                    // without one is a projection this reader cannot trust.
                    throw new BudgetProjectionDriftException(
                        "A reserving request has no planned payment date.");
                }

                page.Add(new BudgetReservationParticipant(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    DateOnly.FromDateTime(reader.GetDateTime(3)),
                    reader.GetInt32(4),
                    reader.GetDecimal(5)));
            }

            Reconcile(reserved, total, totalLineCount, totalReserved, page.Count, skip, take,
                completeTotal, completeLineCount, completeReserved);
            if (page.Any(p => p.LineCount < 1 || p.ReservedAmountKzt <= 0m)
                || page.Sum(p => p.LineCount) > totalLineCount
                || page.Sum(p => p.ReservedAmountKzt) > totalReserved
                || (page.Count == total && (page.Sum(p => p.LineCount) != totalLineCount
                    || page.Sum(p => p.ReservedAmountKzt) != totalReserved)))
                throw new BudgetProjectionDriftException("The visible reservation page does not match its subtotal.");

            // Release the SQL reader before loading the pinned DDS capture.
            // The Serializable business transaction still owns this capture.
            await reader.DisposeAsync();
            if (cashConsumption is not null)
            {
                var key = Bms.Domain.Budgeting.BudgetKey.Create(costCenterId, cashFlowItemId, budgetMonth);
                var usage = await cashConsumption.ReadAsync([key], cancellationToken);
                paid = usage[key].EffectivePaid(paid);
            }
            return new BudgetReservationSlice(
                limit,
                reserved,
                paid,
                page,
                total,
                totalLineCount,
                completeReserved)
            {
                VisibleReservedAmountKzt = totalReserved
            };
        }
        finally
        {
            if (openedHere)
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    public Task<IReadOnlyList<BudgetReservationExportRow>> ReadForExportAsync(
        Guid? costCenterId,
        Guid? cashFlowItemId,
        DateOnly? budgetMonth,
        CancellationToken cancellationToken) =>
        ReadForExportAsync(costCenterId, cashFlowItemId, budgetMonth, BusinessDataReadScope.All, cancellationToken);

    public async Task<IReadOnlyList<BudgetReservationExportRow>> ReadForExportAsync(
        Guid? costCenterId,
        Guid? cashFlowItemId,
        DateOnly? budgetMonth,
        BusinessDataReadScope scope, CancellationToken cancellationToken) =>
        (await ReadCaptureForExportAsync(costCenterId, cashFlowItemId, budgetMonth, scope, cancellationToken)).Rows;

    public async Task<BudgetReservationExportCapture> ReadCaptureForExportAsync(
        Guid? costCenterId, Guid? cashFlowItemId, DateOnly? budgetMonth,
        BusinessDataReadScope scope, CancellationToken cancellationToken)
    {
        if (!scope.HasAny || (costCenterId.HasValue && !scope.Contains(costCenterId.Value)))
            throw new ActorForbiddenException();
        if (dbContext.BudgetRegisterEnabled343)
            return await new BudgetExecutionReportCapture386(dbContext).ReadReservationsForExportAsync(
                costCenterId, cashFlowItemId, budgetMonth, scope, cancellationToken);
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = ReservationExportBatch;
            if (dbContext.Database.CurrentTransaction is { } ambient)
            {
                command.Transaction = ambient.GetDbTransaction();
            }

            AddNullableParameter(command, "@readScopeCfoIds", DbType.String,
                scope.CanReadAll ? null : JsonSerializer.Serialize(scope.CostCenterIds));
            AddNullableParameter(command, "@costCenterId", DbType.Guid, costCenterId);
            AddNullableParameter(command, "@cashFlowItemId", DbType.Guid, cashFlowItemId);
            // One extra row proves that the whole file must be refused. Never
            // return a truncated workbook or materialize an unbounded area.
            AddParameter(
                command,
                "@maximumExportRows",
                DbType.Int32,
                BudgetExecutionExportPolicy.MaximumRows + 1);
            AddNullableParameter(
                command,
                "@budgetMonth",
                DbType.Date,
                budgetMonth.HasValue
                    ? new DateOnly(budgetMonth.Value.Year, budgetMonth.Value.Month, 1)
                        .ToDateTime(TimeOnly.MinValue)
                    : null);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = await ReadExportRowsAsync(
                reader, BudgetExecutionExportPolicy.MaximumRows, cancellationToken);
            if (!await reader.NextResultAsync(cancellationToken))
                throw new BudgetProjectionDriftException("The export returned no complete monetary control.");
            var totals = new List<BudgetReservationExportTotal>();
            while (await reader.ReadAsync(cancellationToken))
            {
                if (totals.Count >= BudgetExecutionExportPolicy.MaximumRows)
                    throw new BudgetExecutionExportTooLargeException(totals.Count + 1, BudgetExecutionExportPolicy.MaximumRows);
                totals.Add(new BudgetReservationExportTotal(
                    Bms.Domain.Budgeting.BudgetKey.Create(reader.GetGuid(0), reader.GetGuid(1),
                        DateOnly.FromDateTime(reader.GetDateTime(2))),
                    reader.GetInt32(3), reader.GetInt32(4), reader.GetDecimal(5)));
            }
            return new BudgetReservationExportCapture(rows, totals);
        }
        finally
        {
            if (openedHere)
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    internal static async Task<IReadOnlyList<BudgetReservationExportRow>> ReadExportRowsAsync(
        DbDataReader reader,
        int maximumRows,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        var rows = new List<BudgetReservationExportRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count >= maximumRows)
            {
                throw new BudgetExecutionExportTooLargeException(rows.Count + 1, maximumRows);
            }

            if (reader.IsDBNull(8))
            {
                throw new BudgetProjectionDriftException(
                    "A reserving request has no planned payment date.");
            }

            rows.Add(new BudgetReservationExportRow(
                DateOnly.FromDateTime(reader.GetDateTime(0)),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(7),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                DateOnly.FromDateTime(reader.GetDateTime(8)),
                reader.GetInt32(9),
                reader.GetDecimal(10))
            {
                CostCenterId = reader.GetGuid(5),
                CashFlowItemId = reader.GetGuid(11)
            });
        }

        return rows;
    }

    /// <summary>
    /// Every reserving request of every position in one filtered area.
    ///
    /// The area is captured first with the very filters the export sheet uses,
    /// and the participants are grouped by key and request before either the
    /// request table or the catalogues are joined. Every join is on a primary
    /// key, so none of them can multiply a captured row.
    ///
    /// The order continues the order of the positions sheet - newest month
    /// first, then cost centre, then cash flow item - and inside one position
    /// it is the planned payment date, the purpose and finally the request
    /// identifier as a technical tie-break.
    /// </summary>
    private const string ReservationExportBatch = """
        SET NOCOUNT ON;
        DECLARE @area TABLE
        (
            BudgetMonth date NOT NULL,
            CostCenterId uniqueidentifier NOT NULL,
            CashFlowItemId uniqueidentifier NOT NULL,
            PRIMARY KEY (BudgetMonth, CostCenterId, CashFlowItemId)
        );

        INSERT INTO @area (BudgetMonth, CostCenterId, CashFlowItemId)
        SELECT p.BudgetMonth, p.CostCenterId, p.CashFlowItemId
        FROM bms_web.BudgetPositions AS p
        WHERE (@readScopeCfoIds IS NULL OR p.CostCenterId IN
                (SELECT [value] FROM OPENJSON(@readScopeCfoIds) WITH ([value] uniqueidentifier '$')))
          AND (@costCenterId IS NULL OR p.CostCenterId = @costCenterId)
          AND (@cashFlowItemId IS NULL OR p.CashFlowItemId = @cashFlowItemId)
          AND (@budgetMonth IS NULL OR p.BudgetMonth = @budgetMonth);

        DECLARE @capture TABLE
        (
            BudgetMonth date NOT NULL,
            CostCenterId uniqueidentifier NOT NULL,
            CashFlowItemId uniqueidentifier NOT NULL,
            ExpenseRequestId uniqueidentifier NOT NULL,
            Status nvarchar(40) NOT NULL,
            Purpose nvarchar(1000) NULL,
            PlannedPaymentDate date NULL,
            LineCount int NOT NULL,
            ReservedAmountKzt decimal(19, 4) NOT NULL,
            IsVisible bit NOT NULL,
            PRIMARY KEY (BudgetMonth, CostCenterId, CashFlowItemId, ExpenseRequestId)
        );

        INSERT INTO @capture
            (BudgetMonth, CostCenterId, CashFlowItemId, ExpenseRequestId, Status, Purpose,
             PlannedPaymentDate, LineCount, ReservedAmountKzt, IsVisible)
        SELECT a.BudgetMonth, a.CostCenterId, a.CashFlowItemId, g.ExpenseRequestId,
            r.Status, r.Purpose, r.PlannedPaymentDate, g.LineCount, g.ReservedAmountKzt,
            CASE WHEN (@readScopeCfoIds IS NULL OR
                (r.DefaultCostCenterId IN (SELECT [value] FROM OPENJSON(@readScopeCfoIds) WITH ([value] uniqueidentifier '$'))
                 AND NOT EXISTS (SELECT 1 FROM bms_web.ExpenseRequestLines AS scopeLine
                     WHERE scopeLine.ExpenseRequestId = r.Id AND scopeLine.CostCenterId IS NOT NULL
                     AND scopeLine.CostCenterId NOT IN
                         (SELECT [value] FROM OPENJSON(@readScopeCfoIds) WITH ([value] uniqueidentifier '$')))))
                THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
        FROM @area AS a
        INNER JOIN
        (
            SELECT x.CostCenterId, x.CashFlowItemId, x.BudgetMonth, x.ExpenseRequestId,
                COUNT(*) AS LineCount, SUM(x.ReservedAmountKzt) AS ReservedAmountKzt
            FROM bms_web.BudgetRequestProjections AS x
            WHERE (@readScopeCfoIds IS NULL OR x.CostCenterId IN
                (SELECT [value] FROM OPENJSON(@readScopeCfoIds) WITH ([value] uniqueidentifier '$')))
              AND (@costCenterId IS NULL OR x.CostCenterId = @costCenterId)
              AND (@cashFlowItemId IS NULL OR x.CashFlowItemId = @cashFlowItemId)
              AND (@budgetMonth IS NULL OR x.BudgetMonth = @budgetMonth)
            GROUP BY x.CostCenterId, x.CashFlowItemId, x.BudgetMonth, x.ExpenseRequestId
        ) AS g ON g.CostCenterId = a.CostCenterId AND g.CashFlowItemId = a.CashFlowItemId
            AND g.BudgetMonth = a.BudgetMonth
        INNER JOIN bms_web.ExpenseRequests AS r ON r.Id = g.ExpenseRequestId;

        SELECT TOP (@maximumExportRows)
            a.BudgetMonth, cc.Code, cc.Name, cfi.Code, cfi.Name, a.CostCenterId,
            a.Purpose, a.Status, a.PlannedPaymentDate, a.LineCount, a.ReservedAmountKzt, a.CashFlowItemId
        FROM @capture AS a
        LEFT JOIN bms_web.CostCenters AS cc ON cc.Id = a.CostCenterId
        LEFT JOIN bms_web.CashFlowItems AS cfi ON cfi.Id = a.CashFlowItemId
        WHERE a.IsVisible = 1
        ORDER BY a.BudgetMonth DESC, a.CostCenterId, a.CashFlowItemId,
            a.PlannedPaymentDate, a.Purpose, a.ExpenseRequestId;

        SELECT TOP (@maximumExportRows) a.CostCenterId, a.CashFlowItemId, a.BudgetMonth,
            COUNT(c.ExpenseRequestId), ISNULL(SUM(c.LineCount), 0),
            ISNULL(SUM(c.ReservedAmountKzt), CAST(0 AS decimal(19, 4)))
        FROM @area AS a
        LEFT JOIN @capture AS c ON c.CostCenterId = a.CostCenterId
            AND c.CashFlowItemId = a.CashFlowItemId AND c.BudgetMonth = a.BudgetMonth
        GROUP BY a.CostCenterId, a.CashFlowItemId, a.BudgetMonth
        ORDER BY a.BudgetMonth DESC, a.CostCenterId, a.CashFlowItemId;
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

    /// <summary>
    /// Everything the capture promises about itself. Any failure here means the
    /// composition on screen would not add up to the reserve on screen, and the
    /// only honest answer is no answer.
    /// </summary>
    internal static void Reconcile(
        decimal positionReserved,
        int total,
        int totalLineCount,
        decimal totalReserved,
        int pageCount,
        int skip,
        int take,
        int? completeTotal = null,
        int? completeLineCount = null,
        decimal? completeReserved = null)
    {
        var fullTotal = completeTotal ?? total;
        var fullLines = completeLineCount ?? totalLineCount;
        var fullReserved = completeReserved ?? totalReserved;
        if (fullReserved != positionReserved)
        {
            throw new BudgetProjectionDriftException(
                "The reserving requests do not add up to the reserved amount of the position.");
        }

        if (total < 0 || totalLineCount < total || fullTotal < total || fullLines < fullTotal
            || fullLines < totalLineCount || totalReserved < 0m || totalReserved > fullReserved)
        {
            throw new BudgetProjectionDriftException(
                "The reservation capture counted fewer request lines than requests.");
        }

        if (fullTotal == 0 && positionReserved != 0m)
        {
            throw new BudgetProjectionDriftException(
                "The position reserves an amount that no request accounts for.");
        }

        if ((total == 0 && (totalLineCount != 0 || totalReserved != 0m))
            || (total > 0 && totalReserved <= 0m) || (fullTotal > 0 && fullReserved <= 0m))
            throw new BudgetProjectionDriftException("The reservation monetary totals do not match their captured counts.");

        // An empty window beyond the end is valid (including after concurrent
        // shrinkage). Inside the captured area the page must be exact, not just
        // no larger than the requested window. Subtraction avoids Int32 wrap.
        var expectedPageCount = skip >= total ? 0 : Math.Min(take, total - skip);
        if (pageCount != expectedPageCount)
        {
            throw new BudgetProjectionDriftException(
                "The reservation page does not fit the area it was counted from.");
        }
    }

    /// <summary>
    /// One capture, then three reads of it.
    ///
    /// The participants are grouped by request over the budget-key index before
    /// anything is joined, so the join to the request table is one lookup per
    /// request on its primary key - it cannot multiply a row and it is not a
    /// read per line.
    ///
    /// The order is the planned payment date, then the purpose, then the request
    /// identifier as a technical tie-break. This system has no request number to
    /// sort by; the identifier never leaves the reader, it only makes the order
    /// total.
    /// </summary>
    internal const string ReservationBatch = """
        SET NOCOUNT ON;

        DECLARE @position TABLE
        (
            LimitAmountKzt decimal(19, 4) NULL,
            ReservedAmountKzt decimal(19, 4) NOT NULL,
            PaidAmountKzt decimal(19, 4) NOT NULL
        );

        INSERT INTO @position (LimitAmountKzt, ReservedAmountKzt, PaidAmountKzt)
        SELECT
            p.LimitAmountKzt,
            p.ReservedAmountKzt,
            p.PaidAmountKzt
        FROM bms_web.v_CompanyBudgetExecutionPositions AS p
        WHERE p.CostCenterId = @costCenterId
          AND p.CashFlowItemId = @cashFlowItemId
          AND p.BudgetMonth = @budgetMonth;

        DECLARE @scope TABLE
        (
            ExpenseRequestId uniqueidentifier NOT NULL PRIMARY KEY,
            Status nvarchar(40) NOT NULL,
            Purpose nvarchar(1000) NULL,
            PlannedPaymentDate date NULL,
            LineCount int NOT NULL,
            ReservedAmountKzt decimal(19, 4) NOT NULL,
            IsVisible bit NOT NULL
        );

        INSERT INTO @scope
            (ExpenseRequestId, Status, Purpose, PlannedPaymentDate, LineCount, ReservedAmountKzt, IsVisible)
        SELECT
            r.Id,
            r.Status,
            r.Purpose,
            r.PlannedPaymentDate,
            g.LineCount,
            g.ReservedAmountKzt,
            CASE WHEN (@readScopeCfoIds IS NULL OR
                (r.DefaultCostCenterId IN (SELECT [value] FROM OPENJSON(@readScopeCfoIds) WITH ([value] uniqueidentifier '$'))
                 AND NOT EXISTS (SELECT 1 FROM bms_web.ExpenseRequestLines AS scopeLine
                     WHERE scopeLine.ExpenseRequestId = r.Id AND scopeLine.CostCenterId IS NOT NULL
                     AND scopeLine.CostCenterId NOT IN
                         (SELECT [value] FROM OPENJSON(@readScopeCfoIds) WITH ([value] uniqueidentifier '$')))))
                THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
        FROM
        (
            SELECT
                x.ExpenseRequestId,
                COUNT(*) AS LineCount,
                SUM(x.ReservedAmountKzt) AS ReservedAmountKzt
            FROM bms_web.BudgetRequestProjections AS x
            WHERE x.CostCenterId = @costCenterId
              AND x.CashFlowItemId = @cashFlowItemId
              AND x.BudgetMonth = @budgetMonth
            GROUP BY x.ExpenseRequestId
        ) AS g
        INNER JOIN bms_web.ExpenseRequests AS r ON r.Id = g.ExpenseRequestId;

        SELECT p.LimitAmountKzt, p.ReservedAmountKzt, p.PaidAmountKzt
        FROM @position AS p;

        SELECT
            COUNT(CASE WHEN a.IsVisible = 1 THEN 1 END),
            ISNULL(SUM(CASE WHEN a.IsVisible = 1 THEN a.LineCount ELSE 0 END), 0),
            ISNULL(SUM(CASE WHEN a.IsVisible = 1 THEN a.ReservedAmountKzt ELSE 0 END), CAST(0 AS decimal(19, 4))),
            COUNT(*),
            ISNULL(SUM(a.LineCount), 0),
            ISNULL(SUM(a.ReservedAmountKzt), CAST(0 AS decimal(19, 4)))
        FROM @scope AS a;

        SELECT
            a.ExpenseRequestId,
            a.Status,
            a.Purpose,
            a.PlannedPaymentDate,
            a.LineCount,
            a.ReservedAmountKzt
        FROM @scope AS a
        WHERE a.IsVisible = 1
        ORDER BY a.PlannedPaymentDate, a.Purpose, a.ExpenseRequestId
        OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
        """;

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
}
