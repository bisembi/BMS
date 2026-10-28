using System.Globalization;
using System.Text.Json;
using Bms.Application.Abstractions;

namespace Bms.Application.ExpenseRequests;

/// Header changes, emitted in a fixed field order.
internal static partial class ExpenseRequestHistoryProjector
{

    private static List<ExpenseRequestHistoryChangeDto> BuildChanges(
        AuditSnapshot? before,
        AuditSnapshot after,
        out bool isPartial,
        ExpenseRequestHistoryReferences? references = null)
    {
        var changes = new List<ExpenseRequestHistoryChangeDto>();
        isPartial = false;
        AddPaymentOperationChanges(changes, before?.PaymentOperation, after.PaymentOperation, ref isPartial);

        AddChange(changes, MainSection, "Статус",
            NormalizeText(before?.Status), NormalizeText(after.Status), FormatStatus);
        AddReferenceChange(changes, MainSection, "Организация",
            before?.OrganizationId, after.OrganizationId, ref isPartial, references?.Organizations);
        AddReferenceChange(changes, MainSection, "ЦФО по умолчанию",
            before?.DefaultCostCenterId, after.DefaultCostCenterId, ref isPartial, references?.CostCenters);
        AddChange(changes, MainSection, "Планируемая дата оплаты",
            before?.PlannedPaymentDate, after.PlannedPaymentDate, FormatDate);
        AddChange(changes, MainSection, "Валюта",
            NormalizeText(before?.CurrencyCode), NormalizeText(after.CurrencyCode), FormatCurrency);
        AddReferenceChange(changes, MainSection, "Контрагент",
            before?.CounterpartyId, after.CounterpartyId, ref isPartial, references?.Counterparties);
        AddReferenceChange(changes, MainSection, "Договор",
            before?.ContractId, after.ContractId, ref isPartial, references?.Contracts);
        AddChange(changes, MainSection, "Назначение платежа",
            NormalizeText(before?.Purpose), NormalizeText(after.Purpose), value => value);
        AddChange(changes, MainSection, "Комментарий",
            NormalizeText(before?.Comment), NormalizeText(after.Comment), value => value);
        AddChange(changes, MainSection, "Обоснование",
            NormalizeText(before?.BusinessJustification),
            NormalizeText(after.BusinessJustification), value => value);
        AddChange(changes, MainSection, "Срочная заявка",
            before?.IsUrgent, after.IsUrgent, FormatBoolean);
        AddChange(changes, MainSection, "Причина срочности",
            NormalizeText(before?.UrgencyReason), NormalizeText(after.UrgencyReason), value => value);
        AddChange(changes, MainSection, "Новый контрагент",
            before?.IsNewCounterparty, after.IsNewCounterparty, FormatBoolean);
        AddChange(changes, MainSection, "Новый договор",
            before?.IsNewContract, after.IsNewContract, FormatBoolean);

        AddChange(changes, CounterpartySection, "Основание оплаты",
            NormalizeText(before?.PaymentBasisCode),
            NormalizeText(after.PaymentBasisCode),
            FormatPaymentBasis);
        AddChange(changes, CounterpartySection, "Номер счёта",
            NormalizeText(before?.InvoiceNumber), NormalizeText(after.InvoiceNumber), value => value);
        AddChange(changes, CounterpartySection, "Дата счёта",
            before?.InvoiceDate, after.InvoiceDate, FormatDate);
        AddChange(changes, CounterpartySection, "Наименование публичной оферты",
            NormalizeText(before?.PublicOfferName), NormalizeText(after.PublicOfferName), value => value);
        AddChange(changes, CounterpartySection, "Ссылка или идентификатор оферты",
            NormalizeText(before?.PublicOfferIdentifier),
            NormalizeText(after.PublicOfferIdentifier), value => value);
        AddChange(changes, CounterpartySection, "Пояснение иного основания",
            NormalizeText(before?.OtherBasisExplanation),
            NormalizeText(after.OtherBasisExplanation), value => value);
        AddChange(changes, CounterpartySection, "Подтверждающий документ отсутствует",
            before?.SupportingDocumentMissing,
            after.SupportingDocumentMissing,
            FormatBoolean);
        AddChange(changes, CounterpartySection, "Пояснение отсутствия документа",
            NormalizeText(before?.SupportingDocumentMissingExplanation),
            NormalizeText(after.SupportingDocumentMissingExplanation), value => value);
        AddChange(changes, CounterpartySection, "Наименование нового контрагента",
            NormalizeText(before?.CandidateCounterpartyName),
            NormalizeText(after.CandidateCounterpartyName), value => value);
        AddChange(changes, CounterpartySection, "Резидентность",
            NormalizeText(before?.CandidateResidencyCode),
            NormalizeText(after.CandidateResidencyCode),
            FormatResidency);
        AddSensitiveChange(changes, CounterpartySection, "БИН/ИИН",
            before?.CandidateBinIin, after.CandidateBinIin, ref isPartial);
        AddChange(changes, CounterpartySection, "Страна регистрации",
            NormalizeText(before?.CandidateCountry), NormalizeText(after.CandidateCountry), value => value);
        AddSensitiveChange(changes, CounterpartySection, "Регистрационный номер нерезидента",
            before?.CandidateForeignRegistrationNumber,
            after.CandidateForeignRegistrationNumber,
            ref isPartial);
        AddChange(changes, CounterpartySection, "Регистрационный номер отсутствует",
            before?.CandidateRegistrationNumberMissing,
            after.CandidateRegistrationNumberMissing,
            FormatBoolean);
        AddChange(changes, CounterpartySection, "Пояснение отсутствия регистрационного номера",
            NormalizeText(before?.CandidateRegistrationNumberMissingExplanation),
            NormalizeText(after.CandidateRegistrationNumberMissingExplanation), value => value);
        AddChange(changes, CounterpartySection, "Номер нового договора",
            NormalizeText(before?.CandidateContractNumber),
            NormalizeText(after.CandidateContractNumber), value => value);
        AddChange(changes, CounterpartySection, "Номер нового договора отсутствует",
            before?.CandidateContractNumberMissing,
            after.CandidateContractNumberMissing,
            FormatBoolean);
        AddChange(changes, CounterpartySection, "Дата нового договора",
            before?.CandidateContractDate, after.CandidateContractDate, FormatDate);
        AddChange(changes, CounterpartySection, "Предмет нового договора",
            NormalizeText(before?.CandidateContractSubject),
            NormalizeText(after.CandidateContractSubject), value => value);
        AddChange(changes, CounterpartySection, "Условия оплаты нового договора",
            NormalizeText(before?.CandidateContractTerms),
            NormalizeText(after.CandidateContractTerms), value => value);
        AddChange(changes, CounterpartySection, "Файл нового договора отсутствует",
            before?.NewContractDocumentMissing,
            after.NewContractDocumentMissing,
            FormatBoolean);
        AddChange(changes, CounterpartySection, "Пояснение отсутствия файла договора",
            NormalizeText(before?.NewContractDocumentMissingExplanation),
            NormalizeText(after.NewContractDocumentMissingExplanation), value => value);

        AddAmountChange(changes, MainSection, "Общая сумма",
            before?.TotalAmount, after.TotalAmount,
            before?.CurrencyCode, after.CurrencyCode);
        AddLineChanges(
            changes,
            before?.Lines ?? [],
            after.Lines ?? [],
            before?.CurrencyCode,
            after.CurrencyCode,
            ref isPartial,
            references);

        return changes;
    }
}
