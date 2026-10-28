namespace Bms.Infrastructure.Persistence;

/// <summary>
/// Read-only projection of the BMS currency catalogue.
/// Import ownership and freshness are outside the application command path.
/// </summary>
public sealed class CurrencyReference
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NumericCode { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

/// <summary>
/// Read-only daily projection of currency rates supplied to BMS.
/// Source timestamps intentionally carry no UTC claim.
/// </summary>
public sealed class CurrencyRateReference
{
    public Guid CurrencyId { get; set; }
    public DateOnly RateDate { get; set; }
    public decimal Rate { get; set; }
    public decimal Multiplicity { get; set; }
    public DateTime PublishedAtSource { get; set; }
    public string SourceName { get; set; } = string.Empty;
}
