namespace Bms.Application.Abstractions;

public sealed record AccountingSourceCashLeg(string DocumentKey,int LineNo,string EntryDateLocal,decimal AmountKzt);
public sealed record AccountingSourcePayment(string DocumentKey,string FinancialHash,string OrganizationCode,
    string OrganizationName,string CounterpartyRef,string CounterpartyName,string Bin,string CurrencyNumericCode,
    DateOnly PaymentDay,string DocumentNumber,decimal Amount,decimal AmountKzt,bool Eligible,int LegCount,
    string? RequestNumber = null, bool OperationReferenceEligible = false, bool PayrollAdvanceEligible = false);
public sealed record AccountingPaymentSourceSnapshot(Guid Id,DateTimeOffset CapturedAtUtc,DateOnly CutoffExclusiveLocal,
    IReadOnlyList<AccountingSourcePayment> Documents,IReadOnlyList<AccountingSourceCashLeg> Legs,
    Guid? PayrollEvidenceDdsSnapshotId = null);

public interface IAccountingPaymentSourceReader
{
    Task<AccountingPaymentSourceSnapshot> ReadAsync(CancellationToken token);
    Task<IReadOnlyList<AccountingSourceCashLeg>> ReadRepresentedLegsAsync(Guid ddsSnapshotId,
        IReadOnlyCollection<string> documentKeys,CancellationToken token);
}
