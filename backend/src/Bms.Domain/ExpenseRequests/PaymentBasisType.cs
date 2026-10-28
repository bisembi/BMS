using Bms.Domain.Common;
using System.Text.Json.Serialization;

namespace Bms.Domain.ExpenseRequests;

[JsonConverter(typeof(StrictStringEnumJsonConverter<PaymentBasisType>))]
public enum PaymentBasisType
{
    Contract = 1,
    InvoiceWithoutContract = 2,
    PublicOffer = 3,
    OtherBasis = 4
}
