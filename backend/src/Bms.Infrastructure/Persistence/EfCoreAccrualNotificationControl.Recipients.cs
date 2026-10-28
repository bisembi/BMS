using Bms.Application.Accruals.Notifications;
using Bms.Domain.Workflow;
using Microsoft.EntityFrameworkCore;

namespace Bms.Infrastructure.Persistence;

internal sealed partial class EfCoreAccrualNotificationControl
{
    private sealed record Recipient(Guid EmployeeId, bool IsInitiator, bool IsResponsibleAccountant);

    private async Task<IReadOnlyList<Recipient>> ReadRecipientsAsync(
        AccrualRedZoneCandidate candidate, CancellationToken cancellationToken)
    {
        var accountants = await (
            from assignment in dbContext.EmployeeFunctionalRoles.AsNoTracking()
            join employee in dbContext.Employees.AsNoTracking() on assignment.EmployeeId equals employee.Id
            join role in dbContext.FunctionalRoles.AsNoTracking() on assignment.FunctionalRoleId equals role.Id
            where assignment.IsActive && employee.IsActive && role.IsActive &&
                role.Code == ExpenseRequestWorkflowCodes.ResponsibleAccountantRoleCode &&
                dbContext.Organizations.Any(organization =>
                    organization.Id == candidate.OrganizationId && organization.IsActive) &&
                dbContext.EmployeeActorMappings.Any(mapping =>
                    mapping.EmployeeId == employee.Id && mapping.IsActive &&
                    dbContext.ActorOrganizationGrants.Any(grant =>
                        grant.ActorExternalId == mapping.ActorExternalId &&
                        grant.OrganizationId == candidate.OrganizationId && grant.IsActive))
            select employee.Id).Distinct().ToArrayAsync(cancellationToken);
        var initiator = await (
            from mapping in dbContext.EmployeeActorMappings.AsNoTracking()
            join employee in dbContext.Employees.AsNoTracking() on mapping.EmployeeId equals employee.Id
            where mapping.ActorExternalId == candidate.InitiatorExternalId &&
                mapping.IsActive && employee.IsActive
            select (Guid?)employee.Id).SingleOrDefaultAsync(cancellationToken);

        var recipients = accountants.ToDictionary(id => id,
            id => new Recipient(id, IsInitiator: false, IsResponsibleAccountant: true));
        if (initiator is { } initiatorId)
        {
            recipients[initiatorId] = new Recipient(initiatorId, IsInitiator: true,
                IsResponsibleAccountant: recipients.ContainsKey(initiatorId));
        }
        return recipients.Values.ToArray();
    }

    private void RecordIssue(AccrualRedZoneCandidate candidate, string code,
        List<AccrualNotificationIssue> issues, string actor, AccrualNotificationTrigger trigger,
        DateTimeOffset atUtc)
    {
        var issue = issues.SingleOrDefault(item => item.Code == code);
        if (issue is not null && atUtc < issue.LastDetectedAtUtc)
        {
            throw new AccrualNotificationControlUnavailableException();
        }
        var newOccurrence = issue is null || issue.ResolvedAtUtc is not null ||
            issue.AccrualCycleId != candidate.CycleId;
        if (issue is null)
        {
            issue = new AccrualNotificationIssue
            {
                ExpenseRequestId = candidate.ExpenseRequestId,
                Code = code
            };
            dbContext.AccrualNotificationIssues.Add(issue);
            issues.Add(issue);
        }
        issue.AccrualCycleId = candidate.CycleId;
        issue.LastDetectedAtUtc = atUtc;
        if (!newOccurrence)
        {
            return;
        }
        issue.FirstDetectedAtUtc = atUtc;
        issue.ResolvedAtUtc = null;
        RecordEvidence(candidate.ExpenseRequestId, actor, atUtc,
            "AccrualNotificationRecipientConfigurationError",
            "accrual-notification.configuration-error.v1",
            new
            {
                trigger = trigger.ToString(),
                result = "recipientConfigurationError",
                code,
                candidate.ExpenseRequestId,
                candidate.CycleId,
                candidate.AsOfDate,
                candidate.AgeDays
            });
    }

    private void ResolveIssue(AccrualNotificationIssue issue, string actor,
        AccrualNotificationTrigger trigger, DateTimeOffset atUtc)
    {
        if (issue.ResolvedAtUtc is not null)
        {
            return;
        }
        if (atUtc < issue.LastDetectedAtUtc)
        {
            throw new AccrualNotificationControlUnavailableException();
        }
        issue.LastDetectedAtUtc = atUtc;
        issue.ResolvedAtUtc = atUtc;
        RecordEvidence(issue.ExpenseRequestId, actor, atUtc,
            "AccrualNotificationRecipientConfigurationResolved",
            "accrual-notification.configuration-resolved.v1",
            new
            {
                trigger = trigger.ToString(),
                result = "configurationResolved",
                issue.ExpenseRequestId,
                issue.AccrualCycleId,
                issue.Code
            });
    }
}
