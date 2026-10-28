using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Bms.Domain.Common;

namespace Bms.Domain.ExpenseRequests;

/// Payment requisites, not a budget/accrual dimension. Null is the legacy supplier operation.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExpensePaymentOperation(
    string Kind,
    string? PaymentKind = null,
    string? Period = null,
    string? TaxAuthorityName = null,
    string? TaxAuthorityCode = null,
    string? BudgetCode = null,
    string? PaymentCode = null,
    string? RecipientName = null,
    string? RecipientBinIin = null,
    string? RecipientIban = null,
    string? RecipientBik = null,
    string? RecipientKbe = null,
    string? PayrollMode = null,
    string? RegisterNumber = null,
    DateOnly? RegisterDate = null,
    Guid? EmployeeId = null,
    string? EmployeeName = null,
    string? EmployeeIin = null,
    string? AdvancePurpose = null,
    DateOnly? AdvanceReportDueDate = null,
    string? TransferMethod = null,
    bool IsPenalty = false,
    decimal? RegisterAmount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? AdvanceAmount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? BudgetCostCenterId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? EmployeeNames = null)
{
    public const string PayrollAdvanceKind = "PayrollAdvance";
    public const string BusinessTripKind = "BusinessTrip";
    public const int MaximumBusinessTripEmployees = 40;
    [JsonIgnore] public bool IsPayrollAdvance => Kind == PayrollAdvanceKind;
    [JsonIgnore] public bool IsBusinessTrip => Kind == BusinessTripKind;
    public static bool IsBudgetTaxKind(string? kind) => kind is "BudgetTax" or "VAT" or "CIT";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    // Keep legacy operation JSON/fingerprints byte-for-byte unchanged. The trip
    // list uses Cyrillic directly, still HTML-escaped, within the existing 8k-char
    // nvarchar JSON constraint; normalization also checks the complete payload.
    private static readonly JsonSerializerOptions TripJson = new(Json)
    { Encoder = JavaScriptEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic, UnicodeRanges.Latin1Supplement) };
    public bool Equals(ExpensePaymentOperation? other) => ReferenceEquals(this, other)
        || other is not null && string.Equals(Serialize(this), Serialize(other), StringComparison.Ordinal);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Serialize(this));
    public static readonly IReadOnlyDictionary<string, (string Principal, string Penalty)> SocialCodes =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        { ["OPV"] = ("010", "019"), ["OPVR"] = ("089", "098"), ["SO"] = ("012", "017"),
          ["OOSMS"] = ("121", "123"), ["VOSMS"] = ("122", "124") };
    public static ExpensePaymentOperation? Normalize(ExpensePaymentOperation? input)
    {
        if (input is null) return null;
        static string? Text(string? value, int max)
        {
            if (value is null) return null;
            if (value.Length > max || value.Any(char.IsControl)) throw new DomainRuleViolationException("Invalid operation requisites.");
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().Normalize();
        }
        if (input.EmployeeNames is { Count: > MaximumBusinessTripEmployees })
            throw new DomainRuleViolationException("Too many business trip employees.");
        var op = input with {
            Kind = Text(input.Kind, 20) ?? "", PaymentKind = Text(input.PaymentKind, 20), Period = Text(input.Period, 20),
            TaxAuthorityName = Text(input.TaxAuthorityName, 300), TaxAuthorityCode = Text(input.TaxAuthorityCode, 20),
            BudgetCode = Text(input.BudgetCode, 6), PaymentCode = Text(input.PaymentCode, 3),
            RecipientName = Text(input.RecipientName, 300), RecipientBinIin = Text(input.RecipientBinIin, 12),
            RecipientIban = Text(input.RecipientIban, 34)?.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant(),
            RecipientBik = Text(input.RecipientBik, 11)?.ToUpperInvariant(), RecipientKbe = Text(input.RecipientKbe, 2),
            PayrollMode = Text(input.PayrollMode, 20), RegisterNumber = Text(input.RegisterNumber, 100),
            EmployeeName = Text(input.EmployeeName, 150), EmployeeIin = Text(input.EmployeeIin, 12),
            AdvancePurpose = Text(input.AdvancePurpose, 1000), TransferMethod = Text(input.TransferMethod, 10),
            EmployeeNames = input.EmployeeNames?.Select(name => name is null
                ? throw new DomainRuleViolationException("Invalid employee name.") : Text(name, 150) ?? "").ToArray()
        };
        if (op.Kind is not ("Tax" or "Payroll" or "Accountable" or PayrollAdvanceKind or BusinessTripKind)) throw new DomainRuleViolationException("Invalid payment operation.");
        if (op.IsBusinessTrip)
        {
            if (Serialize(op with { EmployeeNames = null }) != Serialize(new ExpensePaymentOperation(BusinessTripKind)))
                throw new DomainRuleViolationException("Business trip requests contain employee names, not banking or other operation requisites.");
            if (Serialize(op).Length > 8000)
                throw new DomainRuleViolationException("Business trip employee requisites exceed the supported size.");
            return op;
        }
        if (op.EmployeeNames is not null) throw new DomainRuleViolationException("Employee lists belong to business trip requests.");
        if (op.BudgetCostCenterId == Guid.Empty || !op.IsPayrollAdvance && (op.AdvanceAmount is not null || op.BudgetCostCenterId is not null))
            throw new DomainRuleViolationException("Advance dimensions belong to another payment operation.");
        if (op.AdvanceAmount is { } advanceAmount)
            MoneyAmountPolicy.EnsureNonNegative(advanceAmount, "Advance amount");
        if (op.IsPayrollAdvance && (op.PaymentKind is not null || op.PaymentCode is not null || op.RecipientName is not null
                || op.RecipientBinIin is not null || op.RecipientIban is not null || op.RecipientBik is not null || op.RecipientKbe is not null
                || op.PayrollMode is not null || op.RegisterNumber is not null || op.RegisterDate is not null || op.RegisterAmount is not null
                || op.EmployeeId is not null || op.EmployeeIin is not null))
            throw new DomainRuleViolationException("Banking and payroll register requisites are maintained in 1C, not in an advance request.");
        if (op.PaymentKind is not null && !IsBudgetTaxKind(op.PaymentKind) && !SocialCodes.ContainsKey(op.PaymentKind))
            throw new DomainRuleViolationException("Invalid tax or social payment kind.");
        if (op.PayrollMode is not (null or "Register" or "Employee") || op.TransferMethod is not (null or "Bank" or "Cash")
            || op.EmployeeId == Guid.Empty) throw new DomainRuleViolationException("Invalid operation recipient.");
        static bool Digits(string? v, int length) => v is null || v.Length == length && v.All(char.IsAsciiDigit);
        if (!Digits(op.BudgetCode, 6) || !Digits(op.PaymentCode, 3) || !Digits(op.RecipientBinIin, 12)
            || !Digits(op.EmployeeIin, 12) || !Digits(op.RecipientKbe, 2))
            throw new DomainRuleViolationException("Invalid numeric payment requisites.");
        if (op.RecipientIban is not null && !IsIban(op.RecipientIban)) throw new DomainRuleViolationException("Invalid recipient IBAN.");
        if (op.RecipientBik is not null && !Regex.IsMatch(op.RecipientBik, "^[A-Z0-9]{8}([A-Z0-9]{3})?$", RegexOptions.CultureInvariant))
            throw new DomainRuleViolationException("Invalid recipient BIC.");
        if (op.Period is not null && !IsPeriod(op.Period, op.Kind == "Tax" && IsBudgetTaxKind(op.PaymentKind)))
            throw new DomainRuleViolationException("Invalid payment period.");
        if (op.RegisterAmount is not null && (op.RegisterAmount <= 0 || decimal.Round(op.RegisterAmount.Value, 2) != op.RegisterAmount))
            throw new DomainRuleViolationException("The register total must be a positive amount with two decimal places.");
        if (op.Kind is "Payroll" or "Accountable" && op.PayrollMode != "Register"
            && op.EmployeeIin is not null && op.RecipientBinIin is not null && op.EmployeeIin != op.RecipientBinIin)
            throw new DomainRuleViolationException("The employee and beneficiary IIN must match.");
        // Reject hidden requisites from other scenarios, rather than saving a mixed beneficiary.
        if (op.Kind != "Tax" && (op.PaymentKind is not null || op.TaxAuthorityName is not null || op.TaxAuthorityCode is not null
                || op.BudgetCode is not null || op.IsPenalty)
            || op.Kind != "Payroll" && op.PayrollMode is not null
            || op.Kind != "Accountable" && (op.AdvancePurpose is not null || op.AdvanceReportDueDate is not null)
            || op.Kind == "Tax" && (op.EmployeeId is not null || op.EmployeeName is not null || op.EmployeeIin is not null || op.TransferMethod == "Cash")
            || op.Kind == "Tax" && !IsBudgetTaxKind(op.PaymentKind) && (op.BudgetCode is not null || op.TaxAuthorityName is not null || op.TaxAuthorityCode is not null)
            || op.Kind == "Accountable" && (op.Period is not null || op.RegisterNumber is not null || op.RegisterDate is not null || op.RegisterAmount is not null)
            || op.TransferMethod == "Cash" && (op.RecipientIban is not null || op.RecipientBik is not null || op.RecipientKbe is not null)
            || op.Kind == "Payroll" && op.PayrollMode == "Register" && (op.EmployeeId is not null || op.EmployeeName is not null || op.EmployeeIin is not null))
            throw new DomainRuleViolationException("Requisites belong to another payment operation.");
        if ((op.Kind == "Tax" && IsBudgetTaxKind(op.PaymentKind) || op.Kind == "Payroll" && op.PayrollMode == "Employee")
            && (op.RegisterNumber is not null || op.RegisterDate is not null || op.RegisterAmount is not null))
            throw new DomainRuleViolationException("Register requisites belong to another payment scenario.");
        if (op.Kind == "Tax" && op.PaymentKind is not null && SocialCodes.TryGetValue(op.PaymentKind, out var codes)
            && op.PaymentCode is not null && op.PaymentCode != (op.IsPenalty ? codes.Penalty : codes.Principal))
            throw new DomainRuleViolationException("The social payment code does not match its kind.");
        return op;
    }
    public IReadOnlyList<string> MissingRequirements(string currency, SubmissionAttachmentFacts facts, IReadOnlyCollection<ExpenseRequestLine> lines, DateOnly? paymentDate)
    {
        var missing = new List<string>();
        void Required(string? value, string field) { if (string.IsNullOrWhiteSpace(value)) missing.Add("PaymentOperation." + field); }
        if (IsBusinessTrip)
        {
            if (EmployeeNames is not { Count: > 0 } || EmployeeNames.Any(string.IsNullOrWhiteSpace))
                missing.Add("PaymentOperation.EmployeeNames");
            if (lines.Any(line => line.VatRateCode != ExpenseVatRate.WithoutVat)) missing.Add("PaymentOperation.WithoutVat");
            return missing;
        }
        if (IsPayrollAdvance)
        {
            Required(Period, "Period"); Required(EmployeeName, "EmployeeName"); Required(TransferMethod, "TransferMethod");
            if (BudgetCostCenterId is null) missing.Add("PaymentOperation.BudgetCostCenterId");
            if (AdvanceAmount is null or <= 0m) missing.Add("PaymentOperation.AdvanceAmount");
            if (lines.Count != 1 || AdvanceAmount != lines.Sum(l => l.Amount)) missing.Add("PaymentOperation.AdvanceTotalMismatch");
            if (lines.Any(l => l.VatRateCode != ExpenseVatRate.WithoutVat)) missing.Add("PaymentOperation.WithoutVat");
            return missing;
        }
        void Register() { Required(RegisterNumber, "RegisterNumber"); if (RegisterDate is null) missing.Add("PaymentOperation.RegisterDate");
            if (!facts.HasSupportingDocument) missing.Add("PaymentOperation.RegisterDocument");
            if (RegisterAmount is null) missing.Add("PaymentOperation.RegisterAmount");
            else if (RegisterAmount != lines.Sum(line => line.Amount)) missing.Add("PaymentOperation.RegisterTotalMismatch"); }
        void Employee() { Required(EmployeeName, "EmployeeName"); Required(EmployeeIin, "EmployeeIin"); }
        if (Kind == "Tax" || TransferMethod == "Bank") Required(PaymentCode, "PaymentCode");
        if (Kind == "Tax")
        {
            if (currency != "KZT") missing.Add("PaymentOperation.TaxCurrencyKzt");
            Required(PaymentKind, "PaymentKind"); Required(Period, "Period");
            if (IsBudgetTaxKind(PaymentKind)) { Required(TaxAuthorityName, "TaxAuthorityName"); Required(TaxAuthorityCode, "TaxAuthorityCode"); Required(BudgetCode, "BudgetCode"); }
            else if (PaymentKind is not null) Register();
        }
        if (Kind == "Payroll") { Required(Period, "Period"); Required(PayrollMode, "PayrollMode");
            if (PayrollMode == "Register") Register(); else if (PayrollMode == "Employee") Employee(); }
        if (Kind == "Accountable") { Employee(); Required(AdvancePurpose, "AdvancePurpose");
            if (AdvanceReportDueDate is null || AdvanceReportDueDate < paymentDate) missing.Add("PaymentOperation.AdvanceReportDueDate"); }
        if (Kind != "Tax") Required(TransferMethod, "TransferMethod");
        if (Kind != "Tax" && TransferMethod == "Bank")
        {
            Required(RecipientName, "RecipientName"); Required(RecipientBinIin, "RecipientBinIin");
            Required(RecipientIban, "RecipientIban"); Required(RecipientBik, "RecipientBik"); Required(RecipientKbe, "RecipientKbe");
        }
        if (lines.Any(line => line.VatRateCode != ExpenseVatRate.WithoutVat)) missing.Add("PaymentOperation.WithoutVat");
        return missing;
    }
    public static bool IsPeriod(string value, bool tax) =>
        DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
        || tax && Regex.IsMatch(value, "^[1-9][0-9]{3}(-Q[1-4])?$", RegexOptions.CultureInvariant);
    public static bool IsIban(string value)
    {
        if (!Regex.IsMatch(value, "^[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}$", RegexOptions.CultureInvariant)
            || value.StartsWith("KZ", StringComparison.Ordinal) && value.Length != 20) return false;
        var remainder = 0;
        foreach (var c in value[4..] + value[..4])
        { if (char.IsAsciiDigit(c)) remainder = (remainder * 10 + c - '0') % 97;
          else remainder = (remainder * 100 + c - 'A' + 10) % 97; }
        return remainder == 1;
    }
    public static string Serialize(ExpensePaymentOperation value) => JsonSerializer.Serialize(value, value.IsBusinessTrip ? TripJson : Json);
    public static ExpensePaymentOperation Deserialize(string value) => Normalize(JsonSerializer.Deserialize<ExpensePaymentOperation>(value, Json))
        ?? throw new InvalidOperationException("Invalid stored payment operation.");
}
