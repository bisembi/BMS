using System.Data;
using Bms.Application.Accruals.Notifications;
using Bms.Application.Abstractions;
using Bms.Application.Common;
using Microsoft.EntityFrameworkCore;
using Bms.Domain.Workflow;

namespace Bms.Infrastructure.Persistence;

internal sealed class EfCoreAccrualNotificationAdministrationReader(BmsDbContext dbContext, ICurrentActor currentActor)
    : IAccrualNotificationAdministrationReader
{
    public async Task<AdministrativeAccrualNotificationPageDto> ReadQueueAsync(
        int skip, int take, CancellationToken cancellationToken)
    {
        AccrualNotificationAdministrationService.RequireFinancialReader(currentActor);
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        RequireCleanRead(skip, take);
        return await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, token);
            // User-approved FIN_CONTROLLER/FIN_DIRECTOR register, not technical
            // administration. Prove the exact current mapping/employee/selected
            // role once inside this Serializable attempt, before count/page.
            // Both approved roles have global register access; do not expand
            // correlated authority predicates for each financial request row.
            if (!await FinancialRoleCodes(actorExternalId).AnyAsync(token))
                throw new ActorForbiddenException();
            var query =
                from notification in dbContext.AccrualNotifications.AsNoTracking()
                join request in dbContext.ExpenseRequests.AsNoTracking()
                    on notification.ExpenseRequestId equals request.Id
                join organization in dbContext.Organizations.AsNoTracking()
                    on notification.OrganizationId equals organization.Id
                join employee in dbContext.Employees.AsNoTracking()
                    on notification.RecipientEmployeeId equals employee.Id
                where notification.NotificationType == AccrualNotificationCodes.RedZone &&
                    request.OrganizationId == notification.OrganizationId
                select new
                {
                    notification.Id,
                    notification.ExpenseRequestId,
                    OrganizationName = organization.Name,
                    RecipientCode = employee.Code,
                    RecipientName = employee.Name,
                    RecipientIsActive = employee.IsActive,
                    notification.RecipientWasInitiator,
                    notification.RecipientWasResponsibleAccountant,
                    notification.CreatedAtUtc,
                    notification.FullyPaidAtUtc,
                    notification.PaymentBusinessDate,
                    notification.AsOfDate,
                    notification.AgeDays,
                    notification.RedFromDays,
                    notification.RemainingAmountKzt
                };
            query = query.TagWith("W604 administrative notification queue");
            var total = await query.CountAsync(token);
            var rows = await query.OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Id)
                .Skip(skip).Take(take).ToArrayAsync(token);
            await transaction.CommitAsync(token);
            return new AdministrativeAccrualNotificationPageDto(total, skip, take, rows.Select(row =>
                new AdministrativeAccrualNotificationDto(row.Id, row.ExpenseRequestId, row.OrganizationName,
                    row.RecipientCode, row.RecipientName, row.RecipientIsActive,
                    row.RecipientWasInitiator, row.RecipientWasResponsibleAccountant,
                    row.CreatedAtUtc, row.FullyPaidAtUtc, row.PaymentBusinessDate, row.AsOfDate,
                    row.AgeDays, row.RedFromDays, row.RemainingAmountKzt)).ToArray());
        }, cancellationToken);
    }

    public async Task<AccrualNotificationIssuePageDto> ReadIssuesAsync(
        int skip, int take, CancellationToken cancellationToken)
    {
        AccrualNotificationAdministrationService.RequireFinancialReader(currentActor);
        var actorExternalId = ActorIdentity.RequireBusinessDataAccess(currentActor);
        RequireCleanRead(skip, take);
        return await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, token);
            if (!await FinancialRoleCodes(actorExternalId).AnyAsync(token))
                throw new ActorForbiddenException();
            var query =
                from issue in dbContext.AccrualNotificationIssues.AsNoTracking()
                join request in dbContext.ExpenseRequests.AsNoTracking()
                    on issue.ExpenseRequestId equals request.Id
                join organization in dbContext.Organizations.AsNoTracking()
                    on request.OrganizationId equals organization.Id
                where issue.ResolvedAtUtc == null
                select new
                {
                    issue.ExpenseRequestId,
                    OrganizationName = organization.Name,
                    issue.Code,
                    issue.FirstDetectedAtUtc,
                    issue.LastDetectedAtUtc
                };
            query = query.TagWith("W604 administrative notification issues");
            var total = await query.CountAsync(token);
            var rows = await query.OrderByDescending(row => row.LastDetectedAtUtc)
                .ThenByDescending(row => row.ExpenseRequestId).ThenBy(row => row.Code)
                .Skip(skip).Take(take).ToArrayAsync(token);
            await transaction.CommitAsync(token);
            return new AccrualNotificationIssuePageDto(total, skip, take, rows.Select(row =>
                new AccrualNotificationIssueDto(row.ExpenseRequestId, row.OrganizationName,
                    row.Code, row.FirstDetectedAtUtc, row.LastDetectedAtUtc)).ToArray());
        }, cancellationToken);
    }

    private IQueryable<string> FinancialRoleCodes(string actorExternalId) =>
        BusinessDataReadScopeQuery.RoleCodes(dbContext,
            BusinessDataReadScopeQuery.EmployeeIds(dbContext, actorExternalId))
            .Where(code => code == ExpenseRequestWorkflowCodes.FinancialControllerRoleCode ||
                code == ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode ||
                code == ExpenseRequestWorkflowCodes.AuditorRoleCode)
            .TagWith("Notification register current financial authority");

    private void RequireCleanRead(int skip, int take)
    {
        if (skip < 0 || take is < 1 or > 100 || skip > int.MaxValue - take ||
            dbContext.Database.CurrentTransaction is not null || dbContext.ChangeTracker.HasChanges())
        {
            throw new AccrualNotificationReadUnavailableException();
        }
    }
}
