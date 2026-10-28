using Bms.Domain.ExpenseRequests;
namespace Bms.Application.ExpenseRequests;
internal static partial class ExpenseRequestHistoryProjector
{
    private static void AddPaymentOperationChanges(List<ExpenseRequestHistoryChangeDto> changes,
        ExpensePaymentOperation? before, ExpensePaymentOperation? after, ref bool partial)
    {
        string Kind(ExpensePaymentOperation? op) => op?.Kind switch {"Tax"=>"Оплата налогов и социальных платежей","Payroll"=>"Оплата зарплаты","PayrollAdvance"=>"Выплата аванса","Accountable"=>"Выдача подотчёт","BusinessTrip"=>"Командировочные расходы",_=>"Оплата поставщику"};
        AddChange(changes, MainSection, "Операция", Kind(before), Kind(after), v=>v);
        var oldNames = before?.EmployeeNames ?? [];
        var newNames = after?.EmployeeNames ?? [];
        for (var i = 0; i < Math.Max(oldNames.Count, newNames.Count); i++)
            AddChange(changes, CounterpartySection, $"ФИО сотрудника, строка {i + 1}",
                i < oldNames.Count ? oldNames[i] : null, i < newNames.Count ? newNames[i] : null, v => v);
        var fields = new (string Property,string Label,bool Sensitive)[] {
            ("AdvanceAmount","Сумма аванса",false),
            ("PaymentKind","Вид налогового / социального платежа",false),("Period","Период платежа",false),
            ("TaxAuthorityName","УГД",false),("TaxAuthorityCode","Код УГД",false),("BudgetCode","КБК",false),("PaymentCode","КНП",false),
            ("RecipientName","Получатель",false),("RecipientBinIin","БИН / ИИН получателя",true),("RecipientIban","IBAN получателя",true),
            ("RecipientBik","БИК",false),("RecipientKbe","КБе",false),("PayrollMode","Оформление зарплаты",false),
            ("RegisterNumber","Номер ведомости / реестра",false),("RegisterDate","Дата ведомости / реестра",false),("RegisterAmount","Итог ведомости / реестра",false),
            ("EmployeeName","Сотрудник",false),("EmployeeIin","ИИН сотрудника",true),
            ("AdvancePurpose","Цель подотчёта",false),("AdvanceReportDueDate","Срок авансового отчёта",false),
            ("TransferMethod","Способ выплаты",false),("IsPenalty","Пеня",false)
        };
        foreach(var (property,label,sensitive) in fields)
        {
            var info=typeof(ExpensePaymentOperation).GetProperty(property)!;
            string? Value(ExpensePaymentOperation? op) {
                var v=op is null?null:info.GetValue(op);
                return v switch {null=>null, DateOnly date=>date.ToString("dd.MM.yyyy",System.Globalization.CultureInfo.InvariantCulture),
                    decimal amount=>amount.ToString("N2",System.Globalization.CultureInfo.GetCultureInfo("ru-RU")),bool flag=>flag?"Да":"Нет",
                    "BudgetTax"=>"Налог в бюджет","VAT"=>"НДС","CIT"=>"КПН","OOSMS"=>"Отчисления ОСМС","VOSMS"=>"Взносы ОСМС","Register"=>"По ведомости","Employee"=>"Отдельному сотруднику","Bank"=>"Банковский перевод","Cash"=>"Наличными",_=>v.ToString()};
            }
            if(sensitive) AddSensitiveChange(changes, CounterpartySection,label,Value(before),Value(after),ref partial);
            else AddChange(changes,CounterpartySection,label,Value(before),Value(after),v=>v);
        }
    }
}
