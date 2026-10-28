using Microsoft.Extensions.Configuration;

namespace Bms.Infrastructure.Persistence;

public sealed record AccountingPaymentSettings(bool BudgetControlEnabled,bool ReconciliationEnabled,bool ReschedulingEnabled)
{
    internal static AccountingPaymentSettings Read(IConfiguration c)
    {
        var settings=new AccountingPaymentSettings(c.GetValue<bool>("Bms:AccountingPayments:BudgetControlEnabled"),
            c.GetValue<bool>("Bms:AccountingPayments:ReconciliationEnabled"),c.GetValue<bool>("Bms:AccountingPayments:ReschedulingEnabled"));
        if((settings.ReconciliationEnabled||settings.ReschedulingEnabled)&&!settings.BudgetControlEnabled)
            throw new InvalidOperationException("Accounting mutation requires source-aware budget control.");
        return settings;
    }
}
