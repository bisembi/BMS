using Bms.Application.Common;

namespace Bms.Application.Authorization;

public sealed record StaffSubstitutionInput471(Guid DeputyEmployeeId,string RoleCode,DateOnly From,DateOnly? Through);
public sealed record StaffSubstitutionSnapshot471(Guid Id,long Number,Guid PrincipalEmployeeId,Guid DeputyEmployeeId,
    string RoleCode,DateOnly From,DateOnly? Through,DateTimeOffset ValidFromUtc,DateTimeOffset? ValidBeforeUtc,
    string Status,int Version,DateTimeOffset CreatedAtUtc,DateTimeOffset UpdatedAtUtc,DateTimeOffset? AcceptedAtUtc);
public sealed record StaffSubstitutionDto471(Guid Id,string Number,Guid PrincipalEmployeeId,string PrincipalName,
    Guid DeputyEmployeeId,string DeputyName,string RoleCode,string RoleName,DateOnly From,DateOnly? Through,
    string Status,int Version,DateTimeOffset UpdatedAtUtc,IReadOnlyList<string> Commands);
public sealed record StaffEmployee471(Guid Id,string Name);
public sealed record StaffRole471(string Code,string Name);
public sealed record StaffOptions471(Guid EmployeeId,string EmployeeName,IReadOnlyList<StaffRole471> Roles,IReadOnlyList<StaffEmployee471> Employees);
public sealed record StaffSubstitutionPage471(IReadOnlyList<StaffSubstitutionDto471> Items,int Skip,int Take,int Total);
public sealed record StaffNews471(Guid Id,string Number,string PrincipalName,string DeputyName,string RoleName,
    DateOnly From,DateOnly? Through,string Status,DateTimeOffset AtUtc);
public sealed record StaffAction471(Guid Id,DateTimeOffset AtUtc,string ActorName,string PrincipalName,
    string RoleName,string ObjectType,string ObjectId,string Action);
public sealed record StaffRoleChoice471(Guid DocumentId,string RoleCode,string PrincipalName,DateOnly From,DateOnly? Through);

/// Immutable request-scoped evidence, never supplied by the client. Physical
/// actor remains the deputy; only the selected role's principal capacity changes.
public sealed record StaffActingRole471(Guid DocumentId,Guid PrincipalEmployeeId,Guid DeputyEmployeeId,
    string PrincipalActorExternalId,string DeputyActorExternalId,string PrincipalName,string RoleCode,
    DateTimeOffset ValidFromUtc,DateTimeOffset? ValidBeforeUtc)
{
    public bool Includes(DateTimeOffset now)=>now>=ValidFromUtc&&(ValidBeforeUtc is null||now<ValidBeforeUtc);
}
public interface IStaffSubstitutionStore471
{
    Task<StaffOptions471> OptionsAsync(string actor,CancellationToken token);
    Task<StaffSubstitutionPage471> ListAsync(string actor,int skip,int take,CancellationToken token);
    Task<StaffSubstitutionDto471?> GetAsync(string actor,Guid id,CancellationToken token);
    Task<StaffSubstitutionDto471> ExecuteAsync(string actor,Guid id,Guid commandId,int expectedVersion,string action,StaffSubstitutionInput471? input,CancellationToken token);
    Task<IReadOnlyList<StaffNews471>> NewsAsync(string actor,CancellationToken token);
    Task<IReadOnlyList<StaffActingRole471>> ActiveAsync(string actor,CancellationToken token);
    Task<StaffActingRole471?> ResolveActiveAsync(string actor,Guid documentId,string role,CancellationToken token);
    Task<IReadOnlyList<StaffSubstitutionDto471>> PendingAsync(string actor,CancellationToken token);
    Task<IReadOnlyList<StaffAction471>> ActionsAsync(string actor,Guid documentId,CancellationToken token);
}
public sealed class StaffSubstitutionConflict471:Exception {public StaffSubstitutionConflict471():base("Substitution state or authority changed."){} }
public sealed class StaffSubstitutionUnavailable471:Exception {public StaffSubstitutionUnavailable471(Exception? inner=null):base("Staff substitution is unavailable.",inner){} }

public static class StaffSubstitutionPolicy471
{
    public static void Validate(StaffSubstitutionInput471 input,Guid principal)
    {
        if(principal==Guid.Empty||input.DeputyEmployeeId==Guid.Empty||principal==input.DeputyEmployeeId
            ||!ActiveRoleCatalog.IsBusinessRole(input.RoleCode)||input.From.Year is <1900 or >9998
            ||input.Through is{} end&&(end<input.From||end.Year>9998))throw new ArgumentException("Invalid substitution fields.");
    }
    public static (DateTimeOffset From,DateTimeOffset? Before) Period(StaffSubstitutionInput471 input,TimeZoneInfo zone)
    {
        DateTimeOffset Utc(DateOnly d)=>new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue),DateTimeKind.Unspecified),zone));
        return(Utc(input.From),input.Through is{} end?Utc(end.AddDays(1)):null);
    }
    public static bool Active(StaffSubstitutionSnapshot471 doc,DateTimeOffset now)=>doc.Status=="CONFIRMED"
        &&now>=doc.ValidFromUtc&&(doc.ValidBeforeUtc is null||now<doc.ValidBeforeUtc);
    public static string Transition(StaffSubstitutionSnapshot471 doc,Guid employee,string action,DateTimeOffset now)
    {
        var author=employee==doc.PrincipalEmployeeId;var recipient=employee==doc.DeputyEmployeeId;
        if(!author&&!recipient)throw new ActorForbiddenException();
        return action switch
        {
            "update" when author&&doc.Status=="DRAFT"=>"DRAFT",
            "submit" when author&&doc.Status=="DRAFT"&&(doc.ValidBeforeUtc is null||now<doc.ValidBeforeUtc)=>"PENDING",
            "confirm" when recipient&&doc.Status=="PENDING"&&(doc.ValidBeforeUtc is null||now<doc.ValidBeforeUtc)=>"CONFIRMED",
            "reject" when recipient&&doc.Status=="PENDING"=>"REJECTED",
            "revoke" when doc.Status is "DRAFT" or "PENDING" or "CONFIRMED"=>"REVOKED",
            _=>throw new StaffSubstitutionConflict471()
        };
    }
    public static bool Overlaps(DateTimeOffset from,DateTimeOffset? before,DateTimeOffset otherFrom,DateTimeOffset? otherBefore)=>
        (before is null||otherFrom<before)&&(otherBefore is null||from<otherBefore);
}
