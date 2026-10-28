using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

public sealed partial class AuthorizationDirectoryStore
{
    // Approved company policy: an active employee's active account may use
    // every active organization. Provision ordinary grants at mapping time;
    // do not bypass role/CFO checks in readers, commands or the access scope.
    // The existing audited directory transaction owns SaveChanges and commit.
    private async Task EnsureActiveOrganizationGrants431Async(
        string actorExternalId, CancellationToken cancellationToken)
    {
        EnsureSerializable();
        var grants = await dbContext.ActorOrganizationGrants
            .FromSqlInterpolated($"""
                SELECT [grant].* FROM [bms_web].[ActorOrganizationGrants] AS [grant]
                    WITH (UPDLOCK, HOLDLOCK, INDEX([PK_bms_web_ActorOrganizationGrants]))
                WHERE [grant].[ActorExternalId] = {actorExternalId}
                """)
            .ToListAsync(cancellationToken);
        var organizations = await dbContext.Organizations
            .AsNoTracking().Where(item => item.IsActive).OrderBy(item => item.Id)
            .Select(item => item.Id).ToArrayAsync(cancellationToken);
        foreach (var organizationId in organizations)
        {
            var grant = grants.SingleOrDefault(item => item.OrganizationId == organizationId);
            if (grant is not null)
                grant.IsActive = true;
            else
                dbContext.ActorOrganizationGrants.Add(new ActorOrganizationGrant
                {
                    ActorExternalId = actorExternalId,
                    OrganizationId = organizationId,
                    IsActive = true,
                });
        }
    }
}
