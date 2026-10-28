# Каталог HTTP-регистраций BMS 1.0

Технический исходный срез: catalog485.

Механически извлечены 196 регистраций с разрешённым статическим префиксом
и 0 с префиксом, требующим чтения контекста. Это **не OpenAPI**
и не доказательство доступности: регистрация, middleware, флаги, роль, область,
состояние и серверная валидация проверяются отдельно. Параметры `{id:guid}` —
шаблон ASP.NET, не готовый адрес запроса. Динамические выражения и другие виды
регистраций могут не попасть в механическую выборку.

## Правила клиента

Использовать Windows-вход и HTTPS. При записи применять выданный CSRF-токен
и контракт идентификатора попытки. Передавать ожидаемую версию там, где она
нужна. Не придумывать actor-ID из JSON и не заменять роли заголовком клиента.
При 409 перечитать конфликт; при неизвестном исходе разрешить прежнюю
идемпотентную попытку, не создавать новую мутацию вслепую.

Точные JSON-формы: [публичные декодеры frontend](../frontend/src/api) и
[серверные endpoints](../backend/src/Bms.Api/Endpoints). Источником точного
запроса/ответа служат соответствующий обработчик и DTO, не только эта таблица.

## Статические маршруты

| Метод | Шаблон | Регистрация |
| --- | --- | --- |
| GET | `/api/v1/administration/accrual-notifications` | [AccrualNotificationAdministrationEndpoints.cs:14](../backend/src/Bms.Api/Endpoints/AccrualNotificationAdministrationEndpoints.cs#L14) |
| GET | `/api/v1/administration/accrual-notifications/issues` | [AccrualNotificationAdministrationEndpoints.cs:16](../backend/src/Bms.Api/Endpoints/AccrualNotificationAdministrationEndpoints.cs#L16) |
| GET | `/api/v1/administration/active-sessions` | [ActiveSessionEndpoints.cs:16](../backend/src/Bms.Api/Endpoints/ActiveSessionEndpoints.cs#L16) |
| GET | `/api/v1/administration/change-journal` | [ChangeJournalEndpoints.cs:11](../backend/src/Bms.Api/Endpoints/ChangeJournalEndpoints.cs#L11) |
| GET | `/api/v1/administration/exchange-journal` | [ExchangeJournalEndpoints.cs:12](../backend/src/Bms.Api/Endpoints/ExchangeJournalEndpoints.cs#L12) |
| GET | `/api/v1/administration/exchange-journal/{kind}/{id:guid}` | [ExchangeJournalEndpoints.cs:13](../backend/src/Bms.Api/Endpoints/ExchangeJournalEndpoints.cs#L13) |
| GET | `/api/v1/administration/registers/budget-execution` | [BudgetExecutionRegisterEndpoints343.cs:11](../backend/src/Bms.Api/Endpoints/BudgetExecutionRegisterEndpoints343.cs#L11) |
| GET | `/api/v1/administration/usage-log` | [ActiveSessionEndpoints.cs:17](../backend/src/Bms.Api/Endpoints/ActiveSessionEndpoints.cs#L17) |
| GET | `/api/v1/administration/usage-log/daily` | [ActiveSessionEndpoints.cs:18](../backend/src/Bms.Api/Endpoints/ActiveSessionEndpoints.cs#L18) |
| GET | `/api/v1/audit-monitor/business-events` | [BusinessAuditMonitorEndpoints.cs:14](../backend/src/Bms.Api/Endpoints/BusinessAuditMonitorEndpoints.cs#L14) |
| GET | `/api/v1/audit-monitor/business-events/{auditId:guid}` | [BusinessAuditDetailEndpoints.cs:14](../backend/src/Bms.Api/Endpoints/BusinessAuditDetailEndpoints.cs#L14) |
| GET | `/api/v1/audit-monitor/business-events/search` | [BusinessAuditSearchEndpoints.cs:14](../backend/src/Bms.Api/Endpoints/BusinessAuditSearchEndpoints.cs#L14) |
| POST | `/api/v1/budget/actuals/import` | [BudgetActualsEndpoints.cs:15](../backend/src/Bms.Api/Endpoints/BudgetActualsEndpoints.cs#L15) |
| POST | `/api/v1/budget/actuals/preview` | [BudgetActualsEndpoints.cs:14](../backend/src/Bms.Api/Endpoints/BudgetActualsEndpoints.cs#L14) |
| GET | `/api/v1/budget/adjustments` | [BudgetAdjustmentEndpoints.cs:15](../backend/src/Bms.Api/Endpoints/BudgetAdjustmentEndpoints.cs#L15) |
| POST | `/api/v1/budget/adjustments` | [BudgetAdjustmentEndpoints.cs:21](../backend/src/Bms.Api/Endpoints/BudgetAdjustmentEndpoints.cs#L21) |
| GET | `/api/v1/budget/adjustments/{id:guid}` | [BudgetAdjustmentEndpoints.cs:19](../backend/src/Bms.Api/Endpoints/BudgetAdjustmentEndpoints.cs#L19) |
| PUT | `/api/v1/budget/adjustments/{id:guid}` | [BudgetAdjustmentEndpoints.cs:23](../backend/src/Bms.Api/Endpoints/BudgetAdjustmentEndpoints.cs#L23) |
| POST | `/api/v1/budget/adjustments/{id:guid}/{action}` | [BudgetAdjustmentEndpoints.cs:25](../backend/src/Bms.Api/Endpoints/BudgetAdjustmentEndpoints.cs#L25) |
| GET | `/api/v1/budget/adjustments/basis/{baseId:guid}` | [BudgetAdjustmentEndpoints.cs:17](../backend/src/Bms.Api/Endpoints/BudgetAdjustmentEndpoints.cs#L17) |
| GET | `/api/v1/budget/company-versions` | [CompanyBudgetVersionEndpoints.cs:14](../backend/src/Bms.Api/Endpoints/CompanyBudgetVersionEndpoints.cs#L14) |
| POST | `/api/v1/budget/company-versions` | [CompanyBudgetVersionEndpoints.cs:20](../backend/src/Bms.Api/Endpoints/CompanyBudgetVersionEndpoints.cs#L20) |
| DELETE | `/api/v1/budget/company-versions/{id:guid}` | [CompanyBudgetVersionEndpoints.cs:32](../backend/src/Bms.Api/Endpoints/CompanyBudgetVersionEndpoints.cs#L32) |
| GET | `/api/v1/budget/company-versions/{id:guid}` | [CompanyBudgetVersionEndpoints.cs:18](../backend/src/Bms.Api/Endpoints/CompanyBudgetVersionEndpoints.cs#L18) |
| PUT | `/api/v1/budget/company-versions/{id:guid}` | [CompanyBudgetVersionEndpoints.cs:26](../backend/src/Bms.Api/Endpoints/CompanyBudgetVersionEndpoints.cs#L26) |
| POST | `/api/v1/budget/company-versions/{id:guid}/activate` | [CompanyBudgetVersionEndpoints.cs:28](../backend/src/Bms.Api/Endpoints/CompanyBudgetVersionEndpoints.cs#L28) |
| PUT | `/api/v1/budget/company-versions/{id:guid}/active-composition` | [CompanyBudgetVersionEndpoints.cs:30](../backend/src/Bms.Api/Endpoints/CompanyBudgetVersionEndpoints.cs#L30) |
| GET | `/api/v1/budget/company-versions/{id:guid}/history` | [CompanyBudgetVersionEndpoints.cs:22](../backend/src/Bms.Api/Endpoints/CompanyBudgetVersionEndpoints.cs#L22) |
| POST | `/api/v1/budget/company-versions/{id:guid}/revisions` | [CompanyBudgetVersionEndpoints.cs:24](../backend/src/Bms.Api/Endpoints/CompanyBudgetVersionEndpoints.cs#L24) |
| GET | `/api/v1/budget/company-versions/options` | [CompanyBudgetVersionEndpoints.cs:16](../backend/src/Bms.Api/Endpoints/CompanyBudgetVersionEndpoints.cs#L16) |
| GET | `/api/v1/budget/directory/cost-centers` | [BudgetDirectoryEndpoints.cs:22](../backend/src/Bms.Api/Endpoints/BudgetDirectoryEndpoints.cs#L22) |
| GET | `/api/v1/budget/directory/cost-centers/{id:guid}` | [BudgetDirectoryEndpoints.cs:26](../backend/src/Bms.Api/Endpoints/BudgetDirectoryEndpoints.cs#L26) |
| PUT | `/api/v1/budget/directory/cost-centers/{id:guid}` | [BudgetDirectoryEndpoints.cs:28](../backend/src/Bms.Api/Endpoints/BudgetDirectoryEndpoints.cs#L28) |
| PUT | `/api/v1/budget/directory/cost-centers/{id:guid}/purpose` | [BudgetDirectoryEndpoints.cs:31](../backend/src/Bms.Api/Endpoints/BudgetDirectoryEndpoints.cs#L31) |
| GET | `/api/v1/budget/directory/employees` | [BudgetDirectoryEndpoints.cs:24](../backend/src/Bms.Api/Endpoints/BudgetDirectoryEndpoints.cs#L24) |
| POST | `/api/v1/budget/expense-draft/preview` | [ExpenseDraftBudgetPreviewEndpoints.cs:14](../backend/src/Bms.Api/Endpoints/ExpenseDraftBudgetPreviewEndpoints.cs#L14) |
| GET | `/api/v1/budget/expense-requests/{requestId:guid}/explanation` | [RequestBudgetExplanationEndpoints442.cs:11](../backend/src/Bms.Api/Endpoints/RequestBudgetExplanationEndpoints442.cs#L11) |
| GET | `/api/v1/budget/instances` | [BudgetInstanceEndpoints.cs:26](../backend/src/Bms.Api/Endpoints/BudgetInstanceEndpoints.cs#L26) |
| POST | `/api/v1/budget/instances` | [BudgetInstanceEndpoints.cs:32](../backend/src/Bms.Api/Endpoints/BudgetInstanceEndpoints.cs#L32) |
| DELETE | `/api/v1/budget/instances/{id:guid}` | [BudgetInstanceEndpoints.cs:34](../backend/src/Bms.Api/Endpoints/BudgetInstanceEndpoints.cs#L34) |
| GET | `/api/v1/budget/instances/{id:guid}` | [BudgetInstanceEndpoints.cs:30](../backend/src/Bms.Api/Endpoints/BudgetInstanceEndpoints.cs#L30) |
| PUT | `/api/v1/budget/instances/{id:guid}` | [BudgetInstanceEndpoints.cs:33](../backend/src/Bms.Api/Endpoints/BudgetInstanceEndpoints.cs#L33) |
| GET | `/api/v1/budget/instances/{id:guid}/approval` | [CfoInitiatorEndpoints.cs:35](../backend/src/Bms.Api/Endpoints/CfoInitiatorEndpoints.cs#L35) |
| POST | `/api/v1/budget/instances/{id:guid}/approval-decisions` | [CfoInitiatorEndpoints.cs:40](../backend/src/Bms.Api/Endpoints/CfoInitiatorEndpoints.cs#L40) |
| POST | `/api/v1/budget/instances/{id:guid}/approve` | [BudgetInstanceEndpoints.cs:35](../backend/src/Bms.Api/Endpoints/BudgetInstanceEndpoints.cs#L35) |
| GET | `/api/v1/budget/instances/{id:guid}/history` | [BudgetInstanceEndpoints.cs:31](../backend/src/Bms.Api/Endpoints/BudgetInstanceEndpoints.cs#L31) |
| POST | `/api/v1/budget/instances/{id:guid}/submit` | [CfoInitiatorEndpoints.cs:37](../backend/src/Bms.Api/Endpoints/CfoInitiatorEndpoints.cs#L37) |
| GET | `/api/v1/budget/instances/cost-centers` | [BudgetInstanceEndpoints.cs:27](../backend/src/Bms.Api/Endpoints/BudgetInstanceEndpoints.cs#L27) |
| GET | `/api/v1/budget/instances/creation-options` | [BudgetInstanceEndpoints.cs:29](../backend/src/Bms.Api/Endpoints/BudgetInstanceEndpoints.cs#L29) |
| GET | `/api/v1/budget/instances/creator-cost-center` | [BudgetInstanceEndpoints.cs:28](../backend/src/Bms.Api/Endpoints/BudgetInstanceEndpoints.cs#L28) |
| GET | `/api/v1/budget/positions` | [BudgetEndpoints.cs:22](../backend/src/Bms.Api/Endpoints/BudgetEndpoints.cs#L22) |
| GET | `/api/v1/budget/positions/export.xlsx` | [BudgetEndpoints.cs:23](../backend/src/Bms.Api/Endpoints/BudgetEndpoints.cs#L23) |
| GET | `/api/v1/budget/positions/reservations` | [BudgetEndpoints.cs:25](../backend/src/Bms.Api/Endpoints/BudgetEndpoints.cs#L25) |
| GET | `/api/v1/budget/reports/bdds` | [BddsReportEndpoints.cs:16](../backend/src/Bms.Api/Endpoints/BddsReportEndpoints.cs#L16) |
| POST | `/api/v1/budget/reports/bdds/drill` | [BddsDrillEndpoints.cs:14](../backend/src/Bms.Api/Endpoints/BddsDrillEndpoints.cs#L14) |
| POST | `/api/v1/budget/reports/bdds/import` | [BddsReportEndpoints.cs:17](../backend/src/Bms.Api/Endpoints/BddsReportEndpoints.cs#L17) |
| GET | `/api/v1/budget/reports/unbudgeted-articles` | [UnbudgetedArticlesEndpoints.cs:13](../backend/src/Bms.Api/Endpoints/UnbudgetedArticlesEndpoints.cs#L13) |
| GET | `/api/v1/expense-requests/` | [ExpenseRequestEndpoints.cs:125](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L125) |
| POST | `/api/v1/expense-requests/` | [ExpenseRequestEndpoints.cs:40](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L40) |
| DELETE | `/api/v1/expense-requests/{id:guid}` | [ExpenseRequestEndpoints.cs:85](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L85) |
| GET | `/api/v1/expense-requests/{id:guid}` | [ExpenseRequestEndpoints.cs:124](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L124) |
| PUT | `/api/v1/expense-requests/{id:guid}` | [ExpenseRequestEndpoints.cs:69](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L69) |
| GET | `/api/v1/expense-requests/{id:guid}/approval-details` | [ExpenseRequestEndpoints.cs:71](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L71) |
| PUT | `/api/v1/expense-requests/{id:guid}/approval-details` | [ExpenseRequestEndpoints.cs:72](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L72) |
| GET | `/api/v1/expense-requests/{id:guid}/approval-details/idempotency` | [ExpenseRequestEndpoints.cs:73](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L73) |
| GET | `/api/v1/expense-requests/{id:guid}/approval-route` | [ExpenseRequestEndpoints.cs:20](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L20) |
| GET | `/api/v1/expense-requests/{id:guid}/attachments` | [ExpenseRequestEndpoints.cs:49](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L49) |
| POST | `/api/v1/expense-requests/{id:guid}/attachments` | [ExpenseRequestEndpoints.cs:51](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L51) |
| DELETE | `/api/v1/expense-requests/{id:guid}/attachments/{attachmentId:guid}` | [ExpenseRequestEndpoints.cs:64](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L64) |
| GET | `/api/v1/expense-requests/{id:guid}/attachments/{attachmentId:guid}/content` | [ExpenseRequestEndpoints.cs:59](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L59) |
| GET | `/api/v1/expense-requests/{id:guid}/attachments/{attachmentId:guid}/idempotency/delete` | [ExpenseRequestEndpoints.cs:67](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L67) |
| POST | `/api/v1/expense-requests/{id:guid}/attachments/{attachmentId:guid}/preview` | [ExpenseRequestEndpoints.cs:61](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L61) |
| GET | `/api/v1/expense-requests/{id:guid}/attachments/idempotency/add` | [ExpenseRequestEndpoints.cs:57](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L57) |
| GET | `/api/v1/expense-requests/{id:guid}/closing` | [ExpenseRequestEndpoints.cs:94](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L94) |
| GET | `/api/v1/expense-requests/{id:guid}/closing/{commandCode}/idempotency` | [ExpenseRequestEndpoints.cs:115](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L115) |
| POST | `/api/v1/expense-requests/{id:guid}/closing/full` | [ExpenseRequestEndpoints.cs:99](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L99) |
| POST | `/api/v1/expense-requests/{id:guid}/closing/partial` | [ExpenseRequestEndpoints.cs:96](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L96) |
| POST | `/api/v1/expense-requests/{id:guid}/closing/reopen` | [ExpenseRequestEndpoints.cs:102](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L102) |
| GET | `/api/v1/expense-requests/{id:guid}/commands` | [ExpenseRequestEndpoints.cs:89](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L89) |
| POST | `/api/v1/expense-requests/{id:guid}/commands/{commandCode}` | [ExpenseRequestEndpoints.cs:91](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L91) |
| GET | `/api/v1/expense-requests/{id:guid}/commands/{commandCode}/idempotency` | [ExpenseRequestEndpoints.cs:113](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L113) |
| GET | `/api/v1/expense-requests/{id:guid}/history` | [ExpenseRequestHistoryEndpoints.cs:12](../backend/src/Bms.Api/Endpoints/ExpenseRequestHistoryEndpoints.cs#L12) |
| GET | `/api/v1/expense-requests/{id:guid}/idempotency/delete-draft` | [ExpenseRequestEndpoints.cs:86](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L86) |
| GET | `/api/v1/expense-requests/{id:guid}/idempotency/submit` | [ExpenseRequestEndpoints.cs:111](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L111) |
| GET | `/api/v1/expense-requests/{id:guid}/idempotency/update-draft` | [ExpenseRequestEndpoints.cs:107](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L107) |
| GET | `/api/v1/expense-requests/{id:guid}/payment-details` | [ExpenseRequestEndpoints.cs:122](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L122) |
| GET | `/api/v1/expense-requests/{id:guid}/payment-summary` | [ExpenseRequestEndpoints.cs:120](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L120) |
| POST | `/api/v1/expense-requests/{id:guid}/refresh-currency` | [ExpenseRequestEndpoints.cs:74](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L74) |
| GET | `/api/v1/expense-requests/{id:guid}/submission-preflight` | [ExpenseRequestEndpoints.cs:117](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L117) |
| POST | `/api/v1/expense-requests/{id:guid}/submit` | [ExpenseRequestEndpoints.cs:87](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L87) |
| GET | `/api/v1/expense-requests/{id:guid}/treasury-payment-date` | [TreasuryPaymentDateEndpoints.cs:33](../backend/src/Bms.Api/Endpoints/TreasuryPaymentDateEndpoints.cs#L33) |
| PUT | `/api/v1/expense-requests/{id:guid}/treasury-payment-date` | [TreasuryPaymentDateEndpoints.cs:34](../backend/src/Bms.Api/Endpoints/TreasuryPaymentDateEndpoints.cs#L34) |
| POST | `/api/v1/expense-requests/{sourceId:guid}/copy` | [ExpenseRequestEndpoints.cs:76](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L76) |
| POST | `/api/v1/expense-requests/{sourceId:guid}/copy-complete` | [ExpenseRequestEndpoints.cs:78](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L78) |
| GET | `/api/v1/expense-requests/{sourceId:guid}/idempotency/copy-complete` | [ExpenseRequestEndpoints.cs:81](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L81) |
| GET | `/api/v1/expense-requests/{sourceId:guid}/idempotency/copy-draft` | [ExpenseRequestEndpoints.cs:109](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L109) |
| POST | `/api/v1/expense-requests/attachments/upload-intent` | [ExpenseRequestEndpoints.cs:46](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L46) |
| GET | `/api/v1/expense-requests/debtors/by-initiator` | [ExpenseRequestEndpoints.cs:18](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L18) |
| GET | `/api/v1/expense-requests/export.xlsx` | [ExpenseRequestEndpoints.cs:119](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L119) |
| GET | `/api/v1/expense-requests/idempotency/create-draft` | [ExpenseRequestEndpoints.cs:105](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L105) |
| GET | `/api/v1/expense-requests/initiators` | [ExpenseRequestEndpoints.cs:17](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L17) |
| POST | `/api/v1/expense-requests/number-reservation` | [ExpenseRequestEndpoints.cs:42](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L42) |
| POST | `/api/v1/expense-requests/number-reservation/release` | [ExpenseRequestEndpoints.cs:44](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L44) |
| GET | `/api/v1/expense-requests/payment-register` | [ExpenseRequestEndpoints.cs:19](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L19) |
| GET | `/api/v1/expense-requests/upload-sessions/{sessionId:guid}/attachments` | [ExpenseRequestEndpoints.cs:24](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L24) |
| POST | `/api/v1/expense-requests/upload-sessions/{sessionId:guid}/attachments` | [ExpenseRequestEndpoints.cs:25](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L25) |
| DELETE | `/api/v1/expense-requests/upload-sessions/{sessionId:guid}/attachments/{attachmentId:guid}` | [ExpenseRequestEndpoints.cs:30](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L30) |
| GET | `/api/v1/expense-requests/upload-sessions/{sessionId:guid}/attachments/{attachmentId:guid}/content` | [ExpenseRequestEndpoints.cs:32](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L32) |
| GET | `/api/v1/expense-requests/upload-sessions/{sessionId:guid}/attachments/{attachmentId:guid}/idempotency/delete` | [ExpenseRequestEndpoints.cs:38](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L38) |
| POST | `/api/v1/expense-requests/upload-sessions/{sessionId:guid}/attachments/{attachmentId:guid}/preview` | [ExpenseRequestEndpoints.cs:34](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L34) |
| GET | `/api/v1/expense-requests/upload-sessions/{sessionId:guid}/attachments/idempotency/add` | [ExpenseRequestEndpoints.cs:36](../backend/src/Bms.Api/Endpoints/ExpenseRequestEndpoints.cs#L36) |
| GET | `/api/v1/integration-monitor/accrual-notifications` | [AccrualNotificationEndpoints.cs:16](../backend/src/Bms.Api/Endpoints/AccrualNotificationEndpoints.cs#L16) |
| GET | `/api/v1/integration-monitor/database-readiness` | [IntegrationMonitorEndpoints.cs:17](../backend/src/Bms.Api/Endpoints/IntegrationMonitorEndpoints.cs#L17) |
| GET | `/api/v1/integration-monitor/outbox` | [IntegrationMonitorEndpoints.cs:15](../backend/src/Bms.Api/Endpoints/IntegrationMonitorEndpoints.cs#L15) |
| GET | `/api/v1/integration-monitor/outbox-summary` | [IntegrationMonitorEndpoints.cs:16](../backend/src/Bms.Api/Endpoints/IntegrationMonitorEndpoints.cs#L16) |
| GET | `/api/v1/integration-monitor/reference-projections` | [IntegrationMonitorEndpoints.cs:18](../backend/src/Bms.Api/Endpoints/IntegrationMonitorEndpoints.cs#L18) |
| GET | `/api/v1/management/accruals/active` | [AccrualEndpoints.cs:21](../backend/src/Bms.Api/Endpoints/AccrualEndpoints.cs#L21) |
| GET | `/api/v1/management/accruals/active/export.xlsx` | [AccrualEndpoints.cs:22](../backend/src/Bms.Api/Endpoints/AccrualEndpoints.cs#L22) |
| GET | `/api/v1/management/accruals/opiu` | [AccrualEndpoints.cs:24](../backend/src/Bms.Api/Endpoints/AccrualEndpoints.cs#L24) |
| GET | `/api/v1/management/accruals/opiu/details` | [AccrualEndpoints.cs:25](../backend/src/Bms.Api/Endpoints/AccrualEndpoints.cs#L25) |
| GET | `/api/v1/manual-deficit-corrections/{id:guid}/{operation}/idempotency` | [ManualDeficitEndpoints.cs:29](../backend/src/Bms.Api/Endpoints/ManualDeficitEndpoints.cs#L29) |
| POST | `/api/v1/manual-deficit-corrections/{id:guid}/approve` | [ManualDeficitEndpoints.cs:25](../backend/src/Bms.Api/Endpoints/ManualDeficitEndpoints.cs#L25) |
| POST | `/api/v1/manual-deficit-corrections/{id:guid}/reject` | [ManualDeficitEndpoints.cs:27](../backend/src/Bms.Api/Endpoints/ManualDeficitEndpoints.cs#L27) |
| GET | `/api/v1/my-tasks` | [MyApprovalTasks355Endpoints.cs:12](../backend/src/Bms.Api/Endpoints/MyApprovalTasks355Endpoints.cs#L12) |
| GET | `/api/v1/notifications/accruals` | [AccrualNotificationEndpoints.cs:14](../backend/src/Bms.Api/Endpoints/AccrualNotificationEndpoints.cs#L14) |
| GET | `/api/v1/notifications/budget-adjustments` | [MyApprovalTasks355Endpoints.cs:13](../backend/src/Bms.Api/Endpoints/MyApprovalTasks355Endpoints.cs#L13) |
| GET | `/api/v1/notifications/returned-requests` | [ReturnedRequestNotice377Endpoints.cs:12](../backend/src/Bms.Api/Endpoints/ReturnedRequestNotice377Endpoints.cs#L12) |
| GET | `/api/v1/overpayment-deficits` | [ManualDeficitEndpoints.cs:18](../backend/src/Bms.Api/Endpoints/ManualDeficitEndpoints.cs#L18) |
| GET | `/api/v1/overpayment-deficits/{id:guid}` | [ManualDeficitEndpoints.cs:19](../backend/src/Bms.Api/Endpoints/ManualDeficitEndpoints.cs#L19) |
| POST | `/api/v1/overpayment-deficits/{id:guid}/corrections` | [ManualDeficitEndpoints.cs:20](../backend/src/Bms.Api/Endpoints/ManualDeficitEndpoints.cs#L20) |
| GET | `/api/v1/overpayment-deficits/{id:guid}/corrections/idempotency` | [ManualDeficitEndpoints.cs:22](../backend/src/Bms.Api/Endpoints/ManualDeficitEndpoints.cs#L22) |
| GET | `/api/v1/reference-data/accrual-cost-centers` | [ReferenceDataEndpoints.cs:57](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L57) |
| GET | `/api/v1/reference-data/approval-matrix` | [ApprovalMatrixEndpoints.cs:23](../backend/src/Bms.Api/Endpoints/ApprovalMatrixEndpoints.cs#L23) |
| GET | `/api/v1/reference-data/approval-matrix/initiators` | [ApprovalMatrixEndpoints.cs:13](../backend/src/Bms.Api/Endpoints/ApprovalMatrixEndpoints.cs#L13) |
| GET | `/api/v1/reference-data/authorization-directory/active-ad-employees` | [ReferenceDataEndpoints.cs:89](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L89) |
| GET | `/api/v1/reference-data/authorization-directory/cfo-assignments` | [CfoInitiatorEndpoints.cs:23](../backend/src/Bms.Api/Endpoints/CfoInitiatorEndpoints.cs#L23) |
| GET | `/api/v1/reference-data/authorization-directory/employees` | [ReferenceDataEndpoints.cs:83](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L83) |
| POST | `/api/v1/reference-data/authorization-directory/employees` | [ReferenceDataEndpoints.cs:80](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L80) |
| GET | `/api/v1/reference-data/authorization-directory/employees/{id:guid}` | [ReferenceDataEndpoints.cs:85](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L85) |
| PUT | `/api/v1/reference-data/authorization-directory/employees/{id:guid}/actor-mapping` | [ReferenceDataEndpoints.cs:97](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L97) |
| GET | `/api/v1/reference-data/authorization-directory/employees/{id:guid}/cost-centers` | [CfoInitiatorEndpoints.cs:25](../backend/src/Bms.Api/Endpoints/CfoInitiatorEndpoints.cs#L25) |
| PUT | `/api/v1/reference-data/authorization-directory/employees/{id:guid}/cost-centers` | [CfoInitiatorEndpoints.cs:27](../backend/src/Bms.Api/Endpoints/CfoInitiatorEndpoints.cs#L27) |
| PUT | `/api/v1/reference-data/authorization-directory/employees/{id:guid}/roles` | [ReferenceDataEndpoints.cs:94](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L94) |
| POST | `/api/v1/reference-data/authorization-directory/employees/from-ad` | [ReferenceDataEndpoints.cs:91](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L91) |
| GET | `/api/v1/reference-data/authorization-directory/roles` | [ReferenceDataEndpoints.cs:87](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L87) |
| DELETE | `/api/v1/reference-data/budget-owner-cost-center-assignments` | [ReferenceDataEndpoints.cs:77](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L77) |
| GET | `/api/v1/reference-data/budget-owner-cost-center-assignments` | [ReferenceDataEndpoints.cs:70](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L70) |
| POST | `/api/v1/reference-data/budget-owner-cost-center-assignments` | [ReferenceDataEndpoints.cs:71](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L71) |
| PUT | `/api/v1/reference-data/budget-owner-cost-center-assignments` | [ReferenceDataEndpoints.cs:74](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L74) |
| GET | `/api/v1/reference-data/cash-flow-items` | [ReferenceDataEndpoints.cs:104](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L104) |
| GET | `/api/v1/reference-data/cash-flow-items/{id:guid}/accrual-settings` | [CashFlowAccrualSettingsEndpoints.cs:17](../backend/src/Bms.Api/Endpoints/CashFlowAccrualSettingsEndpoints.cs#L17) |
| PUT | `/api/v1/reference-data/cash-flow-items/{id:guid}/accrual-settings` | [CashFlowAccrualSettingsEndpoints.cs:18](../backend/src/Bms.Api/Endpoints/CashFlowAccrualSettingsEndpoints.cs#L18) |
| GET | `/api/v1/reference-data/cash-flow-items/groups` | [ReferenceDataEndpoints.cs:105](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L105) |
| GET | `/api/v1/reference-data/cash-flow-items/order` | [CashFlowCatalogOrderEndpoints.cs:19](../backend/src/Bms.Api/Endpoints/CashFlowCatalogOrderEndpoints.cs#L19) |
| POST | `/api/v1/reference-data/cash-flow-items/order/move` | [CashFlowCatalogOrderEndpoints.cs:20](../backend/src/Bms.Api/Endpoints/CashFlowCatalogOrderEndpoints.cs#L20) |
| GET | `/api/v1/reference-data/cash-flow-items/query` | [ReferenceDataEndpoints.cs:106](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L106) |
| GET | `/api/v1/reference-data/cash-flow-items/resolve` | [ReferenceDataEndpoints.cs:107](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L107) |
| GET | `/api/v1/reference-data/contracts` | [ReferenceDataEndpoints.cs:113](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L113) |
| GET | `/api/v1/reference-data/cost-centers` | [ReferenceDataEndpoints.cs:43](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L43) |
| GET | `/api/v1/reference-data/cost-centers/{id:guid}/editor` | [CfoInitiatorEndpoints.cs:18](../backend/src/Bms.Api/Endpoints/CfoInitiatorEndpoints.cs#L18) |
| PUT | `/api/v1/reference-data/cost-centers/{id:guid}/editor` | [CfoInitiatorEndpoints.cs:20](../backend/src/Bms.Api/Endpoints/CfoInitiatorEndpoints.cs#L20) |
| POST | `/api/v1/reference-data/cost-centers/editor` | [CfoInitiatorEndpoints.cs:15](../backend/src/Bms.Api/Endpoints/CfoInitiatorEndpoints.cs#L15) |
| GET | `/api/v1/reference-data/cost-departments` | [ReferenceDataEndpoints.cs:44](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L44) |
| GET | `/api/v1/reference-data/counterparties` | [ReferenceDataEndpoints.cs:112](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L112) |
| GET | `/api/v1/reference-data/counterparty-residencies` | [ReferenceDataEndpoints.cs:102](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L102) |
| GET | `/api/v1/reference-data/currencies` | [ReferenceDataEndpoints.cs:114](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L114) |
| GET | `/api/v1/reference-data/currency-rates/` | [CurrencyRateEndpoints.cs:26](../backend/src/Bms.Api/Endpoints/CurrencyRateEndpoints.cs#L26) |
| GET | `/api/v1/reference-data/currency-rates/expense-draft-defaults` | [CurrencyRateEndpoints.cs:28](../backend/src/Bms.Api/Endpoints/CurrencyRateEndpoints.cs#L28) |
| POST | `/api/v1/reference-data/currency-rates/import` | [CurrencyRateEndpoints.cs:35](../backend/src/Bms.Api/Endpoints/CurrencyRateEndpoints.cs#L35) |
| PUT | `/api/v1/reference-data/currency-rates/manual` | [CurrencyRateEndpoints.cs:30](../backend/src/Bms.Api/Endpoints/CurrencyRateEndpoints.cs#L30) |
| GET | `/api/v1/reference-data/expense-items` | [ReferenceDataEndpoints.cs:108](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L108) |
| POST | `/api/v1/reference-data/expense-items` | [ReferenceDataEndpoints.cs:109](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L109) |
| POST | `/api/v1/reference-data/maintenance/{directoryId}` | [ReferenceDataEndpoints.cs:115](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L115) |
| DELETE | `/api/v1/reference-data/maintenance/{directoryId}/{id:guid}` | [ReferenceDataEndpoints.cs:121](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L121) |
| PUT | `/api/v1/reference-data/maintenance/{directoryId}/{id:guid}` | [ReferenceDataEndpoints.cs:118](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L118) |
| GET | `/api/v1/reference-data/organizations` | [ReferenceDataEndpoints.cs:36](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L36) |
| POST | `/api/v1/reference-data/organizations` | [ReferenceDataEndpoints.cs:37](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L37) |
| PUT | `/api/v1/reference-data/organizations/{id:guid}` | [ReferenceDataEndpoints.cs:40](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L40) |
| GET | `/api/v1/reference-data/payment-bases` | [ReferenceDataEndpoints.cs:101](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L101) |
| GET | `/api/v1/reference-data/payment-operation-options` | [ReferenceDataEndpoints.cs:19](../backend/src/Bms.Api/Endpoints/ReferenceDataEndpoints.cs#L19) |
| GET | `/api/v1/session` | [SessionEndpoints.cs:17](../backend/src/Bms.Api/Endpoints/SessionEndpoints.cs#L17) |
| GET | `/api/v1/staff-substitutions` | [StaffSubstitution471Endpoints.cs:21](../backend/src/Bms.Api/Endpoints/StaffSubstitution471Endpoints.cs#L21) |
| GET | `/api/v1/staff-substitutions/{id:guid}` | [StaffSubstitution471Endpoints.cs:23](../backend/src/Bms.Api/Endpoints/StaffSubstitution471Endpoints.cs#L23) |
| POST | `/api/v1/staff-substitutions/{id:guid}/{action}` | [StaffSubstitution471Endpoints.cs:27](../backend/src/Bms.Api/Endpoints/StaffSubstitution471Endpoints.cs#L27) |
| GET | `/api/v1/staff-substitutions/{id:guid}/actions` | [StaffSubstitution471Endpoints.cs:25](../backend/src/Bms.Api/Endpoints/StaffSubstitution471Endpoints.cs#L25) |
| GET | `/api/v1/staff-substitutions/news` | [StaffSubstitution471Endpoints.cs:17](../backend/src/Bms.Api/Endpoints/StaffSubstitution471Endpoints.cs#L17) |
| GET | `/api/v1/staff-substitutions/options` | [StaffSubstitution471Endpoints.cs:15](../backend/src/Bms.Api/Endpoints/StaffSubstitution471Endpoints.cs#L15) |
| GET | `/api/v1/staff-substitutions/pending` | [StaffSubstitution471Endpoints.cs:19](../backend/src/Bms.Api/Endpoints/StaffSubstitution471Endpoints.cs#L19) |
| GET | `/api/v1/treasury/goods-calendar/grid` | [GoodsCalendarEndpoints458.cs:13](../backend/src/Bms.Api/Endpoints/GoodsCalendarEndpoints458.cs#L13) |
| GET | `/api/v1/treasury/goods-calendar/grid/export.xlsx` | [GoodsCalendarEndpoints458.cs:16](../backend/src/Bms.Api/Endpoints/GoodsCalendarEndpoints458.cs#L16) |
| GET | `/api/v1/treasury/payment-calendar` | [TreasuryEndpoints.cs:19](../backend/src/Bms.Api/Endpoints/TreasuryEndpoints.cs#L19) |
| GET | `/api/v1/treasury/payment-calendar/dated` | [DatedTreasuryEndpoints.cs:19](../backend/src/Bms.Api/Endpoints/DatedTreasuryEndpoints.cs#L19) |
| GET | `/api/v1/treasury/payment-calendar/dated/export.xlsx` | [DatedTreasuryEndpoints.cs:21](../backend/src/Bms.Api/Endpoints/DatedTreasuryEndpoints.cs#L21) |
| GET | `/api/v1/treasury/payment-calendar/export.xlsx` | [TreasuryEndpoints.cs:21](../backend/src/Bms.Api/Endpoints/TreasuryEndpoints.cs#L21) |
| GET | `/api/v1/treasury/payment-calendar/grid` | [DatedTreasuryEndpoints.cs:23](../backend/src/Bms.Api/Endpoints/DatedTreasuryEndpoints.cs#L23) |
| GET | `/api/v1/treasury/payment-calendar/grid/export.xlsx` | [DatedTreasuryEndpoints.cs:24](../backend/src/Bms.Api/Endpoints/DatedTreasuryEndpoints.cs#L24) |

## Префиксы для ручной проверки

Не обнаружены для выбранного шаблона извлечения.
