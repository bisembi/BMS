using Bms.Application.Abstractions;
using Bms.Application.Security;
using Bms.Domain.Workflow;

namespace Bms.Application.Common;

/// <summary>
/// Read-only access to the change journal, session monitor and budget register.
/// Never use this policy for administration commands or the exchange journal.
/// ActiveRoleCode is the server-resolved, request-scoped assignment, not a client grant.
/// </summary>
public static class AdministrationReportReadAccess
{
    public static void Require(ICurrentActor actor)
    {
        ActorIdentity.RequireBusinessDataAccess(actor);
        if (actor.IsBmsIntegrationAdministrator)
            throw new ActorForbiddenException();
        if (actor.IsBmsAdministrator) return;
        if (string.Equals(actor.ActiveRoleCode,
                ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode, StringComparison.Ordinal)
            && string.Equals(actor.ActiveProfileCode, BmsAccessProfileCodes.Business, StringComparison.Ordinal)
            && actor.HasFullBusinessDataAccess) return;
        throw new ActorForbiddenException();
    }
}
