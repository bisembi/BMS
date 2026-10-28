namespace Bms.Application.Abstractions;

public interface IBusinessAuditReadAuthority
{
    Task<bool> CanReadAsync(
        string actorExternalId,
        CancellationToken cancellationToken);
}

public sealed class BusinessAuditReadAuthorityUnavailableException : Exception
{
    private const string SafeMessage =
        "The business-audit read authority could not be resolved consistently.";

    public BusinessAuditReadAuthorityUnavailableException()
        : base(SafeMessage)
    {
    }

    public BusinessAuditReadAuthorityUnavailableException(Exception innerException)
        : base(
            SafeMessage,
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }
}
