namespace Bms.Application.Abstractions;

/// <summary>Emergency reversible admission gate. It never blocks reading or
/// processing already-saved advances and never rewinds financial data.</summary>
public sealed record PayrollAdvanceCreationPolicy372(bool Enabled = true);
