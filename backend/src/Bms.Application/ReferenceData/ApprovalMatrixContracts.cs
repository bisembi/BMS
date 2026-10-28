namespace Bms.Application.ReferenceData;

/// <summary>
/// Informational view of the verified approval/NSI/payment-handoff registry.
/// It is not command discovery for a document and grants no execution right.
/// Payment intake and the separate closing contract are outside this matrix.
/// </summary>
public sealed record ApprovalMatrixDto(
    int DefinitionVersion,
    decimal GeneralDirectorThresholdKzt,
    IReadOnlyList<ApprovalMatrixStatusDto> Statuses,
    IReadOnlyList<ApprovalMatrixRouteDto> Routes,
    IReadOnlyList<ApprovalMatrixTransitionDto> Transitions);

public sealed record ApprovalMatrixStatusDto(string Code, string Name);

public sealed record ApprovalMatrixRouteDto(
    string DeficitBand,
    IReadOnlyList<ApprovalMatrixStageDto> Stages);

public sealed record ApprovalMatrixStageDto(int Number, string RoleCode);

public sealed record ApprovalMatrixTransitionDto(
    string StatusCode,
    int StageNumber,
    string RoleCode,
    string CommandCode,
    string TargetStatusCode,
    int TargetStageNumber,
    string ConditionCode,
    bool RequiresReason);

public sealed class ApprovalMatrixUnavailableException()
    : Exception("The approval matrix is temporarily unavailable.");
