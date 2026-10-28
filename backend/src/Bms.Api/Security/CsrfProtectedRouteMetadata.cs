namespace Bms.Api.Security;

/// <summary>
/// Marks a route as one whose registration went through
/// <see cref="CsrfEndpointConventions.RequireCsrfToken"/>.
///
/// The marker exists so coverage is a measurement rather than a promise: a
/// test walks the live route table and fails when a mutating route carries no
/// marker. Adding the filter without the marker, or the marker without the
/// filter, is not possible through the public convention.
/// </summary>
public sealed class CsrfProtectedRouteMetadata
{
    /// <summary>The single instance attached to every protected route.</summary>
    public static CsrfProtectedRouteMetadata Instance { get; } = new();

    private CsrfProtectedRouteMetadata()
    {
    }
}
