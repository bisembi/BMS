using Bms.Domain.Common;

namespace Bms.Domain.Budgeting;

/// <summary>
/// Budget instance document with editable draft and immutable approved states.
/// The aggregate records an already authorized approval; it neither allocates
/// CFO approval ordinals nor writes Limit/Reserve/Paid projections.
/// </summary>
public sealed class BudgetInstance
{
    private readonly List<BudgetInstanceLine> _lines = [];

    private BudgetInstance()
    {
    }

    private BudgetInstance(
        Guid id,
        string number,
        Guid costCenterId,
        DateOnly validFrom,
        DateOnly validTo,
        Guid ownerEmployeeId,
        string ownerExternalId,
        Guid concurrencyStamp)
    {
        Id = id;
        Number = number;
        CostCenterId = costCenterId;
        ValidFrom = validFrom;
        ValidTo = validTo;
        Status = BudgetInstanceStatus.Draft;
        OwnerEmployeeId = ownerEmployeeId;
        OwnerExternalId = ownerExternalId;
        ConcurrencyStamp = concurrencyStamp;
    }

    public Guid Id { get; private set; }

    public string Number { get; private set; } = string.Empty;

    public Guid CostCenterId { get; private set; }

    public DateOnly ValidFrom { get; private set; }

    public DateOnly ValidTo { get; private set; }

    public BudgetInstanceStatus Status { get; private set; }

    public Guid OwnerEmployeeId { get; private set; }

    public string OwnerExternalId { get; private set; } = string.Empty;

    public Guid ConcurrencyStamp { get; private set; }

    public long? ApprovalOrdinal { get; private set; }

    public DateTimeOffset? ApprovedAtUtc { get; private set; }

    public Guid? ApprovedByEmployeeId { get; private set; }

    public string? ApprovedByExternalId { get; private set; }

    public IReadOnlyList<BudgetInstanceLine> Lines => _lines.AsReadOnly();

    public static BudgetInstance CreateDraft(
        string number,
        Guid costCenterId,
        DateOnly validFrom,
        DateOnly validTo,
        Guid ownerEmployeeId,
        string ownerExternalId,
        IReadOnlyList<BudgetInstanceLineDraft> lines)
    {
        if (costCenterId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Cost centre is required for a budget instance.");
        }

        EnsureValidityPeriod(validFrom, validTo);

        if (ownerEmployeeId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Owner employee is required.");
        }

        if (string.IsNullOrWhiteSpace(ownerExternalId))
        {
            throw new DomainRuleViolationException("Owner external id is required.");
        }

        // The application layer allocates this value transactionally through the
        // budget directory. The aggregate accepts the allocator's value and
        // validates its shape; it never authorizes public manual numbering.
        var normalizedNumber = NormalizeNumber(number);
        var id = Guid.NewGuid();
        var instance = new BudgetInstance(
            id,
            normalizedNumber,
            costCenterId,
            validFrom,
            validTo,
            ownerEmployeeId,
            ownerExternalId.Trim(),
            Guid.NewGuid());
        instance.ReplaceLines(lines);
        return instance;
    }

    public static string NormalizeNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            throw new DomainRuleViolationException("Budget instance number is required.");
        }

        var trimmed = number.Trim();
        if (trimmed.Length is 0 or > 40)
        {
            throw new DomainRuleViolationException("Budget instance number length is invalid.");
        }

        if (!string.Equals(number, trimmed, StringComparison.Ordinal))
        {
            throw new DomainRuleViolationException("Budget instance number cannot have leading or trailing spaces.");
        }

        if (trimmed.Any(character => char.IsControl(character)))
        {
            throw new DomainRuleViolationException("Budget instance number cannot contain control characters.");
        }

        return trimmed;
    }

    public void ReplaceDraftLines(
        Guid expectedConcurrencyStamp,
        IReadOnlyList<BudgetInstanceLineDraft> lines)
        => ReplaceDraft(expectedConcurrencyStamp, ValidFrom, ValidTo, lines);

    public void ReplaceDraft(
        Guid expectedConcurrencyStamp,
        DateOnly validFrom,
        DateOnly validTo,
        IReadOnlyList<BudgetInstanceLineDraft> lines)
    {
        EnsureDraftEditable(expectedConcurrencyStamp);
        EnsureValidityPeriod(validFrom, validTo);
        var replacement = BuildLines(lines, validFrom, validTo);
        // Both header and lines change only after the entire replacement is valid.
        ValidFrom = validFrom;
        ValidTo = validTo;
        _lines.Clear();
        _lines.AddRange(replacement);
        ConcurrencyStamp = Guid.NewGuid();
    }

    public static void EnsureValidityPeriod(DateOnly validFrom, DateOnly validTo)
    {
        if (validFrom.Day != 1
            || validTo.Day != DateTime.DaysInMonth(validTo.Year, validTo.Month)
            || validFrom > validTo)
        {
            throw new DomainRuleViolationException("Budget instance validity must be an inclusive monthly interval.",
                DomainRuleViolationReason.BudgetPeriodInvalid);
        }
    }

    /// <summary>
    /// Records one approval of the exact draft version. The application must
    /// authorize the approver, resolve an idempotent replay before this call,
    /// and allocate the ordinal under the transaction's CostCenter lock.
    /// This method is deliberately not an idempotency store or allocator.
    /// </summary>
    public void Approve(
        Guid expectedConcurrencyStamp,
        long approvalOrdinal,
        DateTimeOffset approvedAtUtc,
        Guid approvedByEmployeeId,
        string approvedByExternalId)
    {
        // Check the version before status: retrying an already changed version
        // remains a version conflict, not a second approval with a new ordinal.
        if (expectedConcurrencyStamp == Guid.Empty || expectedConcurrencyStamp != ConcurrencyStamp)
        {
            throw new BudgetInstanceConcurrencyConflictException();
        }

        if (Status != BudgetInstanceStatus.Draft)
        {
            throw new DomainRuleViolationException("Only a draft budget instance may be approved.");
        }

        if (ApprovalOrdinal.HasValue || ApprovedAtUtc.HasValue
            || ApprovedByEmployeeId.HasValue || ApprovedByExternalId is not null)
        {
            throw new DomainRuleViolationException("A draft budget instance cannot carry approval metadata.");
        }

        if (approvalOrdinal <= 0)
        {
            throw new DomainRuleViolationException("Budget approval ordinal must be positive.");
        }

        if (approvedAtUtc == default || approvedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new DomainRuleViolationException("Budget approval time must be a nonempty UTC timestamp.");
        }

        if (approvedByEmployeeId == Guid.Empty)
        {
            throw new DomainRuleViolationException("Approving employee is required.");
        }

        // Keep this persisted identity canonical, without depending on the
        // Application layer. This validates shape, not employee/role authority.
        if (string.IsNullOrWhiteSpace(approvedByExternalId)
            || approvedByExternalId.Length > 256
            || !string.Equals(approvedByExternalId, approvedByExternalId.Trim(), StringComparison.Ordinal)
            || approvedByExternalId.Any(char.IsControl))
        {
            throw new DomainRuleViolationException("Approving actor external id must be canonical.");
        }

        // No assignments precede successful validation. Preserve the document
        // number, creator, CFO, inclusive period and every existing line identity.
        var nextConcurrencyStamp = Guid.NewGuid();
        ApprovalOrdinal = approvalOrdinal;
        ApprovedAtUtc = approvedAtUtc;
        ApprovedByEmployeeId = approvedByEmployeeId;
        ApprovedByExternalId = approvedByExternalId;
        Status = BudgetInstanceStatus.Approved;
        ConcurrencyStamp = nextConcurrencyStamp;
    }

    public void EnsureDraftCanBeDeleted(Guid expectedConcurrencyStamp) =>
        EnsureDraftEditable(expectedConcurrencyStamp);

    private void EnsureDraftEditable(Guid expectedConcurrencyStamp)
    {
        // A user who loaded a Draft before approval still receives the existing
        // stale-version conflict, not a misleading refusal of the new version.
        if (expectedConcurrencyStamp == Guid.Empty || expectedConcurrencyStamp != ConcurrencyStamp)
        {
            throw new BudgetInstanceConcurrencyConflictException();
        }

        if (Status != BudgetInstanceStatus.Draft)
        {
            throw new DomainRuleViolationException("Only a draft budget instance may be edited.");
        }
    }

    private void ReplaceLines(IReadOnlyList<BudgetInstanceLineDraft> lines)
    {
        _lines.AddRange(BuildLines(lines, ValidFrom, ValidTo));
    }

    /// Read-only copy for reports/limits. Never attach this projection for saving.
    public BudgetInstance ProjectApprovedCells(IReadOnlyCollection<BudgetAdjustmentCell> cells)
    {
        if (Status != BudgetInstanceStatus.Approved) throw new InvalidOperationException("Only approved budgets have adjustment projections.");
        var copy = new BudgetInstance(Id, Number, CostCenterId, ValidFrom, ValidTo, OwnerEmployeeId, OwnerExternalId, ConcurrencyStamp)
        {
            Status = Status, ApprovalOrdinal = ApprovalOrdinal, ApprovedAtUtc = ApprovedAtUtc,
            ApprovedByEmployeeId = ApprovedByEmployeeId, ApprovedByExternalId = ApprovedByExternalId
        };
        copy.ReplaceLines(cells.Select(c => new BudgetInstanceLineDraft(c.ArticleId, c.Month, c.AmountKzt,
            FlowDirection: c.Direction)).ToArray());
        return copy;
    }

    private List<BudgetInstanceLine> BuildLines(
        IReadOnlyList<BudgetInstanceLineDraft> lines,
        DateOnly validFrom,
        DateOnly validTo)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new DomainRuleViolationException("A budget instance must contain at least one line.",
                DomainRuleViolationReason.BudgetLinesInvalid);
        }

        var replacement = new List<BudgetInstanceLine>(lines.Count);
        for (var index = 0; index < lines.Count; index++)
        {
            var draft = lines[index]
                ?? throw new DomainRuleViolationException("A budget line draft cannot be null.",
                    DomainRuleViolationReason.BudgetLinesInvalid, index);
            var month = draft.BudgetMonth;
            if (month.Day != 1 || month < validFrom || month > validTo)
            {
                throw new DomainRuleViolationException(
                    "Every budget line must start a month within the instance period.",
                    DomainRuleViolationReason.BudgetLinesInvalid, index);
            }

            replacement.Add(BudgetInstanceLine.Create(
                Id,
                draft.CashFlowItemId,
                month,
                draft.LimitAmountKzt,
                draft.FlowDirection,
                index,
                draft.Comment));
        }

        foreach (var cell in replacement.GroupBy(line => (line.CashFlowItemId, line.BudgetMonth, line.FlowDirection)))
            MoneyAmountPolicy.EnsureRepresentable(cell.Sum(line => line.LimitAmountKzt), "Budget cell amount");

        // A rejected input must leave this aggregate intact, including its
        // existing line identities, even before a transaction can roll back.
        return replacement;
    }
}

public sealed record BudgetInstanceLineDraft(
    Guid CashFlowItemId,
    DateOnly BudgetMonth,
    decimal LimitAmountKzt,
    string? Comment = null,
    BudgetFlowDirection FlowDirection = BudgetFlowDirection.Payment);

public sealed class BudgetInstanceConcurrencyConflictException : Exception
{
    public BudgetInstanceConcurrencyConflictException()
        : base("The budget instance was changed by another request.")
    {
    }
}

public sealed class BudgetInstanceNotFoundException : Exception
{
    public BudgetInstanceNotFoundException()
        : base("The budget instance was not found.")
    {
    }
}

/// <summary>
/// Reserved compatibility exception for code paths that encounter an
/// uninitialized budget directory. Number allocation itself is implemented by
/// the application/infrastructure directory store; callers must not invent
/// GUID prefixes or accept free-form numbers.
/// </summary>
public sealed class BudgetInstanceNumberingUnresolvedException : Exception
{
    public BudgetInstanceNumberingUnresolvedException()
        : base("Budget instance numbering is not implemented; draft creation is not available yet.")
    {
    }
}
