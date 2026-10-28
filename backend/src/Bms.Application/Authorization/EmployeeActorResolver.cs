using Bms.Application.Abstractions;
using Bms.Application.Common;

namespace Bms.Application.Authorization;

/// <summary>
/// Resolves an already-authenticated, canonical SSO actor to the one active
/// imported employee. An absent or inactive mapping is ordinary no-authority;
/// contradictory projection data fails closed.
/// </summary>
public sealed class EmployeeActorResolver(IEmployeeActorMappingReader reader,IActiveRoleSelection? selection=null,IStaffSubstitutionStore471? substitutions=null)
{
    public async Task<EmployeeActorResolution> ResolveAsync(
        string actorExternalId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!ActorIdentity.IsCanonical(actorExternalId))
            {
                throw new EmployeeActorResolutionUnavailableException();
            }

            cancellationToken.ThrowIfCancellationRequested();
            var rows = await reader.ReadAsync(actorExternalId, cancellationToken);
            if (rows is null || rows.Any(row => row is null) || rows.Count > 1)
            {
                throw new EmployeeActorResolutionUnavailableException();
            }

            if (rows.Count == 0)
            {
                return EmployeeActorResolution.Unmapped;
            }

            var row = rows[0];
            if (row.EmployeeId == Guid.Empty
                || !ActorIdentity.IsCanonical(row.ActorExternalId)
                || !string.Equals(
                    row.ActorExternalId,
                    actorExternalId,
                    StringComparison.Ordinal))
            {
                throw new EmployeeActorResolutionUnavailableException();
            }

            if (!row.MappingIsActive)
            {
                return EmployeeActorResolution.Unmapped;
            }

            if (!row.EmployeeIsActive)
            {
                throw new EmployeeActorResolutionUnavailableException();
            }

            if(selection?.Substitution471 is{} delegated&&actorExternalId==delegated.DeputyActorExternalId)
            {
                if(row.EmployeeId!=delegated.DeputyEmployeeId||substitutions is null)throw new EmployeeActorResolutionUnavailableException();
                var live=await substitutions.ResolveActiveAsync(actorExternalId,delegated.DocumentId,delegated.RoleCode,cancellationToken);
                if(live!=delegated)throw new EmployeeActorResolutionUnavailableException();
                return new EmployeeActorResolution(delegated.PrincipalEmployeeId);
            }
            return new EmployeeActorResolution(row.EmployeeId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (EmployeeActorResolutionUnavailableException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new EmployeeActorResolutionUnavailableException(exception);
        }
    }
}

public sealed record EmployeeActorMappingProjectionRow(
    string ActorExternalId,
    Guid EmployeeId,
    bool MappingIsActive,
    bool EmployeeIsActive,
    string? EmployeeName = null);

public sealed record EmployeeActorResolution(Guid? EmployeeId)
{
    public static EmployeeActorResolution Unmapped { get; } = new((Guid?)null);

    public bool HasActiveEmployee => EmployeeId.HasValue;
}

public sealed class EmployeeActorResolutionUnavailableException : Exception
{
    public EmployeeActorResolutionUnavailableException()
        : base("The employee actor mapping could not be resolved consistently.")
    {
    }

    public EmployeeActorResolutionUnavailableException(Exception innerException)
        : base("The employee actor mapping could not be resolved consistently.", innerException)
    {
    }
}
