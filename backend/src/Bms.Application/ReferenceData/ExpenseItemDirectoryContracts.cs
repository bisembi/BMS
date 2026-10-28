using System.Text.Json.Serialization;

namespace Bms.Application.ReferenceData;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateExpenseItemCommand(
    string? Code,
    [property: JsonRequired] string Name);

public sealed class ExpenseItemCodeConflictException : Exception
{
    public ExpenseItemCodeConflictException()
        : base("The expense-item code is already assigned.")
    {
    }
}

public sealed class ExpenseItemDirectoryUnavailableException : Exception
{
    public ExpenseItemDirectoryUnavailableException()
        : base("The expense-item directory could not be resolved consistently.")
    {
    }

    public ExpenseItemDirectoryUnavailableException(Exception innerException)
        : base("The expense-item directory could not be resolved consistently.", innerException)
    {
    }
}
