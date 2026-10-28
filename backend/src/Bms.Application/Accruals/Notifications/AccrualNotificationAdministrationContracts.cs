namespace Bms.Application.Accruals.Notifications;

/// <summary>
/// The financial/date/role fields are immutable event data. The recipient's
/// code, name and active state are the current catalogue values, not snapshots.
/// Neither the cycle nor the employee/actor identity is published.
/// </summary>
public sealed record AdministrativeAccrualNotificationDto(
    Guid Id,
    Guid ExpenseRequestId,
    string OrganizationName,
    string RecipientCode,
    string RecipientName,
    bool RecipientIsActive,
    bool RecipientWasInitiator,
    bool RecipientWasResponsibleAccountant,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset FullyPaidAtUtc,
    DateOnly PaymentBusinessDate,
    DateOnly AsOfDate,
    int AgeDays,
    int RedFromDays,
    decimal RemainingAmountKzt);

public sealed record AdministrativeAccrualNotificationPageDto(
    int Total, int Skip, int Take, IReadOnlyList<AdministrativeAccrualNotificationDto> Items);

/// <summary>Only an unresolved configuration issue; never a failed delivery.</summary>
public sealed record AccrualNotificationIssueDto(
    Guid ExpenseRequestId,
    string OrganizationName,
    string Code,
    DateTimeOffset FirstDetectedAtUtc,
    DateTimeOffset LastDetectedAtUtc);

public sealed record AccrualNotificationIssuePageDto(
    int Total, int Skip, int Take, IReadOnlyList<AccrualNotificationIssueDto> Items);

public interface IAccrualNotificationAdministrationReader
{
    Task<AdministrativeAccrualNotificationPageDto> ReadQueueAsync(
        int skip, int take, CancellationToken cancellationToken);

    Task<AccrualNotificationIssuePageDto> ReadIssuesAsync(
        int skip, int take, CancellationToken cancellationToken);
}
