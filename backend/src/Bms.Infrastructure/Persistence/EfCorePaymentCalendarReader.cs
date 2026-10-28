using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Bms.Application.Treasury;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Reads the payment calendar from one captured scope. A single parameterised
/// batch materialises the authorised, filtered settlement area once, and the
/// count, the period totals and the page are all read out of it, so a writer
/// cannot make one answer describe two different moments. The visibility is
/// the same the expense-request list applies: a treasurer sees the whole
/// period while everyone else sees only what they already could.
/// </summary>
public sealed class EfCorePaymentCalendarReader(
    BmsDbContext dbContext,
    ExpenseRequestAccessMoment? accessMoment = null)
    : IPaymentCalendarReader, IPaymentCalendarExportReader
{
    private const string SettlementCurrencyCode = "KZT";

    public async Task<PaymentCalendarReadResult> ReadAsync(
        string actorExternalId,
        bool includeAll,
        PaymentCalendarQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();

        return await ReadCapturedScopeAsync(
            actorExternalId, includeAll,
            new CaptureQuery(query.From, query.Through, query.Skip, query.Take,
                query.ResolveStatuses(), query.Grouping, IsExport: false), cancellationToken);
    }

    /// <summary>
    /// Uses the same authorised SQL capture as the page, with no public page
    /// boundary. The count is checked before the row set is materialised; no
    /// sequence of separate reads can mix amounts from different captures.
    /// </summary>
    public async Task<PaymentCalendarReadResult> ReadForExportAsync(
        string actorExternalId,
        bool includeAll,
        PaymentCalendarExportQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        var statuses = new PaymentCalendarQuery(query.From, query.Through, 0, 1, query.StatusCode)
            .ResolveStatuses();
        return await ReadCapturedScopeAsync(
            actorExternalId, includeAll,
            new CaptureQuery(query.From, query.Through, 0,
                PaymentCalendarExportPolicy.MaximumRows + 1, statuses, query.Grouping, IsExport: true),
            cancellationToken);
    }

    private sealed record CaptureQuery(
        DateOnly From, DateOnly Through, int Skip, int Take,
        IReadOnlyCollection<ExpenseRequestStatus> Statuses, string Grouping, bool IsExport);

    private async Task<PaymentCalendarReadResult> ReadCapturedScopeAsync(
        string actorExternalId,
        bool includeAll,
        CaptureQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (dbContext.Database.CurrentTransaction is null)
        {
            // The role/assignment read must be retained until capture completes.
            return await new SqlServerBusinessTransactionRunner(dbContext).ExecuteAsync(
                token => ReadCapturedScopeAsync(actorExternalId, includeAll, query, token), cancellationToken);
        }
        var readScope = await BusinessDataReadScopeQuery.ResolveAsync(dbContext, actorExternalId, cancellationToken);
        var rounding400=await ApprovedAccountingEvidence400.ReadValidatedRoundingAsync(dbContext,cancellationToken);
        var statuses = query.Statuses.Select(status => status.ToCode()).ToArray();
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = BuildScopeBatch(statuses.Length);
            if (dbContext.Database.CurrentTransaction is { } ambient)
            {
                command.Transaction = ambient.GetDbTransaction();
            }

            AddParameter(command, "@from", DbType.Date, query.From.ToDateTime(TimeOnly.MinValue));
            AddParameter(command, "@through", DbType.Date, query.Through.ToDateTime(TimeOnly.MinValue));
            AddParameter(command, "@settlementCurrencyCode", DbType.String, SettlementCurrencyCode);
            AddParameter(command, "@actorExternalId", DbType.String, actorExternalId);
            AddParameter(command, "@includeAll", DbType.Boolean, readScope.CanReadAll);
            AddParameter(command, "@readScopeCfoIds", DbType.String, JsonSerializer.Serialize(readScope.CostCenterIds));
            AddParameter(command,"@approvedRounding400",DbType.String,JsonSerializer.Serialize(rounding400
                .Select(r=>new{requestId=r.ExpenseRequestId,amount=r.Amount})));
            AddParameter(command, "@skip", DbType.Int32, query.Skip);
            AddParameter(command, "@take", DbType.Int32, query.Take);
            AddParameter(command, "@groupByOrganization", DbType.Boolean,
                query.Grouping == PaymentCalendarGroupingCodes.Organization);
            for (var index = 0; index < statuses.Length; index++)
            {
                AddParameter(command, "@status" + index.ToString(CultureInfo.InvariantCulture), DbType.String, statuses[index]);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            // First result set: the whole captured scope, aggregated.
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new PaymentCalendarUnavailableException();
            }

            var total = reader.GetInt32(0);
            var obligationTotal = reader.GetDecimal(1);
            var confirmedTotal = reader.GetDecimal(2);
            var paidTotal = reader.GetDecimal(3);
            var invalidConfirmedCount = reader.GetInt32(4);
            var settlementCurrencyResolved = reader.GetInt32(5);
            if (settlementCurrencyResolved != 1 || invalidConfirmedCount != 0)
            {
                // Missing currency, negative cash or contradictory credit is
                // not a trustworthy settlement. Credit must not conceal a
                // negative bank balance or a foreign/malformed allocation.
                throw new PaymentCalendarUnavailableException();
            }

            // remaining = SUM(obligation - min(confirmed, obligation))
            // overpaid  = SUM(confirmed - min(confirmed, obligation))
            // Both are exact rewrites in decimal, with no rounding.
            var totals = new PaymentCalendarTotals(
                obligationTotal,
                paidTotal,
                obligationTotal - paidTotal,
                confirmedTotal - paidTotal)
            {
                AppliedOverpaymentAmount = reader.GetDecimal(6),
                ApprovedRoundingAmount = reader.GetDecimal(7),
            };

            if (query.IsExport && total > PaymentCalendarExportPolicy.MaximumRows)
            {
                throw new PaymentCalendarExportTooLargeException();
            }

            // Second result set: a page, or the whole bounded export capture.
            if (!await reader.NextResultAsync(cancellationToken))
            {
                throw new PaymentCalendarUnavailableException();
            }

            var rows = new List<PaymentCalendarRow>(Math.Min(total, query.Take));
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new PaymentCalendarRow(
                    reader.GetGuid(0),
                    DateOnly.FromDateTime(reader.GetDateTime(1)),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetDecimal(6),
                    reader.GetDecimal(7))
                {
                    OrganizationId = reader.IsDBNull(8) ? null : reader.GetGuid(8),
                    OrganizationCode = reader.IsDBNull(9) ? null : reader.GetString(9),
                    OrganizationName = reader.IsDBNull(10) ? null : reader.GetString(10),
                    AppliedOverpaymentAmount = reader.GetDecimal(11),
                    ApprovedRoundingAmount = reader.GetDecimal(12),
                });
            }

            // Third result set: full-scope aggregates for precisely the
            // organizations represented on the page. It is not a page sum or
            // a later read of live requests/reference data.
            if (!await reader.NextResultAsync(cancellationToken))
            {
                throw new PaymentCalendarUnavailableException();
            }
            var organizationGroups = new List<PaymentCalendarOrganizationGroup>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var groupObligation = reader.GetDecimal(4);
                var groupConfirmed = reader.GetDecimal(5);
                var groupPaid = reader.GetDecimal(6);
                organizationGroups.Add(new PaymentCalendarOrganizationGroup(
                    reader.IsDBNull(0) ? null : reader.GetGuid(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetInt32(3),
                    new PaymentCalendarTotals(groupObligation, groupPaid,
                        groupObligation - groupPaid, groupConfirmed - groupPaid)
                    {
                        AppliedOverpaymentAmount = reader.GetDecimal(7),
                        ApprovedRoundingAmount = reader.GetDecimal(8),
                    }));
            }

            // W501 added this floor because the count was its own statement and
            // a page could run past the total beside it. W517 removed that race
            // instead of covering it: both now read from one materialised scope,
            // so the floor can no longer fire. It stays as a residual guard, and
            // the acceptance suite proves the property it used to paper over.
            var reportedTotal = rows.Count != 0
                ? Math.Max(total, query.Skip + rows.Count)
                : total;

            // Only a page uses the historical floor. The export must expose
            // the actual captured count so its completeness check can refuse.
            return new PaymentCalendarReadResult(rows, query.IsExport ? total : reportedTotal, totals)
            {
                OrganizationGroups = organizationGroups,
            };
        }
        catch (DbException exception)
        {
            throw new PaymentCalendarUnavailableException(exception);
        }
        finally
        {
            if (openedHere)
            {
                await dbContext.Database.CloseConnectionAsync();
            }
        }
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// One parameterised batch. It materialises the authorised, filtered
    /// settlement scope exactly once, together with the two amounts each row
    /// needs, and then reads the count, the period totals and the page out of
    /// that one table variable.
    ///
    /// W517 measured why this shape is necessary. With count, totals and page as
    /// separate statements, a writer moving rows in and out of the scope
    /// produced answers that contradicted themselves: a page of ten rows summing
    /// 5 700 beside totals of 11 470, in the same response. Read committed gives
    /// no snapshot across statements, and none inside one either, so the scope is
    /// captured instead of re-derived.
    ///
    /// The visibility predicate mirrors the deliberately non-delegated payment
    /// calendar scope branch for branch. W619 permits a substitute to work on an
    /// existing request, but does not turn that delegation into treasury access.
    /// The duplication is held in place by an executable equivalence test.
    /// </summary>
    private static string BuildScopeBatch(int statusCount)
    {
        var statusParameters = string.Join(
            ", ",
            Enumerable.Range(0, statusCount)
                .Select(index => "@status" + index.ToString(CultureInfo.InvariantCulture)));

        return $"""
            SET NOCOUNT ON;

            DECLARE @settlementCurrencyId uniqueidentifier =
                (SELECT TOP (1) c.Id FROM bms_web.Currencies AS c
                  WHERE c.Code = @settlementCurrencyCode);

            DECLARE @scope TABLE
            (
                PlannedPaymentDate date NOT NULL,
                ExpenseRequestId uniqueidentifier NOT NULL,
                Status nvarchar(64) NOT NULL,
                CurrencyCode nvarchar(16) NOT NULL,
                CounterpartyName nvarchar(150) NULL,
                Purpose nvarchar(1024) NULL,
                ObligationAmount decimal(18, 2) NOT NULL,
                ConfirmedAmount decimal(18, 2) NOT NULL,
                CashConfirmedAmount decimal(18, 2) NOT NULL,
                AppliedOverpaymentAmount decimal(18, 2) NOT NULL,
                ApprovedRoundingAmount decimal(18, 2) NOT NULL,
                InvalidAllocationCount int NOT NULL,
                OrganizationId uniqueidentifier NULL,
                OrganizationCode nvarchar(100) NULL,
                OrganizationName nvarchar(500) NULL,
                OrganizationMissing bit NOT NULL,
                OrganizationKey uniqueidentifier NOT NULL,
                -- Ordered by the page's own sort, so paging reads the capture
                -- in order instead of sorting it again.
                PRIMARY KEY (PlannedPaymentDate, ExpenseRequestId)
            );

            IF @settlementCurrencyId IS NOT NULL
            BEGIN
                INSERT INTO @scope
                    (PlannedPaymentDate, ExpenseRequestId, Status, CurrencyCode,
                     CounterpartyName, Purpose, ObligationAmount, ConfirmedAmount,
                     CashConfirmedAmount, AppliedOverpaymentAmount, ApprovedRoundingAmount, InvalidAllocationCount,
                     OrganizationId, OrganizationCode, OrganizationName,
                     OrganizationMissing, OrganizationKey)
                SELECT
                    r.PlannedPaymentDate,
                    r.Id,
                    r.Status,
                    r.CurrencyCode,
                    (SELECT TOP (1) cp.Name FROM bms_web.Counterparties AS cp
                      WHERE cp.Id = r.CounterpartyId),
                    r.Purpose,
                    ISNULL((SELECT SUM(l.Amount) FROM bms_web.ExpenseRequestLines AS l
                             WHERE l.ExpenseRequestId = r.Id), 0),
                    ISNULL(cash.Amount, 0) + ISNULL(credit.AppliedAmount, 0) + ISNULL(rounding.Amount,0),
                    ISNULL(cash.Amount, 0),
                    ISNULL(credit.AppliedAmount, 0),
                    ISNULL(rounding.Amount,0),
                    ISNULL(credit.InvalidCount, 0),
                    r.OrganizationId,
                    organization.Code,
                    organization.Name,
                    CASE WHEN r.OrganizationId IS NULL THEN 1 ELSE 0 END,
                    ISNULL(r.OrganizationId, CAST('00000000-0000-0000-0000-000000000000' AS uniqueidentifier))
                FROM bms_web.ExpenseRequests AS r
                LEFT JOIN bms_web.Organizations AS organization ON organization.Id = r.OrganizationId
                OUTER APPLY
                (
                    SELECT SUM(CASE WHEN f.IsReversal = 1 THEN -f.Amount ELSE f.Amount END) AS Amount
                    FROM bms_web.ExpenseRequestPaymentFacts AS f
                    WHERE f.ExpenseRequestId = r.Id
                      AND f.CurrencyId = @settlementCurrencyId
                ) AS cash
                OUTER APPLY
                (
                    -- The card/list projector validates every allocation but
                    -- settles only Applied credit. A missing/foreign source
                    -- must remain visible as an error, not disappear in a join.
                    SELECT
                        SUM(CASE WHEN a.Status = N'Applied' THEN a.Amount ELSE 0 END) AS AppliedAmount,
                        SUM(validation.IsInvalid) AS InvalidCount
                    FROM bms_web.OverpaymentAllocations AS a
                    LEFT JOIN bms_web.OverpaymentSources AS source ON source.SourceId = a.SourceId
                    CROSS APPLY
                    (
                        -- Evaluate each row before aggregating. Mixing source
                        -- columns and the outer request in SUM raises SQL8124.
                        SELECT CASE WHEN source.SourceId IS NULL
                                  OR r.OrganizationId IS NULL OR r.CounterpartyId IS NULL
                                  OR source.OrganizationId = CAST('00000000-0000-0000-0000-000000000000' AS uniqueidentifier)
                                  OR source.CounterpartyId = CAST('00000000-0000-0000-0000-000000000000' AS uniqueidentifier)
                                  OR source.OrganizationId <> r.OrganizationId
                                  OR source.CounterpartyId <> r.CounterpartyId
                                  OR source.CurrencyId <> @settlementCurrencyId
                                  OR a.AllocationId = CAST('00000000-0000-0000-0000-000000000000' AS uniqueidentifier)
                                  OR a.Status NOT IN (N'Reserved', N'Applied', N'Released')
                                  OR DATALENGTH(a.Status) <> CASE WHEN a.Status = N'Applied' THEN 14 ELSE 16 END
                                  OR a.Amount <= 0 OR a.AmountKzt <= 0 OR a.Amount <> a.AmountKzt
                                  OR (a.Status = N'Reserved' AND a.AppliedOrReleasedAt IS NOT NULL)
                                  OR (a.Status <> N'Reserved' AND a.AppliedOrReleasedAt IS NULL)
                                  OR DATEPART(TZOFFSET, a.ReservedAt) <> 0
                                  OR a.ReservedAt = CAST('0001-01-01T00:00:00+00:00' AS datetimeoffset(3))
                                  OR DATEPART(TZOFFSET, a.AppliedOrReleasedAt) <> 0
                                  OR a.AppliedOrReleasedAt < a.ReservedAt
                                    THEN 1 ELSE 0 END AS IsInvalid
                    ) AS validation
                    WHERE a.TargetExpenseRequestId = r.Id
                ) AS credit
                OUTER APPLY
                (
                    SELECT SUM(j.amount) Amount FROM OPENJSON(@approvedRounding400)
                    WITH(requestId uniqueidentifier '$.requestId',amount decimal(18,2) '$.amount') j
                    WHERE j.requestId=r.Id
                ) AS rounding
                WHERE r.PlannedPaymentDate IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM bms_web.BusinessAudit AS deleted
                    WHERE deleted.ObjectType = N'ExpenseRequest'
                      AND deleted.ObjectId = CONVERT(nvarchar(36), r.Id)
                      AND deleted.Action = N'ExpenseRequestDraftDeleted')
                  AND r.PlannedPaymentDate >= @from
                  AND r.PlannedPaymentDate <= @through
                  AND r.CurrencyCode = @settlementCurrencyCode
                  AND r.Status IN ({statusParameters})
                  AND (
                        @includeAll = 1
                        OR (r.DefaultCostCenterId IN
                                (SELECT [value] FROM OPENJSON(@readScopeCfoIds) WITH ([value] uniqueidentifier '$'))
                            AND NOT EXISTS (
                                SELECT 1 FROM bms_web.ExpenseRequestLines AS scopeLine
                                WHERE scopeLine.ExpenseRequestId = r.Id
                                  AND scopeLine.CostCenterId IS NOT NULL
                                  AND scopeLine.CostCenterId NOT IN
                                    (SELECT [value] FROM OPENJSON(@readScopeCfoIds) WITH ([value] uniqueidentifier '$'))))
                      );
            END;

            SELECT
                COUNT(*),
                ISNULL(SUM(s.ObligationAmount), CAST(0 AS decimal(18, 2))),
                ISNULL(SUM(s.ConfirmedAmount), CAST(0 AS decimal(18, 2))),
                ISNULL(SUM(CASE WHEN s.ConfirmedAmount < s.ObligationAmount
                                THEN s.ConfirmedAmount ELSE s.ObligationAmount END),
                       CAST(0 AS decimal(18, 2))),
                ISNULL(SUM(CASE WHEN s.CashConfirmedAmount < 0
                                OR s.AppliedOverpaymentAmount > s.ObligationAmount
                                OR s.InvalidAllocationCount <> 0 THEN 1 ELSE 0 END), 0),
                CASE WHEN @settlementCurrencyId IS NULL THEN 0 ELSE 1 END,
                ISNULL(SUM(s.AppliedOverpaymentAmount), CAST(0 AS decimal(18, 2))),
                ISNULL(SUM(s.ApprovedRoundingAmount), CAST(0 AS decimal(18, 2)))
            FROM @scope AS s;

            DECLARE @page TABLE
            (
                ExpenseRequestId uniqueidentifier NOT NULL PRIMARY KEY,
                OrganizationKey uniqueidentifier NOT NULL
            );

            IF @groupByOrganization = 1
            BEGIN
                INSERT INTO @page (ExpenseRequestId, OrganizationKey)
                SELECT s.ExpenseRequestId, s.OrganizationKey
                FROM @scope AS s
                ORDER BY s.OrganizationMissing, s.OrganizationCode, s.OrganizationId,
                         s.PlannedPaymentDate, s.ExpenseRequestId
                OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            END
            ELSE
            BEGIN
                INSERT INTO @page (ExpenseRequestId, OrganizationKey)
                SELECT s.ExpenseRequestId, s.OrganizationKey
                FROM @scope AS s
                ORDER BY s.PlannedPaymentDate, s.ExpenseRequestId
                OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            END;

            SELECT
                s.ExpenseRequestId,
                s.PlannedPaymentDate,
                s.Status,
                s.CurrencyCode,
                s.CounterpartyName,
                s.Purpose,
                s.ObligationAmount,
                s.ConfirmedAmount,
                s.OrganizationId,
                s.OrganizationCode,
                s.OrganizationName,
                s.AppliedOverpaymentAmount,
                s.ApprovedRoundingAmount
            FROM @scope AS s
            JOIN @page AS pageRows ON pageRows.ExpenseRequestId = s.ExpenseRequestId
            ORDER BY CASE WHEN @groupByOrganization = 1 THEN s.OrganizationMissing END,
                     CASE WHEN @groupByOrganization = 1 THEN s.OrganizationCode END,
                     CASE WHEN @groupByOrganization = 1 THEN s.OrganizationId END,
                     s.PlannedPaymentDate, s.ExpenseRequestId;

            SELECT
                s.OrganizationId,
                s.OrganizationCode,
                s.OrganizationName,
                COUNT(*),
                SUM(s.ObligationAmount),
                SUM(s.ConfirmedAmount),
                SUM(CASE WHEN s.ConfirmedAmount < s.ObligationAmount
                         THEN s.ConfirmedAmount ELSE s.ObligationAmount END),
                SUM(s.AppliedOverpaymentAmount),
                SUM(s.ApprovedRoundingAmount)
            FROM @scope AS s
            JOIN (SELECT DISTINCT OrganizationKey FROM @page) AS pageGroups
                ON pageGroups.OrganizationKey = s.OrganizationKey
            WHERE @groupByOrganization = 1
            GROUP BY s.OrganizationMissing, s.OrganizationId, s.OrganizationCode, s.OrganizationName
            ORDER BY s.OrganizationMissing, s.OrganizationCode, s.OrganizationId;
            """;
    }

    public async Task<IReadOnlyList<Guid>> ListOpenableIdsAsync(
        string actorExternalId,
        bool includeAll,
        IReadOnlyCollection<Guid> expenseRequestIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expenseRequestIds);
        if (expenseRequestIds.Count == 0)
        {
            return [];
        }

        var requestedIds = expenseRequestIds.Distinct().ToArray();
        var query = dbContext.ExpenseRequests
            .AsNoTracking()
            .Where(request => requestedIds.Contains(request.Id));
        query = ExpenseRequestVisibilityQuery.ApplyPaymentCalendarDetail(
            dbContext,
            query,
            actorExternalId,
            includeAll,
            accessMoment?.UtcNow ?? DateTimeOffset.UtcNow);
        return await query
            .OrderBy(request => request.Id)
            .Select(request => request.Id)
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>
    /// The filtered scope, expressed on the entity so every predicate stays
    /// translatable (W443): period, settlement statuses, the stored KZT circuit
    /// and the actor's list visibility.
    /// </summary>
    private IQueryable<ExpenseRequest> BuildScope(
        string actorExternalId,
        bool includeAll,
        PaymentCalendarQuery query,
        IReadOnlyCollection<ExpenseRequestStatus> statuses)
    {
        var source = dbContext.ExpenseRequests
            .AsNoTracking()
            .Where(request => request.PlannedPaymentDate != null
                && request.PlannedPaymentDate >= query.From
                && request.PlannedPaymentDate <= query.Through
                && request.CurrencyCode == SettlementCurrencyCode
                && statuses.Contains(request.Status));
        return ExpenseRequestVisibilityQuery.ApplyPaymentCalendarScope(
            dbContext,
            source,
            actorExternalId,
            includeAll,
            accessMoment?.UtcNow ?? DateTimeOffset.UtcNow);
    }
}
