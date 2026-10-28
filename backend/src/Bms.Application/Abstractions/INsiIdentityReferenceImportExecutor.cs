using System.Collections.ObjectModel;
using Bms.Application.ReferenceData;

namespace Bms.Application.Abstractions;

public interface INsiIdentityReferenceImportExecutor
{
    Task<NsiIdentityReferenceImportResult> ExecuteAsync(
        NsiIdentityReferenceImportBatch batch,
        CancellationToken cancellationToken);
}

public sealed class NsiIdentityReferenceImportResult
{
    private readonly IReadOnlyDictionary<string, int> affectedRows;

    private NsiIdentityReferenceImportResult(
        bool isApplied,
        string? issueCode,
        IEnumerable<KeyValuePair<string, int>> affectedRows)
    {
        IsApplied = isApplied;
        IssueCode = issueCode;
        var values = affectedRows.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        this.affectedRows = new ReadOnlyDictionary<string, int>(values);
    }

    public bool IsApplied { get; }

    public string? IssueCode { get; }

    public IReadOnlyDictionary<string, int> AffectedRows => affectedRows;

    public long TotalAffectedRows => affectedRows.Values.Sum(value => (long)value);

    internal static NsiIdentityReferenceImportResult Applied(
        IReadOnlyDictionary<string, int> affectedRows)
    {
        ArgumentNullException.ThrowIfNull(affectedRows);
        if (affectedRows.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0))
        {
            throw new ArgumentException(
                "Import result phases and counts must be valid.",
                nameof(affectedRows));
        }

        return new NsiIdentityReferenceImportResult(true, null, affectedRows);
    }

    internal static NsiIdentityReferenceImportResult Blocked(string issueCode)
    {
        if (string.IsNullOrWhiteSpace(issueCode) ||
            !NsiIdentityReferenceImportIssueCodes.IsKnown(issueCode))
        {
            throw new ArgumentException(
                "A known identity import issue code is required.",
                nameof(issueCode));
        }

        return new NsiIdentityReferenceImportResult(false, issueCode, []);
    }
}

public sealed class NsiIdentityReferenceImportUnavailableException : Exception
{
    private const string SafeMessage =
        "The identity reference import could not be completed consistently.";

    internal NsiIdentityReferenceImportUnavailableException()
        : base(SafeMessage)
    {
    }
}

public sealed class NsiIdentityReferenceImportCommittedException : Exception
{
    private const string SafeMessage =
        "The identity reference import committed, but post-commit cleanup did not complete.";

    internal NsiIdentityReferenceImportCommittedException()
        : base(SafeMessage)
    {
    }
}
