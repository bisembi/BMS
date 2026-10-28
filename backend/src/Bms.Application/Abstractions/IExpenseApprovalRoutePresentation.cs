using Bms.Domain.ExpenseRequests;

namespace Bms.Application.Abstractions;

public sealed record ExpenseApprovalStagePresentation(int StageNumber, string RoleCode, IReadOnlyList<string> Names);
public sealed record ExpenseApprovalRoutePresentationDto(Guid RequestId, Guid RequestVersion, string StatusCode,
    bool PreviewOnly, bool Available, int CurrentStageNumber, IReadOnlyList<ExpenseApprovalStagePresentation> Stages,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? TreasurerNames = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? UnavailableReason = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? NsiSpecialistNames = null);

// Presentation only. Must never authorize submission or modify captured routes.
public interface IExpenseApprovalRoutePresentation
{
    Task<ExpenseApprovalRoutePresentationDto> BuildDraftAsync(ExpenseRequest draft, decimal deficit, CancellationToken token);
    Task<ExpenseApprovalRoutePresentationDto?> ReadAsync(Guid requestId, CancellationToken token);
}
