using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bms.Application.Audit;

public static partial class ChangeJournalFormatter
{
    private const string Unavailable = "Изменение зарегистрировано; подробности недоступны";

    public static ChangeJournalItemDto Format(ChangeJournalRecord record)
    {
        var author = SafeText(record.AuthorName);
        if (author.Length == 0) author = "Автор не определён";
        var eventName = EventName(record.Action);
        var objectName = ObjectName(record.ObjectType);
        var description = Unavailable;
        try
        {
            if (record.AfterJson is null || record.AfterJson.Length > 1_048_576)
                return new(record.OccurredAtUtc, author, eventName, objectName, description);
            using var document = JsonDocument.Parse(record.AfterJson, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new(record.OccurredAtUtc, author, eventName, objectName, description);
            if (record.Action == "ExpenseRequestDraftDeleted" && Property(root, "trainingCleanup").ValueKind == JsonValueKind.True)
                return new(record.OccurredAtUtc, author, "Удалена тренировочная заявка", "Расходная заявка",
                    "Удаление перед боевым запуском; история и вложения сохранены для восстановления.");

            // Only names and reviewed display fields are admitted. Unknown actions
            // stay visible, but neither their JSON nor their technical name escapes.
            if (eventName == "Изменение зарегистрировано")
                return new(record.OccurredAtUtc, author, eventName, objectName, description);
            objectName = Text(root, "name") is { Length: > 0 } name ? name : objectName;
            description = "";
            if (record.Action.StartsWith("ReferenceData", StringComparison.Ordinal))
                description = "Справочник: " + DirectoryName(Text(root, "directoryId"));
            else if (record.Action.StartsWith("BudgetOwnerAssignment", StringComparison.Ordinal))
            {
                objectName = Text(root, "employeeName");
                description = "ЦФО: " + Text(root, "costCenterName");
            }
            else if (record.Action == "CashFlowAccrualSettingsChanged")
            {
                var settings = Property(root, "settings");
                objectName = Text(settings, "name");
                var enabled = Property(settings, "formsManagerialAccrual");
                var expense = Text(Property(settings, "expenseItem"), "name");
                description = enabled.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? $"Акрул {(enabled.GetBoolean() ? "включён" : "выключен")}; статья затрат: {(expense.Length > 0 ? expense : "не указана")}" : Unavailable;
                if (settings.ValueKind == JsonValueKind.Object && settings.EnumerateObject().Any(property =>
                    string.Equals(property.Name, "costCenter", StringComparison.OrdinalIgnoreCase)))
                {
                    var center = Text(Property(settings, "costCenter"), "name");
                    description += $"; ЦФО: {(center.Length > 0 ? center : "не назначено")}";
                    eventName = "Изменены настройки статьи ДДС";
                }
                var activity = Text(settings, "activityType") switch
                {
                    "Operating" => "Операционная деятельность",
                    "Investing" => "Инвестиционная деятельность",
                    "Financing" => "Финансовая деятельность",
                    _ => ""
                };
                if (activity.Length > 0)
                {
                    var group = Text(settings, "groupName");
                    description += $"; вид деятельности: {activity}; группа: {(group.Length > 0 ? group : "не назначена")}";
                    eventName = "Изменены настройки статьи ДДС";
                }
            }
            else if (record.Action is "AuthorizationDirectoryEmployeeCreated" or "AuthorizationDirectoryEmployeeCreatedFromAd"
                or "AuthorizationDirectoryRoleAssignmentUpdated")
            {
                var roles = Property(root, "roles");
                if (roles.ValueKind != JsonValueKind.Array || roles.GetArrayLength() > 32
                    || roles.EnumerateArray().Any(r => r.ValueKind != JsonValueKind.String))
                    return new(record.OccurredAtUtc, author, eventName, objectName, Unavailable);
                var names = roles.EnumerateArray().Select(r => SafeText(r.GetString())).ToArray();
                if (names.Any(r => r.Length == 0) || names.Sum(r => r.Length + 2) > 8_000)
                    return new(record.OccurredAtUtc, author, eventName, objectName, Unavailable);
                var prefix = record.Action == "AuthorizationDirectoryRoleAssignmentUpdated"
                    ? "Роли после изменения: " : "Роли при добавлении: ";
                description = prefix + (names.Length > 0 ? string.Join(", ", names) : "не назначены");
            }
            else if (record.ObjectType == "BudgetInstance")
            {
                var number = Text(root, "number");
                objectName = number.Length > 0 ? "Экземпляр бюджета " + number : "Экземпляр бюджета";
                description = "ЦФО: " + Text(root, "costCenterName");
                var from = Text(root, "validFrom");
                var to = Text(root, "validTo");
                if (from.Length > 0 && to.Length > 0) description += $"; период: {from} — {to}";
                if (record.Action == "BudgetInstanceTestDeleted")
                    description += "; удалён тестовый экземпляр по прямому поручению владельца";
                if (record.Action == "BudgetInstanceRenumbered")
                    description += "; номер: " + Text(root, "previousNumber") + " → " + number;
            }
            else if (record.Action == "BudgetDirectoryUpdated")
                description = "Настройки бюджета по ЦФО";
            else if (record.ObjectType == "ExpenseRequest")
            {
                var request = Property(root, "request");
                if (request.ValueKind != JsonValueKind.Object) request = root;
                var number = Text(request, "number");
                objectName = number.Length > 0 ? "Расходная заявка " + number : "Расходная заявка";
            }
            if (objectName.Length == 0) objectName = ObjectName(record.ObjectType);
        }
        catch (JsonException) { description = Unavailable; }
        return new(record.OccurredAtUtc, author, eventName, objectName, description);
    }

    private static JsonElement Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return default;
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        return default;
    }

    private static string Text(JsonElement element, string name)
    {
        var value = Property(element, name);
        return value.ValueKind == JsonValueKind.String ? SafeText(value.GetString()) : "";
    }

    private static string SafeText(string? value)
    {
        if (value is null || value.Length > 2_048) return "";
        var text = InternalCode().Replace(value, "").Trim();
        if (TechnicalIdentity().IsMatch(text)) return "";
        return new string(text.Where(c => !char.IsControl(c)).Take(600).ToArray());
    }

    private static string DirectoryName(string id) => id switch
    {
        "cash-flow-items" => "Статьи ДДС",
        "cost-centers" => "ЦФО",
        "currencies" => "Валюты",
        "organizations" => "Организации",
        "expense-items" => "Статьи затрат",
        "contracts" => "Договоры",
        "counterparties" => "Контрагенты",
        _ => "Справочники"
    };

    private static string ObjectName(string type) => type switch
    {
        "AuthorizationDirectoryEmployee" => "Сотрудник",
        "BudgetOwnerAssignment" => "Владелец бюджета",
        "BudgetInstance" => "Экземпляр бюджета",
        "BudgetCostCenterSettings" => "ЦФО",
        "CashFlowItem" => "Статья ДДС",
        "ExpenseItem" => "Статья затрат",
        "Organization" => "Организация",
        "FunctionalRoleReference" => "Роль сотрудника",
        "ExpenseRequest" => "Расходная заявка",
        "ReferenceData" => "Запись справочника",
        _ => "Запись BMS"
    };

    private static string EventName(string action) => action switch
    {
        "BudgetInstanceRenumbered" => "Изменён номер экземпляра бюджета",
        "ReferenceDataCreated" => "Добавлена запись справочника",
        "ReferenceDataUpdated" => "Изменена запись справочника",
        "ReferenceDataDeleted" => "Удалена запись справочника",
        "OrganizationCreated" => "Добавлена организация",
        "OrganizationUpdated" => "Изменена организация",
        "ExpenseItemCreated" => "Создана статья затрат",
        "ExpenseItemUpdated" => "Изменена статья затрат",
        "CashFlowAccrualSettingsChanged" => "Изменены настройки акрула",
        "AuthorizationDirectoryEmployeeCreated" => "Добавлен сотрудник",
        "AuthorizationDirectoryEmployeeCreatedFromAd" => "Добавлен сотрудник из AD",
        "AuthorizationDirectoryRoleAssignmentUpdated" => "Изменены роли сотрудника",
        "AdActorMappingUpdated" => "Изменена доменная связь сотрудника",
        "AuthorizationDirectoryEmployeeDeleted" => "Удалён сотрудник",
        "FunctionalRoleReferenceCreated" => "Добавлена роль сотрудника",
        "BudgetOwnerAssignmentCreated" => "Назначен владелец бюджета",
        "BudgetOwnerAssignmentUpdated" => "Изменено назначение владельца бюджета",
        "BudgetOwnerAssignmentDeleted" => "Удалено назначение владельца бюджета",
        "BudgetDirectoryUpdated" => "Изменены настройки бюджета по ЦФО",
        "BudgetInstanceDraftCreated" => "Создан черновик бюджета",
        "BudgetInstanceDraftUpdated" => "Изменён черновик бюджета",
        "BudgetInstanceDraftDeleted" => "Удалён черновик бюджета",
        "BudgetInstanceTestDeleted" => "Удалён тестовый экземпляр бюджета",
        "BudgetInstanceApproved" => "Утверждён бюджет",
        "DraftCreated" => "Создан черновик заявки",
        "DraftUpdated" => "Изменён черновик заявки",
        "DraftCopied" => "Создана копия черновика заявки",
        "WorkflowApproved" => "Заявка согласована",
        "WorkflowReturned" => "Заявка возвращена на доработку",
        "WorkflowRejected" => "Заявка отклонена",
        "WorkflowCancelled" => "Заявка отменена",
        "SubmittedForApproval" => "Заявка подана на согласование",
        "SubmittedToPayment" => "Заявка передана в оплату",
        "SubmittedToNsi" => "Заявка передана на проверку реквизитов НСИ",
        "WorkflowNsiConfirmed" => "НСИ подтверждена",
        "WorkflowSentToPayment" => "Заявка передана в оплату",
        "ExpenseRequestClosedPartially" => "Заявка закрыта частично",
        "ExpenseRequestClosedFully" => "Заявка закрыта",
        "ExpenseRequestReopened" => "Заявка открыта повторно",
        "PaymentApplied" => "Зарегистрирована оплата заявки",
        "Submitted" => "Заявка подана на согласование",
        "Approved" => "Заявка согласована",
        "Rejected" => "Заявка отклонена",
        "ReturnedToDraft" => "Заявка возвращена в черновик",
        _ => "Изменение зарегистрировано"
    };

    [GeneratedRegex(@"\bCF-\d+\s*(?:[—–·:]\s*)?", RegexOptions.CultureInvariant)]
    private static partial Regex InternalCode();
    [GeneratedRegex(@"\bS-\d-\d+(?:-\d+)+\b|\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TechnicalIdentity();
}
