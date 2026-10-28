using Bms.Domain.Common;

namespace Bms.Domain.Payments;

/// <summary>Identity resolution to a BMS counterparty happens before this pure
/// matching boundary. Names, sums or dates never become a document identity.</summary>
public sealed record AccountingPaymentCandidate(string DocumentKey, string FinancialHash, Guid OrganizationId,
    Guid CounterpartyId, string CurrencyCode, DateOnly PaymentDay, decimal Amount, decimal AmountKzt);
public sealed record PaymentRequestCandidate(Guid RequestId, Guid OrganizationId, Guid CounterpartyId,
    string CurrencyCode, DateOnly TreasuryDay, decimal OutstandingAmount);
public sealed record AccountingPaymentMatch(Guid RequestId, IReadOnlyList<string> DocumentKeys);
public sealed record AccountingPaymentMatchPlan(IReadOnlyList<AccountingPaymentMatch> Matches,
    IReadOnlyList<Guid> AmbiguousRequestIds, IReadOnlyList<string> UnmatchedDocumentKeys);

/// <summary>Exact, conservative, one-to-one or one-request / whole-day-group.
/// No fuzzy counterparty, nearest date, tolerance, arbitrary subset or FIFO
/// decision. Existing linked documents must be removed by the caller before
/// planning, under the same locked source/request capture.</summary>
public static class AccountingPaymentMatcher
{
    public static AccountingPaymentMatchPlan Plan(IReadOnlyList<PaymentRequestCandidate> requests,
        IReadOnlyList<AccountingPaymentCandidate> documents)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(documents);
        if (requests.Count > 100_000 || documents.Count > 200_000
            || requests.Select(r => r.RequestId).Distinct().Count() != requests.Count
            || documents.Select(d => d.DocumentKey).Distinct(StringComparer.Ordinal).Count() != documents.Count)
            throw new DomainRuleViolationException("Payment matching requires a bounded, unique capture.");
        foreach (var r in requests)
        {
            Validate(r.OrganizationId, r.CounterpartyId, r.CurrencyCode, r.TreasuryDay, r.OutstandingAmount);
            if (r.RequestId == Guid.Empty) throw new DomainRuleViolationException("Request identity is required.");
        }
        foreach (var d in documents)
        {
            Validate(d.OrganizationId, d.CounterpartyId, d.CurrencyCode, d.PaymentDay, d.Amount);
            MoneyAmountPolicy.EnsurePositive(d.AmountKzt, "Source KZT amount");
            if (string.IsNullOrWhiteSpace(d.DocumentKey) || d.DocumentKey.Length > 150 || d.DocumentKey.Any(char.IsControl)
                || d.FinancialHash.Length != 64 || d.FinancialHash.Any(c => c is not (>= '0' and <= '9') and not (>= 'A' and <= 'F'))
                || (d.CurrencyCode == "KZT" && d.Amount != d.AmountKzt))
                throw new DomainRuleViolationException("Payment source identity or monetary evidence is invalid.");
        }
        var matches = new List<AccountingPaymentMatch>();
        var ambiguous = new HashSet<Guid>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var source = documents.GroupBy(d => (d.OrganizationId, d.CounterpartyId, d.CurrencyCode, d.PaymentDay))
            .ToDictionary(g => g.Key, g => g.OrderBy(d => d.DocumentKey, StringComparer.Ordinal).ToArray());
        foreach (var group in requests.GroupBy(r => (r.OrganizationId, r.CounterpartyId, r.CurrencyCode, r.TreasuryDay)))
        {
            if (!source.TryGetValue(group.Key, out var docs)) continue;
            var pending = group.ToArray();
            var originalRequestCount=pending.Length;
            var amounts=docs.GroupBy(d=>d.Amount).ToDictionary(g=>g.Key,g=>g.ToArray());
            var requestAmounts=pending.GroupBy(r=>r.OutstandingAmount).ToDictionary(g=>g.Key,g=>g.Count());
            var linkedRequests=new HashSet<Guid>();
            // A unique exact amount on BOTH sides is the only individually
            // decidable pair. Equal requests/payments are not assigned by order.
            foreach (var r in pending)
            {
                var candidates = amounts.GetValueOrDefault(r.OutstandingAmount)??[];
                if (candidates.Length == 1 && requestAmounts[r.OutstandingAmount] == 1)
                {
                    matches.Add(new(r.RequestId, [candidates[0].DocumentKey]));
                    used.Add(candidates[0].DocumentKey);
                    linkedRequests.Add(r.RequestId);
                }
            }
            pending = pending.Where(r => !linkedRequests.Contains(r.RequestId)).ToArray();
            var rest = docs.Where(d => !used.Contains(d.DocumentKey)).ToArray();
            if (originalRequestCount == 1 && pending.Length == 1 && rest.Length > 1 && rest.Sum(d => d.Amount) == pending[0].OutstandingAmount)
            {
                matches.Add(new(pending[0].RequestId, rest.Select(d => d.DocumentKey).ToArray()));
                foreach (var d in rest) used.Add(d.DocumentKey);
            }
            else if (pending.Length > 0 && rest.Length > 0)
            {
                foreach (var r in pending) ambiguous.Add(r.RequestId);
            }
        }
        return new(matches.OrderBy(m => m.RequestId).ToArray(), ambiguous.Order().ToArray(),
            documents.Where(d => !used.Contains(d.DocumentKey)).Select(d => d.DocumentKey).Order(StringComparer.Ordinal).ToArray());
    }

    private static void Validate(Guid organization, Guid party, string currency, DateOnly day, decimal amount)
    {
        if (organization == Guid.Empty || party == Guid.Empty || day == default || currency.Length != 3
            || currency.Any(c => c is < 'A' or > 'Z'))
            throw new DomainRuleViolationException("Exact organization, counterparty, currency and payment day are required.");
        MoneyAmountPolicy.EnsurePositive(amount, "Payment matching amount");
    }
}
