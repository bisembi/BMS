namespace Bms.Application.ExpenseRequests;

public sealed class SubmissionPreflightUnavailableException : Exception
{
    public SubmissionPreflightUnavailableException(string message)
        : base(message)
    {
    }

    public SubmissionPreflightUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
