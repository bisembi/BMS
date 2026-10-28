using Bms.Domain.Common;

namespace Bms.Domain.Payments;

public sealed record ReferencedOperationRequest(Guid Id, string Number, Guid OrganizationId, string Currency, decimal Remaining);
public sealed record ReferencedOperationPayment(string Key, string Number, Guid OrganizationId, string Currency, decimal Amount, bool Verified);

/// Explicit BMS references are identities, not fuzzy beneficiary/amount guesses.
/// The whole referenced group is admitted or quarantined; no amount-selected subset.
public static class ReferencedOperationPaymentMatcher
{
    public static AccountingPaymentMatchPlan Plan(IReadOnlyList<ReferencedOperationRequest> requests,
        IReadOnlyList<ReferencedOperationPayment> payments)
    {
        if (requests.Count > 10000 || payments.Count > 100000
            || requests.Select(x => x.Id).Distinct().Count() != requests.Count
            || payments.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != payments.Count)
            throw new DomainRuleViolationException("Bounded unique operation capture required.");
        var matched = new List<AccountingPaymentMatch>();
        var ambiguous = new HashSet<Guid>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in requests.GroupBy(r => r.Number, StringComparer.Ordinal))
        {
            var candidates = payments.Where(p => p.Number == group.Key).ToArray();
            if (candidates.Length == 0) continue;
            var rows = group.ToArray();
            if (rows.Length != 1) { foreach (var r in rows) ambiguous.Add(r.Id); continue; }
            var request = rows[0];
            if (request.Id == Guid.Empty || request.OrganizationId == Guid.Empty || request.Remaining <= 0
                || candidates.Any(p => !p.Verified || p.Amount <= 0 || p.OrganizationId != request.OrganizationId
                    || p.Currency != request.Currency) || candidates.Sum(p => p.Amount) > request.Remaining)
            { ambiguous.Add(request.Id); continue; }
            matched.Add(new(request.Id, candidates.Select(p => p.Key).Order(StringComparer.Ordinal).ToArray()));
            foreach (var payment in candidates) used.Add(payment.Key);
        }
        return new(matched, ambiguous.Order().ToArray(), payments.Where(p => !used.Contains(p.Key)).Select(p => p.Key).ToArray());
    }
}
