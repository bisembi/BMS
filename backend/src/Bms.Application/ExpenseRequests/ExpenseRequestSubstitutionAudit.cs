using System.Text.Json;
using Bms.Application.Abstractions;
using Bms.Application.Authorization;

namespace Bms.Application.ExpenseRequests;

internal static class ExpenseRequestSubstitutionAudit
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static void Append(
        IBusinessAuditSink auditSink,
        Guid requestId,
        DateTimeOffset occurredAtUtc,
        ExpenseRequestActionAuthority authority,
        string action,
        string correlationId)
    {
        ArgumentNullException.ThrowIfNull(auditSink);
        ArgumentNullException.ThrowIfNull(authority);
        if (!authority.IsSubstitution)
        {
            return;
        }

        if (!authority.IsAuthorized
            || authority.SubstitutionId is not Guid substitutionId
            || substitutionId == Guid.Empty
            || authority.ValidFromUtc is not DateTimeOffset validFromUtc
            || authority.ValidThroughUtc is not DateTimeOffset validThroughUtc
            || string.IsNullOrWhiteSpace(authority.Reason))
        {
            throw new ExpenseRequestSubstitutionAuthorityUnavailableException();
        }

        var evidence = JsonSerializer.Serialize(
            new ExpenseRequestSubstitutionAuditEvidence(
                requestId,
                authority.InitiatorExternalId,
                authority.ActorExternalId,
                substitutionId,
                validFromUtc,
                validThroughUtc,
                authority.Reason),
            JsonOptions);
        auditSink.Append(
            Guid.NewGuid(),
            occurredAtUtc,
            authority.ActorExternalId,
            "ExpenseRequestSubstitution",
            requestId.ToString("D"),
            action,
            beforeJson: null,
            evidence,
            correlationId);
    }

    private sealed record ExpenseRequestSubstitutionAuditEvidence(
        Guid ExpenseRequestId,
        string InitiatorExternalId,
        string ActualActorExternalId,
        Guid SubstitutionId,
        DateTimeOffset ValidFromUtc,
        DateTimeOffset ValidThroughUtc,
        string Reason);
}
