using System.Data;
using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bms.Infrastructure.Persistence;

public sealed class SqlServerAccessScope(
    BmsDbContext dbContext,
    BudgetOwnerCreationAuthority? draftAuthority = null) : IAccessScope
{
    private readonly BudgetOwnerCreationAuthority draftAuthority = draftAuthority ?? new(
        new EmployeeActorResolver(new EmployeeActorMappingReader(dbContext)),
        new EmployeeFunctionalRoleResolver(new EmployeeFunctionalRoleAssignmentReader(dbContext)),
        new BudgetOwnerCostCenterAuthorityReader(dbContext));

    public Task<bool> CanUseForAuthorizedExpenseDraftAsync(string actorExternalId, Guid? organizationId,
        IReadOnlyCollection<Guid> costCenterIds, DateOnly asOf,
        BudgetOwnerCreationAuthority principalAuthority, CancellationToken cancellationToken) =>
        CanUseCoreAsync(actorExternalId, organizationId, costCenterIds, asOf,
            allowFinancialDirectorDraft: true, cancellationToken, principalAuthority);

    public Task<bool> CanUseAsync(
        string actorExternalId,
        Guid? organizationId,
        IReadOnlyCollection<Guid> costCenterIds,
        DateOnly asOf,
        CancellationToken cancellationToken) =>
        CanUseCoreAsync(actorExternalId, organizationId, costCenterIds, asOf,
            allowFinancialDirectorDraft: false, cancellationToken);

    public Task<bool> CanUseForExpenseDraftAsync(
        string actorExternalId,
        Guid? organizationId,
        IReadOnlyCollection<Guid> costCenterIds,
        DateOnly asOf,
        CancellationToken cancellationToken) =>
        CanUseCoreAsync(actorExternalId, organizationId, costCenterIds, asOf,
            allowFinancialDirectorDraft: true, cancellationToken);

    private async Task<bool> CanUseCoreAsync(
        string actorExternalId,
        Guid? organizationId,
        IReadOnlyCollection<Guid> costCenterIds,
        DateOnly asOf,
        bool allowFinancialDirectorDraft,
        CancellationToken cancellationToken,
        BudgetOwnerCreationAuthority? principalAuthority = null)
    {
        ValidateActor(actorExternalId);
        ArgumentNullException.ThrowIfNull(costCenterIds);
        EnsureSerializableTransaction();
        var authority = principalAuthority ?? draftAuthority;
        // Grants belong to the represented role's principal, never to a union
        // of principal and deputy scopes. Physical actor remains in every audit.
        if (dbContext.ActiveRoleSelection?.Substitution471 is { } proof)
        {
            await StaffSubstitutionGuard471.ValidateAsync(dbContext, cancellationToken);
            actorExternalId = proof.PrincipalActorExternalId;
            authority = new BudgetOwnerCreationAuthority(
                new EmployeeActorResolver(new EmployeeActorMappingReader(dbContext)),
                new EmployeeFunctionalRoleResolver(new EmployeeFunctionalRoleAssignmentReader(dbContext), dbContext.ActiveRoleSelection),
                new BudgetOwnerCostCenterAuthorityReader(dbContext));
        }

        // Canonical owner/initiator assignments are the scoped draft authority;
        // legacy dated grants are not required to save a draft in one's own CFO.
        // This does not broaden ordinary workflow/closing CanUseAsync.
        if (allowFinancialDirectorDraft)
        {
            var ids = costCenterIds.Distinct().ToArray();
            var scoped = ids.Length == 0
                ? (await authority.ResolveAnyAsync(actorExternalId, cancellationToken)).CanCreateExpenseRequest
                : !ids.Contains(Guid.Empty);
            foreach (var id in ids)
                scoped &= (await authority.ResolveAsync(actorExternalId, id, cancellationToken)).CanCreateExpenseRequest;
            if (scoped)
                return organizationId is not Guid organization || await dbContext.Organizations.AsNoTracking()
                    .AnyAsync(item => item.Id == organization && item.IsActive, cancellationToken);
        }

        // The financial director may prepare drafts for all active reference
        // targets, but never for an inactive/missing organization or group CFO.
        // This is current SQL-backed draft authority, not a read-scope flag or
        // an administrator/profile claim supplied by the client.
        if (allowFinancialDirectorDraft &&
            await authority.HasFinancialDraftAuthorityAsync(actorExternalId, cancellationToken))
        {
            if (organizationId is Guid organization && !await dbContext.Organizations
                    .AsNoTracking().AnyAsync(item => item.Id == organization && item.IsActive, cancellationToken))
            {
                return false;
            }
            var ids = costCenterIds.Distinct().OrderBy(id => id).ToArray();
            if (ids.Contains(Guid.Empty)) return false;
            if (ids.Length == 0) return true;
            var activeIds = await dbContext.CostCenters.AsNoTracking()
                .Where(item => ids.Contains(item.Id) && item.IsActive && !item.IsGroup)
                .Select(item => item.Id).ToArrayAsync(cancellationToken);
            return activeIds.Length == ids.Length;
        }

        if (organizationId.HasValue)
        {
            var grants = await dbContext.ActorOrganizationGrants
                .FromSqlInterpolated($$"""
                    SELECT [grant].[ActorExternalId], [grant].[OrganizationId], [grant].[IsActive]
                    FROM [bms_web].[ActorOrganizationGrants] AS [grant]
                        WITH (HOLDLOCK, INDEX([PK_bms_web_ActorOrganizationGrants]))
                    INNER JOIN [bms_web].[Organizations] AS [organization]
                        WITH (HOLDLOCK, INDEX([PK_bms_web_Organizations]))
                        ON [organization].[Id] = [grant].[OrganizationId]
                    WHERE [grant].[ActorExternalId] = {{actorExternalId}}
                      AND [grant].[OrganizationId] = {{organizationId.Value}}
                      AND [grant].[IsActive] = CAST(1 AS bit)
                      AND [organization].[IsActive] = CAST(1 AS bit)
                    """)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            if (grants.Count != 1)
            {
                return false;
            }
        }

        var requestedCostCenterIds = costCenterIds.Distinct().OrderBy(id => id).ToArray();
        if (requestedCostCenterIds.Length == 0)
        {
            return true;
        }

        if (requestedCostCenterIds.Contains(Guid.Empty))
        {
            return false;
        }

        var requestedIdsJson = JsonSerializer.Serialize(requestedCostCenterIds);
        var currentAccess = await dbContext.ActorCostCenterAccess
            .FromSqlInterpolated($$"""
                WITH RankedAccess AS
                (
                    SELECT
                        [access].[ActorExternalId], [access].[CostCenterId],
                        [access].[EffectiveFrom], [access].[IsAllowed],
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY [access].[CostCenterId]
                            ORDER BY [access].[EffectiveFrom] DESC
                        ) AS [ScopeRank]
                    FROM [bms_web].[ActorCostCenterAccess] AS [access]
                        WITH (HOLDLOCK, INDEX([PK_bms_web_ActorCostCenterAccess]))
                    INNER JOIN OPENJSON({{requestedIdsJson}})
                        WITH ([CostCenterId] uniqueidentifier '$') AS [requested]
                        ON [requested].[CostCenterId] = [access].[CostCenterId]
                    INNER JOIN [bms_web].[CostCenters] AS [costCenter]
                        WITH (HOLDLOCK, INDEX([PK_bms_web_CostCenters]))
                        ON [costCenter].[Id] = [access].[CostCenterId]
                    WHERE [access].[ActorExternalId] = {{actorExternalId}}
                      AND [access].[EffectiveFrom] <= {{asOf}}
                      AND [costCenter].[IsActive] = CAST(1 AS bit)
                      AND [costCenter].[IsGroup] = CAST(0 AS bit)
                )
                SELECT [ActorExternalId], [CostCenterId], [EffectiveFrom], [IsAllowed]
                FROM RankedAccess
                WHERE [ScopeRank] = 1
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var allowedIds = currentAccess
            .Where(access => access.IsAllowed)
            .Select(access => access.CostCenterId)
            .ToHashSet();
        return requestedCostCenterIds.All(allowedIds.Contains);
    }

    private void EnsureSerializableTransaction()
    {
        var transaction = dbContext.Database.CurrentTransaction;
        if (transaction is null ||
            transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
        {
            throw new InvalidOperationException(
                "Access-scope validation requires an active Serializable transaction.");
        }
    }

    private static void ValidateActor(string actorExternalId)
    {
        if (string.IsNullOrWhiteSpace(actorExternalId))
        {
            throw new ArgumentException("Actor external id is required.", nameof(actorExternalId));
        }

    }
}
