using System.Data;
using Bms.Application.Accruals.Notifications;
using Bms.Application.Common;
using Bms.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

internal sealed class EfCoreAccrualNotificationReader(BmsDbContext dbContext) : IAccrualNotificationReader
{
    public async Task<AccrualNotificationPageDto> ReadAsync(string actorExternalId,
        int skip, int take, CancellationToken cancellationToken)
    {
        if (!ActorIdentity.IsCanonical(actorExternalId) || skip < 0 || take is < 1 or > 100 ||
            skip > int.MaxValue - take || dbContext.Database.CurrentTransaction is not null ||
            dbContext.ChangeTracker.HasChanges())
        {
            throw new AccrualNotificationReadUnavailableException();
        }
        return await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            // The current mapping, role, grant, count and page share one view.
            // Administrator status deliberately has no include-all branch.
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, token);
            var actorEmployees = BusinessDataReadScopeQuery.EmployeeIds(dbContext, actorExternalId);
            var recipientIds = await actorEmployees.Take(2).ToArrayAsync(token);
            if (recipientIds.Length > 1) throw new AccrualNotificationReadUnavailableException();
            // Reuse the canonical financial scope, resolved afresh inside this
            // very attempt. Do not expand employee/role/CFO authority for every
            // request considered by the count and page query.
            var scope = await BusinessDataReadScopeQuery.ResolveAsync(dbContext, actorExternalId, token);
            var isResponsibleAccountant = await BusinessDataReadScopeQuery.RoleCodes(dbContext, actorEmployees)
                .AnyAsync(role => role == ExpenseRequestWorkflowCodes.ResponsibleAccountantRoleCode, token);
            var query =
                from notification in dbContext.AccrualNotifications.AsNoTracking()
                join request in BusinessDataReadScopeQuery.ApplyCapturedRequests(
                    dbContext.ExpenseRequests.AsNoTracking(), scope)
                    on notification.ExpenseRequestId equals request.Id
                join organization in dbContext.Organizations.AsNoTracking()
                    on notification.OrganizationId equals organization.Id
                where request.OrganizationId == notification.OrganizationId &&
                    notification.NotificationType == AccrualNotificationCodes.RedZone &&
                    recipientIds.Contains(notification.RecipientEmployeeId) &&
                    (request.InitiatorExternalId == actorExternalId ||
                        (organization.IsActive &&
                         dbContext.ActorOrganizationGrants.Any(grant => grant.ActorExternalId == actorExternalId &&
                            grant.OrganizationId == notification.OrganizationId && grant.IsActive) &&
                         isResponsibleAccountant))
                select new
                {
                    notification.Id,
                    notification.ExpenseRequestId,
                    OrganizationName = organization.Name,
                    notification.CreatedAtUtc,
                    notification.FullyPaidAtUtc,
                    notification.PaymentBusinessDate,
                    notification.AsOfDate,
                    notification.AgeDays,
                    notification.RedFromDays,
                    notification.RemainingAmountKzt
                };
            query = query.TagWith("W603 personal notification captured current authority");
            var total = await query.CountAsync(token);
            var rows = await query.OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id)
                .Skip(skip).Take(take).ToArrayAsync(token);
            await transaction.CommitAsync(token);
            return new AccrualNotificationPageDto(total, skip, take, rows.Select(row =>
                new AccrualNotificationDto(row.Id, row.ExpenseRequestId, row.OrganizationName,
                    row.CreatedAtUtc, row.FullyPaidAtUtc, row.PaymentBusinessDate, row.AsOfDate,
                    row.AgeDays, row.RedFromDays, row.RemainingAmountKzt)).ToArray());
        }, cancellationToken);
    }

    public async Task<AccrualNotificationIssueCounts> ReadIssueCountsAsync(CancellationToken cancellationToken)
    {
        var counts = await dbContext.AccrualNotificationIssues.AsNoTracking()
            .Where(issue => issue.ResolvedAtUtc == null).GroupBy(issue => issue.Code)
            .Select(group => new { Code = group.Key, Count = group.Count() }).ToArrayAsync(cancellationToken);
        if (counts.Any(item => item.Code is not (AccrualNotificationCodes.MissingInitiator or
                AccrualNotificationCodes.MissingAccountant)))
        {
            throw new AccrualNotificationReadUnavailableException();
        }
        return new AccrualNotificationIssueCounts(
            counts.SingleOrDefault(item => item.Code == AccrualNotificationCodes.MissingInitiator)?.Count ?? 0,
            counts.SingleOrDefault(item => item.Code == AccrualNotificationCodes.MissingAccountant)?.Count ?? 0);
    }
}
