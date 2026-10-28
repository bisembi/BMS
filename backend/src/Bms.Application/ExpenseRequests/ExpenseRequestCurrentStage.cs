using Bms.Domain.Common;
using Bms.Domain.ExpenseRequests;
using Bms.Domain.Workflow;

namespace Bms.Application.ExpenseRequests;

/// <summary>
/// Safe list label for the currently active human workflow stage. The label is
/// derived only from the exact active immutable route and its persisted position;
/// participant identities and technical role codes never enter the DTO.
/// </summary>
internal static class ExpenseRequestCurrentStage
{
    internal const string FinancialController = "Финансовый контролёр";
    internal const string FinancialDirector = "Финансовый директор";
    internal const string GeneralDirector = "Генеральный директор";
    internal const string NsiSpecialist = "Специалист НСИ";
    internal const string Treasurer = "Казначей";
    internal const string BudgetOwner = "Владелец ЦФО";

    internal static string? Resolve(ExpenseRequest request, ApprovalRouteSnapshot? snapshot = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Status))
        {
            throw InvariantViolation();
        }

        if (request.Status == ExpenseRequestStatus.InApproval)
        {
            if (request.ActiveApprovalRouteSnapshotNumber is not { } snapshotNumber ||
                snapshotNumber <= 0 || snapshot is null || snapshot.ExpenseRequestId != request.Id
                || snapshot.SnapshotNumber != snapshotNumber || snapshot.OrganizationId != request.OrganizationId
                || snapshot.CostCenterId != request.DefaultCostCenterId)
            {
                throw InvariantViolation();
            }

            var stage = snapshot.Stages.SingleOrDefault(s => s.StageNumber == request.ActiveApprovalRouteStageNumber);
            return stage?.RoleCode switch
            {
                ExpenseRequestWorkflowCodes.BudgetOwnerRoleCode => BudgetOwner,
                ExpenseRequestWorkflowCodes.FinancialControllerRoleCode => FinancialController,
                ExpenseRequestWorkflowCodes.FinancialDirectorRoleCode => FinancialDirector,
                ExpenseRequestWorkflowCodes.GeneralDirectorRoleCode => GeneralDirector,
                ExpenseRequestWorkflowCodes.PayrollCalculatorRoleCode => "Расчетчик зарплаты",
                _ => throw InvariantViolation()
            };
        }

        if (request.ActiveApprovalRouteSnapshotNumber is not null ||
            request.ActiveApprovalRouteStageNumber != 0)
        {
            throw InvariantViolation();
        }

        return request.Status switch
        {
            ExpenseRequestStatus.AwaitingNsi => NsiSpecialist,
            ExpenseRequestStatus.Approved => Treasurer,
            _ => null
        };
    }

    private static DomainRuleViolationException InvariantViolation() => new(
        "Expense-request current workflow stage is inconsistent.");
}
