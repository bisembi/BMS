using Bms.Domain.Common;
using System.Text.Json.Serialization;

namespace Bms.Domain.ExpenseRequests;

[JsonConverter(typeof(StrictStringEnumJsonConverter<CounterpartyResidency>))]
public enum CounterpartyResidency
{
    ResidentKz = 1,
    NonResident = 2
}
