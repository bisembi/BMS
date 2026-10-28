using Bms.Application.Authorization;
using Bms.Application.Budgeting;
using Bms.Application.Common;
using Bms.Domain.Budgeting;
using Bms.Domain.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using System.Text.Json;

namespace Bms.Infrastructure.Persistence;

public sealed class CompanyBudgetVersionStore(BmsDbContext db) : ICompanyBudgetVersionStore
{
    public async Task<IReadOnlyList<CompanyBudgetVersionHistoryDto>> HistoryAsync(Guid id, int skip, int take, CancellationToken token)
    {
        var objectId = id.ToString("D");
        var entries = await db.BusinessAuditEntries.AsNoTracking()
            .Where(x => x.ObjectType == "CompanyBudgetVersion" && x.ObjectId == objectId)
            .OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id).Skip(skip).Take(take)
            // Do not materialize unbounded legacy snapshots.
            .Select(x => new { x.Id, x.OccurredAtUtc, x.Action,
                Before = x.BeforeJson != null && x.BeforeJson.Length <= 1_000_000 ? x.BeforeJson : null,
                After = x.AfterJson.Length <= 1_000_000 ? x.AfterJson : null }).ToArrayAsync(token);
        var history = new List<CompanyBudgetVersionHistoryDto>();
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            var after = HistorySnapshot(entry.After);
            var before = HistorySnapshot(entry.Before);
            if (after is null || after.Id != id) throw new CompanyBudgetPlanUnavailableException();
            var revision = entry.Action == "CompanyBudgetVersionRevisionCreated";
            if (revision && (before is null || before.Id == id || !before.IsPublished)) throw new CompanyBudgetPlanUnavailableException();
            history.Add(new(entry.Id, entry.OccurredAtUtc, entry.Action, revision ? before!.Id : null,
                revision ? before!.Name : null, before?.InstanceCount, after.InstanceCount));
        }
        return history;
    }
    private static CompanyBudgetVersionDto? HistorySnapshot(string? json)
    {
        if (json is null) return null;
        CompanyBudgetResponseBudget.ValidateStoredJson(json);
        try
        {
            var value = JsonSerializer.Deserialize<CompanyBudgetVersionDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (value is null || value.Id == Guid.Empty) throw new CompanyBudgetPlanUnavailableException();
            CompanyBudgetResponseBudget.Validate(value, default);
            return value;
        }
        catch (JsonException) { throw new CompanyBudgetPlanUnavailableException(); }
    }
    private IQueryable<CompanyBudgetVersion> ReplacementCandidates() => db.CompanyBudgetVersions.AsNoTracking()
        .Where(x => x.PublishedAtUtc.HasValue
            && x.PublishedInstanceCount == db.CompanyBudgetVersionInstances.Count(m => m.VersionId == x.Id)
            && db.CompanyBudgetVersionInstances.Any(m => m.VersionId == x.Id)
            && !db.CompanyBudgetVersionInstances.Any(m => m.VersionId == x.Id
                && !db.BudgetInstances.Any(b => b.Id == m.BudgetInstanceId && b.Status == BudgetInstanceStatus.Approved
                    && b.ApprovalOrdinal.HasValue && b.ApprovedAtUtc.HasValue)));
    private IQueryable<BudgetInstance> Visible(BusinessDataReadScope scope) => scope.CanReadAll
        ? db.BudgetInstances.AsNoTracking()
        : db.BudgetInstances.AsNoTracking().Where(x => scope.CostCenterIds.Contains(x.CostCenterId));
    public async Task<CompanyBudgetVersionPageDto> ListAsync(int skip, int take, BusinessDataReadScope scope,
        bool manage, bool activate, CancellationToken token)
    {
        var active = await CompanyBudgetPlanReader.ActiveAsync(db, token);
        var visible = Visible(scope);
        var rows = db.CompanyBudgetVersions.AsNoTracking();
        var replacementAvailable = activate && scope.CanReadAll
            && await ReplacementCandidates().AnyAsync(x => x.Id != active.VersionId, token);
        if (!scope.CanReadAll) rows = rows.Where(x => db.CompanyBudgetVersionInstances.Any(m => m.VersionId == x.Id
            && visible.Any(b => b.Id == m.BudgetInstanceId)));
        var total = await rows.CountAsync(token);
        var items = await rows.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip(skip).Take(take)
            .Select(x => new CompanyBudgetVersionItemDto(x.Id, x.Name, x.ConcurrencyStamp, x.Id == active.VersionId,
                x.PublishedAtUtc.HasValue,
                db.CompanyBudgetVersionInstances.Count(m => m.VersionId == x.Id && visible.Any(b => b.Id == m.BudgetInstanceId)),
                manage && scope.CanReadAll && !x.PublishedAtUtc.HasValue,
                activate && scope.CanReadAll && x.Id != active.VersionId
                    && db.CompanyBudgetVersionInstances.Any(m => m.VersionId == x.Id)
                    && !db.CompanyBudgetVersionInstances.Any(m => m.VersionId == x.Id
                        && db.BudgetInstances.Any(b => b.Id == m.BudgetInstanceId && b.Status != BudgetInstanceStatus.Approved)),
                !scope.CanReadAll && db.CompanyBudgetVersionInstances.Any(m => m.VersionId == x.Id
                    && db.BudgetInstances.Any(b => b.Id == m.BudgetInstanceId && !scope.CostCenterIds.Contains(b.CostCenterId))),
                manage && scope.CanReadAll && (x.Id != active.VersionId || replacementAvailable),
                activate && scope.CanReadAll && x.Id == active.VersionId && x.PublishedAtUtc.HasValue))
            .ToArrayAsync(token);
        var page = new CompanyBudgetVersionPageDto(items, skip, take, total, manage && scope.CanReadAll,
            active.VersionId, active.ConcurrencyStamp, !scope.CanReadAll);
        CompanyBudgetResponseBudget.Validate(page, token);
        return page;
    }
    public async Task<CompanyBudgetOptionsDto> OptionsAsync(string query, Guid? center, int skip, int take,
        BusinessDataReadScope scope, bool manage, bool activate, CancellationToken token)
    {
        var active = await CompanyBudgetPlanReader.ActiveAsync(db, token);
        var instances = Visible(scope);
        if (center.HasValue) instances = instances.Where(x => x.CostCenterId == center.Value);
        var joined = from instance in instances
            join costCenter in db.CostCenters.AsNoTracking() on instance.CostCenterId equals costCenter.Id
            select new { Instance = instance, CostCenter = costCenter };
        if (query.Length > 0) joined = joined.Where(x => x.Instance.Number.Contains(query)
            || x.CostCenter.Code.Contains(query) || x.CostCenter.Name.Contains(query));
        var total = await joined.CountAsync(token);
        // Keep filtering/count/order/paging on relational fields. Constructed
        // record DTO members cannot be rebound by the SQL Server translator.
        var items = await joined.OrderByDescending(x => x.Instance.ValidFrom).ThenBy(x => x.Instance.Number).ThenBy(x => x.Instance.Id)
            .Skip(skip).Take(take).Select(x => new CompanyBudgetInstanceDto(x.Instance.Id, x.Instance.Number,
                x.Instance.CostCenterId, x.CostCenter.Code, x.CostCenter.Name, x.Instance.ValidFrom,
                x.Instance.ValidTo, x.Instance.Status.ToString(), x.Instance.ConcurrencyStamp)).ToArrayAsync(token);
        var visibleInstances = Visible(scope);
        var centers = db.CostCenters.AsNoTracking().Where(x => !x.IsGroup && visibleInstances.Any(b => b.CostCenterId == x.Id));
        var options = await centers.OrderBy(x => x.Code).Select(x => new BddsCostCenterDto(x.Id, x.Code, x.Name)).ToArrayAsync(token);
        var response = new CompanyBudgetOptionsDto(items, skip, take, total, manage && scope.CanReadAll, activate && scope.CanReadAll,
            active.VersionId, active.ConcurrencyStamp, options);
        CompanyBudgetResponseBudget.Validate(response, token);
        return response;
    }
    public async Task<CompanyBudgetVersionDto> ReadAsync(Guid id, BusinessDataReadScope scope, bool manage, bool activate, CancellationToken token)
    {
        var active = await CompanyBudgetPlanReader.ActiveAsync(db, token);
        var version = await db.CompanyBudgetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new CompanyBudgetVersionNotFoundException();
        var sources = Visible(scope).Where(x => db.CompanyBudgetVersionInstances.Any(m => m.VersionId == id && m.BudgetInstanceId == x.Id));
        var joined = from instance in sources
            join costCenter in db.CostCenters.AsNoTracking() on instance.CostCenterId equals costCenter.Id
            select new { Instance = instance, CostCenter = costCenter };
        var instances = await joined.OrderBy(x => x.CostCenter.Code).ThenBy(x => x.Instance.ValidFrom)
            .ThenBy(x => x.Instance.Number).ThenBy(x => x.Instance.Id)
            .Select(x => new CompanyBudgetInstanceDto(x.Instance.Id, x.Instance.Number, x.Instance.CostCenterId,
                x.CostCenter.Code, x.CostCenter.Name, x.Instance.ValidFrom, x.Instance.ValidTo,
                x.Instance.Status.ToString(), x.Instance.ConcurrencyStamp)).ToArrayAsync(token);
        var headers = await sources.ToArrayAsync(token);
        var headerIds = headers.Select(x => x.Id).ToArray();
        var creationTimes = await db.CompanyBudgetInstanceCreations.AsNoTracking().Where(x => headerIds.Contains(x.BudgetInstanceId))
            .ToDictionaryAsync(x => x.BudgetInstanceId, x => x.CreatedAtUtc, token);
        if (!scope.CanReadAll && headers.Length == 0) throw new CompanyBudgetVersionNotFoundException();
        var overlaps = new List<CompanyBudgetOverlapDto>();
        foreach (var slot in CompanyBudgetOverlapMonths.Read(headers))
        {
                var covered = headers.Where(x => x.CostCenterId == slot.CostCenterId
                    && x.ValidFrom <= slot.Month && x.ValidTo >= slot.Month).ToArray();
                Guid? winner;
                try { winner = CompanyBudgetReportSelection.Winner(covered, slot.CostCenterId, slot.Month, creationTimes)?.Id; }
                catch (CompanyBudgetOrderUnresolvedException) { winner = null; }
                overlaps.Add(new(slot.CostCenterId, instances.First(x => x.CostCenterId == slot.CostCenterId).CostCenterName, slot.Month,
                    covered.Select(x => x.Id).Order().ToArray(), winner,
                    winner.HasValue
                        ? "Несколько документов на ЦФО и месяц: используется один целый документ; Approved выбирается по утверждению, Draft/mixed по дате создания. Суммы документов не складываются."
                        : "Перекрытие Draft/mixed содержит документ без достоверной даты создания; отчёт для этого месяца временно недоступен."));
        }
        var anyDraft = await db.CompanyBudgetVersionInstances.AnyAsync(m => m.VersionId == id
            && db.BudgetInstances.Any(b => b.Id == m.BudgetInstanceId && b.Status != BudgetInstanceStatus.Approved), token);
        var response = new CompanyBudgetVersionDto(version.Id, version.Name, version.ConcurrencyStamp, id == active.VersionId, version.PublishedAtUtc.HasValue,
            instances.Length, manage && scope.CanReadAll && !version.PublishedAtUtc.HasValue,
            activate && scope.CanReadAll && id != active.VersionId && instances.Length > 0 && !anyDraft,
            instances.Select(x => x.Id).ToArray(), instances, overlaps, active.VersionId, active.ConcurrencyStamp,
            !scope.CanReadAll && await db.CompanyBudgetVersionInstances.AnyAsync(m => m.VersionId == id
                && db.BudgetInstances.Any(b => b.Id == m.BudgetInstanceId && !scope.CostCenterIds.Contains(b.CostCenterId)), token), !scope.CanReadAll,
            manage && scope.CanReadAll && (id != active.VersionId || (activate
                && await ReplacementCandidates().AnyAsync(x => x.Id != id, token))),
            activate && scope.CanReadAll && id == active.VersionId && version.PublishedAtUtc.HasValue);
        CompanyBudgetResponseBudget.Validate(response, token);
        return response;
    }
    private async Task ValidateSourcesAsync(IReadOnlyList<Guid> ids, CancellationToken token)
    {
        var headers = await db.BudgetInstances.AsNoTracking().Where(x => ids.Contains(x.Id)).ToArrayAsync(token);
        if (headers.Length != ids.Count)
            throw new DomainRuleViolationException("A selected budget document no longer exists.");
        CompanyBudgetOverlapMonths.Read(headers);
    }
    public async Task CreateAsync(Guid id, CreateCompanyBudgetVersionCommand command, Guid employee, string external,
        DateTimeOffset now, CancellationToken token)
    {
        await ValidateSourcesAsync(command.BudgetInstanceIds, token);
        db.CompanyBudgetVersions.Add(new() { Id = id, Name = command.Name, ConcurrencyStamp = Guid.NewGuid(),
            CreatedAtUtc = now, CreatedByEmployeeId = employee, CreatedByExternalId = external });
        db.CompanyBudgetVersionInstances.AddRange(command.BudgetInstanceIds.Select(budget => new CompanyBudgetVersionInstance
            { VersionId = id, BudgetInstanceId = budget }));
    }
    private async Task<CompanyBudgetVersion> TrackedVersionAsync(Guid id, CancellationToken token)
    {
        foreach (var entry in db.ChangeTracker.Entries<CompanyBudgetVersion>().Where(x => x.Entity.Id == id))
        {
            if (entry.State != EntityState.Unchanged) throw new CompanyBudgetPlanUnavailableException();
            entry.State = EntityState.Detached;
        }
        return await db.CompanyBudgetVersions.SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new CompanyBudgetVersionNotFoundException();
    }
    public async Task UpdateAsync(Guid id, UpdateCompanyBudgetVersionCommand command, CancellationToken token)
    {
        var version = await TrackedVersionAsync(id, token);
        if (version.ConcurrencyStamp != command.ExpectedConcurrencyStamp || version.PublishedAtUtc.HasValue)
            throw new CompanyBudgetVersionConflictException();
        await ValidateSourcesAsync(command.BudgetInstanceIds, token);
        version.Name = command.Name; version.ConcurrencyStamp = Guid.NewGuid();
        var previous = await db.CompanyBudgetVersionInstances.Where(x => x.VersionId == id).ToArrayAsync(token);
        db.CompanyBudgetVersionInstances.RemoveRange(previous.Where(x => !command.BudgetInstanceIds.Contains(x.BudgetInstanceId)));
        var old = previous.Select(x => x.BudgetInstanceId).ToHashSet();
        db.CompanyBudgetVersionInstances.AddRange(command.BudgetInstanceIds.Where(x => !old.Contains(x)).Select(budget =>
            new CompanyBudgetVersionInstance { VersionId = id, BudgetInstanceId = budget }));
    }
    public async Task ActivateAsync(Guid id, ActivateCompanyBudgetVersionCommand command, Guid employee, string external,
        DateTimeOffset now, CancellationToken token)
    {
        var version = await TrackedVersionAsync(id, token);
        foreach (var entry in db.ChangeTracker.Entries<CompanyBudgetActiveVersion>())
        {
            if (entry.State != EntityState.Unchanged) throw new CompanyBudgetPlanUnavailableException();
            entry.State = EntityState.Detached;
        }
        var active = await db.CompanyBudgetActiveVersions.SingleOrDefaultAsync(x => x.Id == 1, token)
            ?? throw new CompanyBudgetPlanUnavailableException();
        if (version.ConcurrencyStamp != command.ExpectedConcurrencyStamp || active.ConcurrencyStamp != command.ExpectedActiveConcurrencyStamp
            || active.VersionId == id) throw new CompanyBudgetVersionConflictException();
        var sources = await db.CompanyBudgetVersionInstances.Where(x => x.VersionId == id).Select(x => x.BudgetInstanceId).ToArrayAsync(token);
        if (sources.Length == 0 || await db.BudgetInstances.CountAsync(x => sources.Contains(x.Id)
            && x.Status == BudgetInstanceStatus.Approved, token) != sources.Length)
            throw new DomainRuleViolationException("An active version may contain only Approved budgets.");
        if (!version.PublishedAtUtc.HasValue)
        {
            version.PublishedAtUtc = now; version.ConcurrencyStamp = Guid.NewGuid();
            version.PublishedInstanceCount = sources.Length;
            // Publish the immutable membership before the pointer trigger validates it.
            await db.SaveChangesAsync(token);
        }
        var prior = active.VersionId;
        active.VersionId = id; active.ConcurrencyStamp = Guid.NewGuid(); active.ActivatedAtUtc = now;
        active.ActivatedByEmployeeId = employee; active.ActivatedByExternalId = external;
        db.CompanyBudgetActivations.Add(new() { Id = Guid.NewGuid(), VersionId = id, PreviousVersionId = prior,
            ActiveConcurrencyStamp = active.ConcurrencyStamp, ActivatedAtUtc = now, EmployeeId = employee, ActorExternalId = external });
    }
    public async Task UpdateActiveCompositionAsync(Guid id, UpdateActiveCompanyBudgetCompositionCommand command,
        Guid employee, string external, DateTimeOffset now, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null || db.ChangeTracker.HasChanges()
            || (db.BudgetRegisterEnabled343 && db.BudgetRegisterReadOnly343)) throw new CompanyBudgetPlanUnavailableException();
        await ValidateSourcesAsync(command.BudgetInstanceIds, token);
        await using var sql = db.Database.GetDbConnection().CreateCommand();
        sql.Transaction = db.Database.CurrentTransaction.GetDbTransaction();
        sql.CommandTimeout = 30; sql.CommandType = CommandType.StoredProcedure;
        sql.CommandText = "bms_version454.UpdateActiveComposition";
        sql.Parameters.Add(new SqlParameter("@VersionId", SqlDbType.UniqueIdentifier) { Value = id });
        sql.Parameters.Add(new SqlParameter("@ExpectedVersionStamp", SqlDbType.UniqueIdentifier) { Value = command.ExpectedConcurrencyStamp });
        sql.Parameters.Add(new SqlParameter("@ExpectedActiveStamp", SqlDbType.UniqueIdentifier) { Value = command.ExpectedActiveConcurrencyStamp });
        sql.Parameters.Add(new SqlParameter("@BudgetInstanceIds", SqlDbType.NVarChar, -1) { Value = JsonSerializer.Serialize(command.BudgetInstanceIds) });
        sql.Parameters.Add(new SqlParameter("@EmployeeId", SqlDbType.UniqueIdentifier) { Value = employee });
        sql.Parameters.Add(new SqlParameter("@ActorExternalId", SqlDbType.NVarChar, 256) { Value = external });
        sql.Parameters.Add(new SqlParameter("@AtUtc", SqlDbType.DateTimeOffset) { Value = now });
        try { await sql.ExecuteNonQueryAsync(token); }
        catch (SqlException e) when (e.Number == 65457) { throw new CompanyBudgetVersionConflictException(); }
        catch (SqlException e) when (e.Number == 65456) { throw new ActorForbiddenException(); }
        catch (SqlException e) when (e.Number == 65455) { throw new DomainRuleViolationException("Only approved existing budget documents are allowed."); }
        catch (SqlException e) when (e.Number == 65454) { throw new CompanyBudgetPlanUnavailableException(); }
        // Raw SQL bypasses SaveChanges' financial-write detector. The existing
        // commit projection must reconcile limits in this SAME transaction,
        // otherwise the accepted execution register would keep stale limits.
        db.BudgetRegisterFinancialWrites343 = true;
        db.BudgetRegisterLimitsOnly454 = true;
        // The procedure changes only composition metadata. Evict stale tracked
        // headers so a later command cannot overwrite the fresh concurrency stamps.
        foreach (var entry in db.ChangeTracker.Entries().Where(e => e.Entity is CompanyBudgetVersion
            or CompanyBudgetActiveVersion or CompanyBudgetVersionInstance or CompanyBudgetActivation).ToArray())
            entry.State = EntityState.Detached;
    }
    public async Task<CompanyBudgetVersionDeletionReceipt> DeleteAsync(Guid id, DeleteCompanyBudgetVersionCommand command,
        Guid employee, string external, DateTimeOffset now, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null) throw new CompanyBudgetPlanUnavailableException();
        var version = await TrackedVersionAsync(id, token);
        if (command.ExpectedConcurrencyStamp == Guid.Empty || version.ConcurrencyStamp != command.ExpectedConcurrencyStamp
            || await db.CompanyBudgetActiveVersions.AnyAsync(x => x.VersionId == id, token))
            throw new CompanyBudgetVersionConflictException();
        var count = await db.CompanyBudgetVersionInstances.CountAsync(x => x.VersionId == id, token);
        if (count > 1000 || employee == Guid.Empty || string.IsNullOrWhiteSpace(external)
            || now == default || now.Offset != TimeSpan.Zero) throw new CompanyBudgetPlanUnavailableException();
        var receipt = new CompanyBudgetVersionDeletionReceipt(id, version.Name, true, count);
        // Logical deletion is deliberate. Immutable published composition, approval
        // snapshots and FK references remain intact; every live version reader uses
        // the model-wide tombstone filter. No original financial row is updated.
        db.CompanyBudgetVersionDeletions.Add(new() { VersionId = id, EmployeeId = employee,
            ActorExternalId = external, DeletedAtUtc = now });
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw new CompanyBudgetVersionConflictException(); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException sql && sql.Number is 547 or 2601 or 2627 or 66005)
        { throw new CompanyBudgetVersionConflictException(); }
        return receipt;
    }
}
