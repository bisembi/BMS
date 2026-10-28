using Bms.Domain.Common;

namespace Bms.Domain.Overpayments;

public enum ManualOverpaymentDeficitCorrectionState
{
    PendingApproval = 1,
    ApprovedAndApplied = 2,
    Rejected = 3,
    Annulled = 4
}

public sealed record ManualOverpaymentDeficitCorrectionProposal(
    Guid CorrectionId,
    Guid DeficitId,
    OverpaymentScope Scope,
    decimal DeficitAmountAtCreation,
    decimal DeficitAmountKztAtCreation,
    decimal CorrectionAmount,
    decimal CorrectionAmountKzt,
    string Basis,
    Guid ResponsibleAccountantId,
    ManualOverpaymentDeficitCorrectionState State);

public sealed record ManualOverpaymentDeficitCorrectionDecision(
    Guid CorrectionId,
    Guid DeficitId,
    ManualOverpaymentDeficitCorrectionState State,
    Guid FinancialDirectorId,
    decimal AppliedAmount,
    decimal AppliedAmountKzt,
    decimal? DeficitAmountBefore,
    decimal? DeficitAmountKztBefore,
    decimal? DeficitAmountAfter,
    decimal? DeficitAmountKztAfter,
    bool CreatesSettlement,
    string? DecisionComment,
    string? RejectionReason);

/// <summary>
/// Plans the addressed manual-correction lifecycle without persistence, role
/// evaluation, command identity, timestamps or audit. Approval never silently
/// lowers the agreed pair: a proposal made stale by real settlement is annulled.
/// </summary>
public static class ManualOverpaymentDeficitCorrectionPlanner
{
    private const int MaximumTextLength = 1000;

    public static ManualOverpaymentDeficitCorrectionProposal CreateProposal(
        Guid correctionId,
        OverpaymentDeficitSnapshot deficit,
        decimal correctionAmount,
        string basis,
        Guid responsibleAccountantId,
        bool hasPendingCorrection)
    {
        if (correctionId == Guid.Empty || responsibleAccountantId == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                "Manual deficit correction requires correction and responsible accountant ids.");
        }
        ArgumentNullException.ThrowIfNull(deficit);
        ValidateDeficit(deficit);
        if (deficit.CurrentAmount == 0m)
        {
            throw new DomainRuleViolationException(
                "A closed overpayment deficit cannot receive a manual correction.");
        }
        if (hasPendingCorrection)
        {
            throw new DomainRuleViolationException(
                "Only one pending manual correction is allowed for an overpayment deficit.");
        }
        MoneyAmountPolicy.EnsurePositive(correctionAmount, "Manual correction amount");
        if (correctionAmount > deficit.CurrentAmount)
        {
            throw new DomainRuleViolationException(
                "Manual correction amount cannot exceed the current deficit balance.");
        }
        ValidateRequiredText(basis, "Manual correction basis");

        var correctionAmountKzt = PairedMoneyAllocationPolicy.Allocate(
            deficit.CurrentAmountKzt,
            correctionAmount,
            deficit.CurrentAmount);
        if (correctionAmountKzt == 0m)
        {
            throw new DomainRuleViolationException(
                "Manual correction cannot apply a positive currency amount with zero KZT.");
        }
        if (!IsPairedState(
                deficit.CurrentAmount - correctionAmount,
                deficit.CurrentAmountKzt - correctionAmountKzt))
        {
            throw new DomainRuleViolationException(
                "Manual correction proposal cannot leave an unpaired deficit balance.");
        }

        return new ManualOverpaymentDeficitCorrectionProposal(
            correctionId,
            deficit.DeficitId,
            new OverpaymentScope(
                deficit.OrganizationId,
                deficit.CounterpartyId,
                deficit.CurrencyCode),
            deficit.CurrentAmount,
            deficit.CurrentAmountKzt,
            correctionAmount,
            correctionAmountKzt,
            basis,
            responsibleAccountantId,
            ManualOverpaymentDeficitCorrectionState.PendingApproval);
    }

    public static ManualOverpaymentDeficitCorrectionDecision Approve(
        ManualOverpaymentDeficitCorrectionProposal proposal,
        OverpaymentDeficitSnapshot currentDeficit,
        Guid financialDirectorId,
        string? decisionComment = null)
    {
        ValidateProposal(proposal);
        ArgumentNullException.ThrowIfNull(currentDeficit);
        ValidateDeficit(currentDeficit);
        ValidateDecisionActorAndComment(financialDirectorId, decisionComment);
        if (currentDeficit.DeficitId != proposal.DeficitId
            || !SameScope(currentDeficit, proposal.Scope))
        {
            throw new DomainRuleViolationException(
                "Manual correction approval requires the exact proposal deficit and scope.");
        }

        var stale = currentDeficit.CurrentAmount < proposal.CorrectionAmount
            || currentDeficit.CurrentAmountKzt < proposal.CorrectionAmountKzt;
        var amountAfter = currentDeficit.CurrentAmount;
        var amountKztAfter = currentDeficit.CurrentAmountKzt;
        if (!stale)
        {
            amountAfter -= proposal.CorrectionAmount;
            amountKztAfter -= proposal.CorrectionAmountKzt;
            stale = !IsPairedState(amountAfter, amountKztAfter);
        }

        if (stale)
        {
            return new ManualOverpaymentDeficitCorrectionDecision(
                proposal.CorrectionId,
                proposal.DeficitId,
                ManualOverpaymentDeficitCorrectionState.Annulled,
                financialDirectorId,
                0m,
                0m,
                currentDeficit.CurrentAmount,
                currentDeficit.CurrentAmountKzt,
                currentDeficit.CurrentAmount,
                currentDeficit.CurrentAmountKzt,
                false,
                decisionComment,
                null);
        }

        return new ManualOverpaymentDeficitCorrectionDecision(
            proposal.CorrectionId,
            proposal.DeficitId,
            ManualOverpaymentDeficitCorrectionState.ApprovedAndApplied,
            financialDirectorId,
            proposal.CorrectionAmount,
            proposal.CorrectionAmountKzt,
            currentDeficit.CurrentAmount,
            currentDeficit.CurrentAmountKzt,
            amountAfter,
            amountKztAfter,
            true,
            decisionComment,
            null);
    }

    public static ManualOverpaymentDeficitCorrectionDecision Reject(
        ManualOverpaymentDeficitCorrectionProposal proposal,
        Guid financialDirectorId,
        string rejectionReason,
        string? decisionComment = null)
    {
        ValidateProposal(proposal);
        ValidateDecisionActorAndComment(financialDirectorId, decisionComment);
        ValidateRequiredText(rejectionReason, "Manual correction rejection reason");

        return new ManualOverpaymentDeficitCorrectionDecision(
            proposal.CorrectionId,
            proposal.DeficitId,
            ManualOverpaymentDeficitCorrectionState.Rejected,
            financialDirectorId,
            0m,
            0m,
            null,
            null,
            null,
            null,
            false,
            decisionComment,
            rejectionReason);
    }

    private static void ValidateProposal(ManualOverpaymentDeficitCorrectionProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (proposal.CorrectionId == Guid.Empty
            || proposal.DeficitId == Guid.Empty
            || proposal.ResponsibleAccountantId == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                "Manual correction proposal requires correction, deficit and accountant ids.");
        }
        ArgumentNullException.ThrowIfNull(proposal.Scope);
        ValidateScope(proposal.Scope);
        MoneyAmountPolicy.EnsurePositive(
            proposal.DeficitAmountAtCreation,
            "Manual correction creation deficit amount");
        MoneyAmountPolicy.EnsurePositive(
            proposal.DeficitAmountKztAtCreation,
            "Manual correction creation deficit KZT amount");
        MoneyAmountPolicy.EnsurePositive(
            proposal.CorrectionAmount,
            "Manual correction amount");
        MoneyAmountPolicy.EnsurePositive(
            proposal.CorrectionAmountKzt,
            "Manual correction KZT amount");
        if (proposal.CorrectionAmount > proposal.DeficitAmountAtCreation
            || proposal.CorrectionAmountKzt > proposal.DeficitAmountKztAtCreation)
        {
            throw new DomainRuleViolationException(
                "Manual correction proposal pair cannot exceed its creation balance.");
        }
        if (!IsPairedState(
                proposal.DeficitAmountAtCreation - proposal.CorrectionAmount,
                proposal.DeficitAmountKztAtCreation - proposal.CorrectionAmountKzt))
        {
            throw new DomainRuleViolationException(
                "Manual correction proposal cannot close only one side of the deficit pair.");
        }
        if (string.Equals(proposal.Scope.CurrencyCode, "KZT", StringComparison.Ordinal)
            && (proposal.DeficitAmountAtCreation != proposal.DeficitAmountKztAtCreation
                || proposal.CorrectionAmount != proposal.CorrectionAmountKzt))
        {
            throw new DomainRuleViolationException(
                "A KZT manual correction requires equal currency and KZT pairs.");
        }
        ValidateRequiredText(proposal.Basis, "Manual correction basis");
        if (proposal.State != ManualOverpaymentDeficitCorrectionState.PendingApproval)
        {
            throw new DomainRuleViolationException(
                "Only a pending manual correction can receive a decision.");
        }
    }

    private static void ValidateDeficit(OverpaymentDeficitSnapshot deficit)
    {
        if (deficit.DeficitId == Guid.Empty
            || deficit.OrganizationId == Guid.Empty
            || deficit.CounterpartyId == Guid.Empty
            || deficit.OccurredAtUtc == default)
        {
            throw new DomainRuleViolationException(
                "Manual correction requires a complete overpayment deficit snapshot.");
        }
        ValidateCurrencyCode(deficit.CurrencyCode);
        MoneyAmountPolicy.EnsurePositive(deficit.OriginalAmount, "Deficit original amount");
        MoneyAmountPolicy.EnsurePositive(deficit.OriginalAmountKzt, "Deficit original KZT amount");
        MoneyAmountPolicy.EnsureNonNegative(deficit.CurrentAmount, "Deficit current amount");
        MoneyAmountPolicy.EnsureNonNegative(deficit.CurrentAmountKzt, "Deficit current KZT amount");
        if (deficit.CurrentAmount > deficit.OriginalAmount
            || deficit.CurrentAmountKzt > deficit.OriginalAmountKzt)
        {
            throw new DomainRuleViolationException(
                "Overpayment deficit current pair cannot exceed its original pair.");
        }
        if (!IsPairedState(deficit.CurrentAmount, deficit.CurrentAmountKzt))
        {
            throw new DomainRuleViolationException(
                "Overpayment deficit current currency and KZT balances must close together.");
        }
        if (string.Equals(deficit.CurrencyCode, "KZT", StringComparison.Ordinal)
            && (deficit.OriginalAmount != deficit.OriginalAmountKzt
                || deficit.CurrentAmount != deficit.CurrentAmountKzt))
        {
            throw new DomainRuleViolationException(
                "A KZT overpayment deficit requires equal currency and KZT pairs.");
        }
    }

    private static bool SameScope(OverpaymentDeficitSnapshot deficit, OverpaymentScope scope) =>
        deficit.OrganizationId == scope.OrganizationId
        && deficit.CounterpartyId == scope.CounterpartyId
        && string.Equals(deficit.CurrencyCode, scope.CurrencyCode, StringComparison.Ordinal);

    private static void ValidateDecisionActorAndComment(
        Guid financialDirectorId,
        string? decisionComment)
    {
        if (financialDirectorId == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                "Manual correction decision requires a financial director id.");
        }
        ValidateOptionalText(decisionComment, "Manual correction decision comment");
    }

    private static void ValidateRequiredText(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumTextLength)
        {
            throw new DomainRuleViolationException(
                $"{fieldName} is required and cannot exceed {MaximumTextLength} characters.");
        }
    }

    private static void ValidateOptionalText(string? value, string fieldName)
    {
        if (value is { Length: > MaximumTextLength })
        {
            throw new DomainRuleViolationException(
                $"{fieldName} cannot exceed {MaximumTextLength} characters.");
        }
    }

    private static bool IsPairedState(decimal amount, decimal amountKzt) =>
        (amount == 0m) == (amountKzt == 0m);

    private static void ValidateScope(OverpaymentScope scope)
    {
        if (scope.OrganizationId == Guid.Empty || scope.CounterpartyId == Guid.Empty)
        {
            throw new DomainRuleViolationException(
                "Manual correction requires organization and counterparty scope.");
        }
        ValidateCurrencyCode(scope.CurrencyCode);
    }

    private static void ValidateCurrencyCode(string currencyCode)
    {
        if (currencyCode is not { Length: 3 }
            || currencyCode.Any(character => character is < 'A' or > 'Z'))
        {
            throw new DomainRuleViolationException(
                "Manual correction currency must contain exactly three uppercase ASCII letters.");
        }
    }
}
