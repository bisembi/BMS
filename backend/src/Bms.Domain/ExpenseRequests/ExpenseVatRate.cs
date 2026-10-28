using System.Text.Json.Serialization;

namespace Bms.Domain.ExpenseRequests;

[JsonConverter(typeof(JsonStringEnumConverter<ExpenseVatRate>))]
public enum ExpenseVatRate
{
    WithoutVat = 0,
    Vat16 = 1
}
