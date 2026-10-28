using System.Data;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Bms.Application.Budgeting;
using Bms.Domain.Budgeting;
using Bms.Domain.Workflow;
using Bms.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class BudgetInstanceStore(BmsDbContext dbContext) : IBudgetInstanceStore
{
    public Task RecordCreationAsync(Guid id, DateTimeOffset createdAtUtc, CancellationToken token)
    {
        EnsureSerializableTransaction();
        token.ThrowIfCancellationRequested();
        if (id == Guid.Empty || createdAtUtc == default || createdAtUtc.Offset != TimeSpan.Zero)
            throw new DomainRuleViolationException("Invalid document creation provenance.");
        dbContext.CompanyBudgetInstanceCreations.Add(new() { BudgetInstanceId = id, CreatedAtUtc = createdAtUtc, Source = "Application" });
        return Task.CompletedTask;
    }
    public async Task ValidateCompanyCompositionWarningsAsync(BudgetInstance changed, CancellationToken token)
    {
        EnsureSerializableTransaction();
        var versions = await dbContext.CompanyBudgetVersionInstances.AsNoTracking()
            .Where(x => x.BudgetInstanceId == changed.Id).Select(x => x.VersionId).ToArrayAsync(token);
        foreach (var version in versions)
        {
            var headers = await dbContext.BudgetInstances.AsNoTracking().Where(x => x.Id != changed.Id
                && dbContext.CompanyBudgetVersionInstances.Any(m => m.VersionId == version && m.BudgetInstanceId == x.Id))
                .ToArrayAsync(token);
            CompanyBudgetOverlapMonths.Read(headers.Append(changed).ToArray());
        }
    }
    public Task<bool> IsInApprovalAsync(Guid id, CancellationToken token) =>
        dbContext.BudgetApprovalSubmissions.AnyAsync(r => r.BudgetInstanceId == id && r.State == BudgetApprovalReviewState.Pending, token);
    /// <summary>
    /// Bounded list issues at most four provider commands: corrupt-scope
    /// scalar, COUNT, OFFSET/FETCH page, then memberships for that authorized page.
    /// Never materializes all candidates or queries per document.
    /// </summary>
    internal const int ListOwnedBudgetCommandBound = 4;

    public Task AddAsync(BudgetInstance instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instance);
        dbContext.BudgetInstances.Add(instance);
        return Task.CompletedTask;
    }

    public async Task RemoveDraftAsync(BudgetInstance instance, CancellationToken cancellationToken)
    {
        EnsureSerializableTransaction();
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(instance);
        if (await dbContext.CompanyBudgetVersionInstances.AnyAsync(x => x.BudgetInstanceId == instance.Id, cancellationToken))
            throw new BudgetInstanceInCompanyVersionException();
        if (instance.Status != BudgetInstanceStatus.Draft
            || await IsInApprovalAsync(instance.Id, cancellationToken)
            || dbContext.Entry(instance).State == EntityState.Detached)
        {
            throw new DomainRuleViolationException("Only a tracked draft budget instance may be deleted.");
        }

        if (await dbContext.BddsSnapshotBudgetInstances.AnyAsync(x => x.BudgetInstanceId == instance.Id, cancellationToken))
            throw new DomainRuleViolationException("A budget retained in a report snapshot cannot be deleted.");

        // Logical deletion: the service commits its immutable deletion audit
        // marker. Keep the entire physical aggregate and all historical FKs.
        var reviews = await dbContext.BudgetApprovalSubmissions.Where(x => x.BudgetInstanceId == instance.Id).ToArrayAsync(cancellationToken);
        ValidateClosedDeletionReviews(reviews);
        var reviewIds = reviews.Select(x => x.Id).ToArray();
        var baselines = await dbContext.CompanyBudgetApprovalBaselines.Where(x => reviewIds.Contains(x.ReviewId)).ToArrayAsync(cancellationToken);
        await ValidateDeletionBaselinesAsync(baselines, cancellationToken);
    }

    public async Task<string?> ReadDraftDeletionHistoryAsync(Guid id, CancellationToken token)
    {
        EnsureSerializableTransaction();
        var reviews = await dbContext.BudgetApprovalSubmissions.AsNoTracking()
            .Where(x => x.BudgetInstanceId == id).OrderBy(x => x.SubmissionNumber).ThenBy(x => x.Id).ToArrayAsync(token);
        ValidateClosedDeletionReviews(reviews);
        var ids = reviews.Select(x => x.Id).ToArray();
        var baselines = await dbContext.CompanyBudgetApprovalBaselines.AsNoTracking()
            .Where(x => ids.Contains(x.ReviewId)).OrderBy(x => x.ReviewId).ToArrayAsync(token);
        await ValidateDeletionBaselinesAsync(baselines, token);
        var creation = await dbContext.CompanyBudgetInstanceCreations.AsNoTracking().SingleOrDefaultAsync(x => x.BudgetInstanceId == id, token);
        return JsonSerializer.Serialize(new { approvalSubmissions = reviews, companyVersionBaselines = baselines,
            creationProvenance = creation }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private async Task ValidateDeletionBaselinesAsync(CompanyBudgetApprovalBaseline[] baselines, CancellationToken token)
    {
        foreach (var id in baselines.Select(x => x.VersionId).Distinct())
        {
            // A historical baseline must continue to reference an independently
            // immutable published version after its live FK is archived.
            if (!await dbContext.CompanyBudgetVersions.AnyAsync(x => x.Id == id
                && x.PublishedAtUtc != null && x.PublishedInstanceCount != null && x.PublishedInstanceCount > 0, token))
                throw new DomainRuleViolationException("Invalid budget approval baseline; deletion refused.");
        }
    }

    private static void ValidateClosedDeletionReviews(BudgetApprovalSubmission[] reviews)
    {
        if (reviews.Any(x => x.State is not (BudgetApprovalReviewState.Returned or BudgetApprovalReviewState.Rejected)))
            throw new DomainRuleViolationException("Only returned or rejected budget reviews may be archived for deletion.");
    }

    public Task<bool> NumberExistsAsync(string number, CancellationToken cancellationToken) =>
        dbContext.BudgetInstances.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(instance => instance.Number == number, cancellationToken);

    public Task<string?> ReadNumberAsync(Guid id, Guid costCenterId, CancellationToken cancellationToken)
    {
        EnsureSerializableTransaction();
        return dbContext.BudgetInstances.AsNoTracking()
            .Where(instance => instance.Id == id && instance.CostCenterId == costCenterId)
            .Select(instance => instance.Number).SingleOrDefaultAsync(cancellationToken);
    }

    public Task<Guid?> FindCostCenterIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.BudgetInstances.AsNoTracking().Where(instance => instance.Id == id)
            .Select(instance => (Guid?)instance.CostCenterId).SingleOrDefaultAsync(cancellationToken);

    public async Task<long> AllocateApprovalOrdinalAsync(
        Guid costCenterId,
        CancellationToken cancellationToken)
    {
        EnsureSerializableTransaction();
        if (costCenterId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Cost center is required for a budget approval.");
        }

        var current = await dbContext.BudgetInstances.AsNoTracking()
            .Where(instance => instance.CostCenterId == costCenterId
                && instance.Status == BudgetInstanceStatus.Approved)
            .Select(instance => (long?)instance.ApprovalOrdinal)
            .MaxAsync(cancellationToken);
        var next = checked((current ?? 0L) + 1L);
        if (next <= 0)
        {
            throw new DomainRuleViolationException("The budget approval sequence is exhausted.");
        }

        return next;
    }

    public Task<BudgetInstanceCostCenterLabelProjection?> ReadCostCenterLabelAsync(
        Guid costCenterId, CancellationToken cancellationToken)
    {
        EnsureSerializableTransaction();
        return dbContext.CostCenters.AsNoTracking()
            .Where(center => center.Id == costCenterId)
            .Select(center => new BudgetInstanceCostCenterLabelProjection(center.Id, center.Code, center.Name))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<BudgetInstance?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        EnsureSerializableTransaction();
        // Tracking queries reuse old objects in a scoped context. Refuse
        // pending changes, otherwise detach ONLY this document and its lines.
        var entries = dbContext.ChangeTracker.Entries().Where(entry => entry.Entity switch
        {
            BudgetInstance instance => instance.Id == id,
            BudgetInstanceLine line => line.BudgetInstanceId == id,
            _ => false
        }).ToArray();
        if (entries.Any(entry => entry.State != EntityState.Unchanged))
        {
            throw new InvalidOperationException("Unsaved budget document changes cannot be discarded.");
        }
        foreach (var entry in entries) { entry.State = EntityState.Detached; }
        return dbContext.BudgetInstances.Include(instance => instance.Lines)
            .SingleOrDefaultAsync(instance => instance.Id == id, cancellationToken);
    }

    public async Task<BudgetDirectoryOptionPageDto> ListMemberCostCentersAsync(
        string actorExternalId, Guid employeeId, string query, DateOnly validFrom,
        DateOnly asOfToday, int skip, int take, CancellationToken cancellationToken)
    {
        EnsureSerializableTransaction();
        var rows = BuildMemberCostCentersQuery(actorExternalId, employeeId, validFrom, asOfToday);
        if (query.Length > 0) { rows = rows.Where(row => row.Code.Contains(query) || row.Name.Contains(query)); }
        var total = await rows.CountAsync(cancellationToken);
        var items = await rows.OrderBy(row => row.Code).ThenBy(row => row.Id).Skip(skip).Take(take)
            .Select(row => new BudgetDirectoryOptionDto(row.Id, row.Code, row.Name, row.IsActive))
            .ToArrayAsync(cancellationToken);
        return new(items, skip, take, total);
    }

    internal IQueryable<CostCenterReference> BuildOwnerCostCentersQuery(Guid employeeId) =>
        dbContext.CostCenters.AsNoTracking().Where(center => center.IsActive && !center.IsGroup
            && !dbContext.CostCenterPurposes457.Any(s => s.CostCenterId == center.Id && s.OnlyCostDepartment)
            && BusinessDataReadScopeQuery.OwnedCostCenterIds(dbContext, employeeId).Contains(center.Id));

    public async Task<BudgetDirectoryOptionPageDto> ListVisibleCostCentersAsync(
        string actorExternalId, Guid employeeId, string query, DateOnly validFrom,
        DateOnly asOfToday, int skip, int take, CancellationToken cancellationToken)
    {
        EnsureSerializableTransaction();
        var visible = BusinessDataReadScopeQuery.VisibleCostCenterIds(dbContext, actorExternalId);
        var rows = dbContext.CostCenters.AsNoTracking().Where(center => center.IsActive && !center.IsGroup
            && !dbContext.CostCenterPurposes457.Any(s => s.CostCenterId == center.Id && s.OnlyCostDepartment)
            && visible.Contains(center.Id) && (query == "" || center.Code.Contains(query) || center.Name.Contains(query)));
        var total = await rows.CountAsync(cancellationToken);
        var items = await rows.OrderBy(row => row.Code).ThenBy(row => row.Id).Skip(skip).Take(take)
            .Select(row => new BudgetDirectoryOptionDto(row.Id, row.Code, row.Name, row.IsActive))
            .ToArrayAsync(cancellationToken);
        return new(items, skip, take, total);
    }

    public async Task<BudgetDirectoryOptionPageDto> ListOwnerCostCentersAsync(
        Guid employeeId, int skip, int take, CancellationToken cancellationToken)
    {
        EnsureSerializableTransaction();
        var rows = BuildOwnerCostCentersQuery(employeeId);
        var total = await rows.CountAsync(cancellationToken);
        var items = await rows.OrderBy(row => row.Code).ThenBy(row => row.Id).Skip(skip).Take(take)
            .Select(row => new BudgetDirectoryOptionDto(row.Id, row.Code, row.Name, row.IsActive))
            .ToArrayAsync(cancellationToken);
        return new(items, skip, take, total);
    }

    public async Task<BudgetDirectoryOptionPageDto> ListActiveCostCentersAsync(
        string query, int skip, int take, CancellationToken cancellationToken)
    {
        EnsureSerializableTransaction();
        var rows = dbContext.CostCenters.AsNoTracking().Where(row => row.IsActive && !row.IsGroup
            && !dbContext.CostCenterPurposes457.Any(s => s.CostCenterId == row.Id && s.OnlyCostDepartment));
        if (query.Length > 0) { rows = rows.Where(row => row.Code.Contains(query) || row.Name.Contains(query)); }
        var total = await rows.CountAsync(cancellationToken);
        var items = await rows.OrderBy(row => row.Code).ThenBy(row => row.Id).Skip(skip).Take(take)
            .Select(row => new BudgetDirectoryOptionDto(row.Id, row.Code, row.Name, row.IsActive))
            .ToArrayAsync(cancellationToken);
        return new(items, skip, take, total);
    }

    internal IQueryable<CostCenterReference> BuildMemberCostCentersQuery(
        string actorExternalId, Guid employeeId, DateOnly validFrom, DateOnly asOfToday)
    {
        var initiatorRoles = BusinessDataReadScopeQuery.RoleCodes(dbContext,
            dbContext.Employees.AsNoTracking().Where(employee => employee.Id == employeeId && employee.IsActive).Select(employee => employee.Id));
        var visible = BusinessDataReadScopeQuery.VisibleCostCenterIds(dbContext, actorExternalId);
        var creators = BusinessDataReadScopeQuery.OwnedCostCenterIds(dbContext, employeeId);
        var grantActor471 = BusinessDataReadScopeQuery.GrantActor471(dbContext,actorExternalId);
        return dbContext.CostCenters.AsNoTracking().Where(center =>
            center.IsActive && !center.IsGroup
            && !dbContext.CostCenterPurposes457.Any(s => s.CostCenterId == center.Id && s.OnlyCostDepartment)
            && visible.Contains(center.Id)
            && dbContext.Employees.Any(employee => employee.Id == employeeId && employee.IsActive)
            && (
                (initiatorRoles.Any(role => role == ExpenseRequestWorkflowCodes.CfoInitiatorRoleCode)
                 && dbContext.CfoInitiatorCostCenterAssignments.AsNoTracking().Any(assignment =>
                    assignment.EmployeeId == employeeId && assignment.CostCenterId == center.Id && assignment.IsActive))
                ||
                (dbContext.BudgetCostCenterMemberships.Any(member =>
                    member.EmployeeId == employeeId && member.CostCenterId == center.Id && member.IsActive
                    && (member.MembershipKind == BudgetCostCenterMembershipKinds.Owner
                        || member.MembershipKind == BudgetCostCenterMembershipKinds.Participant))
                 && dbContext.ActorCostCenterAccess.Where(access => access.ActorExternalId == grantActor471
                        && access.CostCenterId == center.Id && access.EffectiveFrom <= asOfToday)
                    .OrderByDescending(access => access.EffectiveFrom).Select(access => access.IsAllowed).FirstOrDefault()
                 && dbContext.ActorCostCenterAccess.Where(access => access.ActorExternalId == grantActor471
                        && access.CostCenterId == center.Id && access.EffectiveFrom <= validFrom)
                    .OrderByDescending(access => access.EffectiveFrom).Select(access => access.IsAllowed).FirstOrDefault())
                || (!dbContext.BudgetCostCenterMemberships.Any(member =>
                        member.EmployeeId == employeeId && member.CostCenterId == center.Id)
                    && creators.Contains(center.Id))
            ));
    }

    public async Task<(IReadOnlyList<BudgetInstanceListProjection> Items, int Total)> ListOwnedBudgetPageAsync(
        string actorExternalId,
        Guid ownerEmployeeId,
        Guid? costCenterId,
        DateOnly? periodFrom,
        DateOnly? periodTo,
        DateOnly asOfToday,
        int skip,
        int take,
        bool approverCanSeeAll,
        CancellationToken cancellationToken)
        => await ListOwnedBudgetPageCoreAsync(actorExternalId, ownerEmployeeId, costCenterId, periodFrom, periodTo,
            asOfToday, skip, take, approverCanSeeAll, cancellationToken, null);

    public Task<(IReadOnlyList<BudgetInstanceListProjection> Items, int Total)> ListOwnedBudgetPageForVersionAsync(
        string actorExternalId, Guid ownerEmployeeId, Guid? costCenterId,
        DateOnly? periodFrom, DateOnly? periodTo, DateOnly asOfToday,
        int skip, int take, bool approverCanSeeAll, Guid versionId, CancellationToken cancellationToken) =>
        ListOwnedBudgetPageCoreAsync(actorExternalId, ownerEmployeeId, costCenterId, periodFrom, periodTo,
            asOfToday, skip, take, approverCanSeeAll, cancellationToken, versionId);

    internal IQueryable<BudgetInstance> ApplyVersionFilter(IQueryable<BudgetInstance> authorized, Guid? versionId) =>
        versionId is Guid selected
            ? authorized.Where(instance => dbContext.CompanyBudgetVersions.Any(version => version.Id == selected
                && dbContext.CompanyBudgetVersionInstances.Any(link => link.VersionId == version.Id && link.BudgetInstanceId == instance.Id)))
            : authorized;

    private async Task<(IReadOnlyList<BudgetInstanceListProjection> Items, int Total)> ListOwnedBudgetPageCoreAsync(
        string actorExternalId, Guid ownerEmployeeId, Guid? costCenterId,
        DateOnly? periodFrom, DateOnly? periodTo, DateOnly asOfToday,
        int skip, int take, bool approverCanSeeAll, CancellationToken cancellationToken, Guid? versionId)
    {
        EnsureSerializableTransaction();
        if (string.IsNullOrWhiteSpace(actorExternalId)
            || skip < 0
            || take < 1)
        {
            return (Array.Empty<BudgetInstanceListProjection>(), 0);
        }

        // Financial read roles see all CFO documents; ordinary readers use
        // canonical owner assignments, never dated editor membership or grants.
        // Create/edit/approval/deletion remain separate command authorities.
        var authorized = approverCanSeeAll
            ? BuildApproverBudgetQuery(costCenterId, periodFrom, periodTo)
            : BuildAuthorizedBudgetQuery(
                actorExternalId,
                ownerEmployeeId,
                costCenterId,
                periodFrom,
                periodTo,
                asOfToday);
        authorized = ApplyVersionFilter(authorized, versionId);

        // Fail-closed: authorized corrupt line years must not silently vanish.
        // Bounded scalar EXISTS — does not load candidate rows.
        var corruptAuthorized = await authorized.AnyAsync(
            instance =>
                !instance.Lines.Any()
                || instance.Lines.Any(line =>
                    line.BudgetMonth.Day != 1
                    || line.BudgetMonth < instance.ValidFrom
                    || line.BudgetMonth > instance.ValidTo),
            cancellationToken);
        if (corruptAuthorized)
        {
            throw new DomainRuleViolationException(
                "Budget instance line months are inconsistent with the header.");
        }

        var filtered = authorized.Where(instance =>
            instance.Lines.Any()
            && instance.Lines.All(line =>
                line.BudgetMonth.Day == 1
                && line.BudgetMonth >= instance.ValidFrom
                && line.BudgetMonth <= instance.ValidTo));

        var total = await filtered.CountAsync(cancellationToken);
        if (total == 0 || skip >= total)
        {
            return (Array.Empty<BudgetInstanceListProjection>(), total);
        }

        var items = await BuildListPageProjection(filtered, skip, take, asOfToday).ToListAsync(cancellationToken);
        var memberships = await BuildPageVersionMembershipQuery(items.Select(item => item.Id).ToArray())
            .ToListAsync(cancellationToken);
        var byInstance = memberships.GroupBy(link => link.InstanceId).ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<BudgetInstanceVersionMembershipDto>)group
                .Select(link => new BudgetInstanceVersionMembershipDto(link.VersionId, link.Name)).ToArray());
        return (items.Select(item => item with {
            IncludedVersions = byInstance.GetValueOrDefault(item.Id, [])
        }).ToArray(), total);
    }

    internal sealed record PageVersionMembership(Guid InstanceId, Guid VersionId, string Name);

    // Call only with IDs returned by the authorized OFFSET/FETCH page above.
    // A version need not be active/published to contain this document.
    internal IQueryable<PageVersionMembership> BuildPageVersionMembershipQuery(Guid[] instanceIds) =>
        (from link in dbContext.CompanyBudgetVersionInstances.AsNoTracking()
         join version in dbContext.CompanyBudgetVersions.AsNoTracking() on link.VersionId equals version.Id
         where instanceIds.Contains(link.BudgetInstanceId)
         orderby version.Name, version.Id
         select new PageVersionMembership(link.BudgetInstanceId, version.Id, version.Name));

    /// <summary>
    /// Financial directors may work with documents from every active non-group
    /// cost centre without membership. This query returns document data only;
    /// command capability is resolved independently from the current role rows.
    /// </summary>
    internal IQueryable<BudgetInstance> BuildApproverBudgetQuery(
        Guid? costCenterId,
        DateOnly? periodFrom,
        DateOnly? periodTo)
    {
        var query = dbContext.BudgetInstances.AsNoTracking()
            .Join(
                dbContext.CostCenters.AsNoTracking(),
                instance => instance.CostCenterId,
                center => center.Id,
                (instance, center) => new { instance, center })
            .Where(row => row.center.IsActive && !row.center.IsGroup)
            .Select(row => row.instance);

        if (costCenterId is Guid costCenterFilter)
        {
            query = query.Where(instance => instance.CostCenterId == costCenterFilter);
        }

        if (periodFrom is DateOnly from)
        {
            query = query.Where(instance => instance.ValidTo >= from);
        }

        if (periodTo is DateOnly to)
        {
            query = query.Where(instance => instance.ValidFrom <= to);
        }

        return query;
    }

    // The additional label JOIN is in the SAME bounded page command. Neither
    // the unrelated option picker nor a per-document lookup supplies names.
    internal IQueryable<BudgetInstanceListProjection> BuildListPageProjection(
        IQueryable<BudgetInstance> authorizedRows, int skip, int take, DateOnly asOfToday) =>
        (from instance in authorizedRows
         join center in dbContext.CostCenters.AsNoTracking()
             on instance.CostCenterId equals center.Id
         // OwnerEmployeeId is captured from the creator at CreateDraft and is
         // immutable. Do not substitute the current CFO owner or filter out
         // historical documents when their author is inactive or unavailable.
         join employee in dbContext.Employees.AsNoTracking()
             on instance.OwnerEmployeeId equals employee.Id into authors
         from author in authors.DefaultIfEmpty()
         select new { Instance = instance, center.Code, center.Name,
             AuthorName = author == null ? null : author.Name })
        .OrderByDescending(row => row.Instance.ValidFrom)
        .ThenBy(row => row.Instance.Number)
        .Skip(skip).Take(take)
        .Select(row => new BudgetInstanceListProjection(
            row.Instance.Id, row.Instance.Number, row.Instance.CostCenterId,
            row.Instance.ValidFrom, row.Instance.ValidTo, row.Instance.Status.ToString(),
            row.Instance.Lines.Count(),
            row.Instance.Lines.Where(line => line.FlowDirection == BudgetFlowDirection.Payment)
                .Sum(line => line.LimitAmountKzt),
            row.Instance.ConcurrencyStamp, row.Code, row.Name,
            row.Instance.Lines.Where(line => line.FlowDirection == BudgetFlowDirection.Receipt)
                .Sum(line => line.LimitAmountKzt),
            row.Instance.Lines.Where(line => line.FlowDirection == BudgetFlowDirection.Payment)
                .Sum(line => line.LimitAmountKzt),
            row.Instance.Lines.Where(line => line.FlowDirection == BudgetFlowDirection.Receipt)
                .Sum(line => line.LimitAmountKzt)
                - row.Instance.Lines.Where(line => line.FlowDirection == BudgetFlowDirection.Payment)
                    .Sum(line => line.LimitAmountKzt),
            row.Instance.Status == BudgetInstanceStatus.Approved
                && row.Instance.ValidFrom <= asOfToday && row.Instance.ValidTo >= asOfToday
                && dbContext.CompanyBudgetVersionInstances.Any(m => m.BudgetInstanceId == row.Instance.Id
                    && dbContext.CompanyBudgetActiveVersions.Any(a => a.Id == 1 && a.VersionId == m.VersionId))
                && dbContext.BudgetInstances.AsNoTracking()
                    .Where(candidate => candidate.CostCenterId == row.Instance.CostCenterId
                        && candidate.Status == BudgetInstanceStatus.Approved
                        && dbContext.CompanyBudgetVersionInstances.Any(m => m.BudgetInstanceId == candidate.Id
                            && dbContext.CompanyBudgetActiveVersions.Any(a => a.Id == 1 && a.VersionId == m.VersionId))
                        && candidate.ValidFrom <= asOfToday && candidate.ValidTo >= asOfToday)
                    .OrderByDescending(candidate => candidate.ApprovalOrdinal)
                    .ThenByDescending(candidate => candidate.ApprovedAtUtc)
                    .ThenByDescending(candidate => candidate.Id)
                    .Select(candidate => candidate.Id).FirstOrDefault() == row.Instance.Id,
            dbContext.BudgetApprovalSubmissions.AsNoTracking()
                .Where(review => review.BudgetInstanceId == row.Instance.Id)
                .OrderByDescending(review => review.SubmissionNumber)
                .Select(review => review.State.ToString()).FirstOrDefault(),
            dbContext.CompanyBudgetVersionInstances.Any(m => m.BudgetInstanceId == row.Instance.Id),
            row.AuthorName, null));

    /// <summary>
    /// Authorized budget instances before line-year integrity filtering.
    /// </summary>
    internal IQueryable<BudgetInstance> BuildAuthorizedBudgetQuery(
        string actorExternalId,
        Guid ownerEmployeeId,
        Guid? costCenterId,
        DateOnly? periodFrom,
        DateOnly? periodTo,
        DateOnly asOfToday)
    {
        if (string.IsNullOrWhiteSpace(actorExternalId))
        {
            throw new ArgumentException("Actor external id is required.", nameof(actorExternalId));
        }

        var query =
            from instance in dbContext.BudgetInstances.AsNoTracking()
            join costCenter in dbContext.CostCenters.AsNoTracking()
                on instance.CostCenterId equals costCenter.Id
            where dbContext.Employees.Any(employee => employee.Id == ownerEmployeeId && employee.IsActive)
                  && costCenter.IsActive
                  && !costCenter.IsGroup
                  && BusinessDataReadScopeQuery.VisibleCostCenterIds(dbContext, actorExternalId)
                      .Contains(instance.CostCenterId)
                  && instance.ValidFrom.Day == 1
                  && instance.ValidFrom <= instance.ValidTo
                  && instance.ValidTo.Day == (instance.ValidTo.Month == 2
                      ? (instance.ValidTo.Year % 4 == 0
                         && (instance.ValidTo.Year % 100 != 0 || instance.ValidTo.Year % 400 == 0) ? 29 : 28)
                      : (instance.ValidTo.Month == 4 || instance.ValidTo.Month == 6
                         || instance.ValidTo.Month == 9 || instance.ValidTo.Month == 11 ? 30 : 31))
            select instance;

        if (costCenterId is Guid costCenterFilter)
        {
            query = query.Where(instance => instance.CostCenterId == costCenterFilter);
        }

        // Inclusive interval overlap, including a document starting in a prior year.
        if (periodFrom is DateOnly from)
        {
            query = query.Where(instance => instance.ValidTo >= from);
        }

        if (periodTo is DateOnly to)
        {
            query = query.Where(instance => instance.ValidFrom <= to);
        }

        return query;
    }

    /// <summary>
    /// Provider translation surface for unit tests: access filters plus
    /// post-authorization line integrity used by count/page.
    /// </summary>
    internal IQueryable<BudgetInstance> BuildVisibleOwnedBudgetQuery(
        string actorExternalId,
        Guid ownerEmployeeId,
        Guid? costCenterId,
        DateOnly? periodFrom,
        DateOnly? periodTo,
        DateOnly asOfToday) =>
        BuildAuthorizedBudgetQuery(
                actorExternalId,
                ownerEmployeeId,
                    costCenterId,
                periodFrom,
                periodTo,
                asOfToday)
            .Where(instance =>
                instance.Lines.Any()
                && instance.Lines.All(line =>
                    line.BudgetMonth.Day == 1
                    && line.BudgetMonth >= instance.ValidFrom
                    && line.BudgetMonth <= instance.ValidTo));

    public Task<bool> CashFlowItemExistsAsync(Guid cashFlowItemId, CancellationToken cancellationToken) =>
        dbContext.CashFlowItems.AsNoTracking()
            .AnyAsync(item => item.Id == cashFlowItemId, cancellationToken);

    public Task<bool> CashFlowItemBelongsToCostCenterAsync(Guid cashFlowItemId, Guid costCenterId,
        CancellationToken cancellationToken)
    {
        EnsureSerializableTransaction();
        return dbContext.CashFlowItems.AsNoTracking().AnyAsync(item => item.Id == cashFlowItemId
            && item.IsActive && !item.IsGroup && item.CostCenterId == costCenterId, cancellationToken);
    }

    public Task<bool> CostCenterIsActiveNonGroupAsync(Guid costCenterId, CancellationToken cancellationToken) =>
        dbContext.CostCenters.AsNoTracking()
            .AnyAsync(
                center => center.Id == costCenterId && center.IsActive && !center.IsGroup
                    && !dbContext.CostCenterPurposes457.Any(s => s.CostCenterId == center.Id && s.OnlyCostDepartment),
                cancellationToken);

    private void EnsureSerializableTransaction()
    {
        var transaction = dbContext.Database.CurrentTransaction;
        if (transaction is null ||
            transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
        {
            throw new InvalidOperationException(
                "Budget-instance list requires an active Serializable transaction.");
        }
    }
}
