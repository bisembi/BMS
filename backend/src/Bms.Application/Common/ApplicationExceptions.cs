namespace Bms.Application.Common;

public sealed class ActorNotAuthenticatedException()
    : Exception("An authenticated actor is required.");

public sealed class ActorForbiddenException()
    : Exception("The actor is not allowed to execute this business command.");

public sealed class IdempotencyConflictException()
    : Exception("The idempotency key was already used with a different request.");

public sealed class InvalidIdempotencyKeyException(string message) : Exception(message);

public sealed class ExpenseRequestNotFoundException()
    : Exception("The expense request was not found.");

public sealed class ExpenseRequestVersionConflictException : Exception
{
    public ExpenseRequestVersionConflictException()
        : base("The expense request was changed by another operation. Reload it and try again.")
    {
    }

    public ExpenseRequestVersionConflictException(Exception innerException)
        : base("The expense request was changed by another operation. Reload it and try again.", innerException)
    {
    }
}

public sealed class BudgetProjectionDriftException(string message) : Exception(message);
