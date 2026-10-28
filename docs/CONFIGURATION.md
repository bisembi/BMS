# Конфигурация новой установки

Значения здесь описывают **состав кандидата**, не параметры действующей компании.
Полный источник: [appsettings](../backend/src/Bms.Api/appsettings.json),
[регистрация зависимостей](../backend/src/Bms.Infrastructure/DependencyInjection.cs),
[host](../backend/src/Bms.Api/BmsApiHost.cs). Не храните реальные переопределения в Git.

## Вход, адреса и SQL

| Ключ | Начальное поведение / требование |
| --- | --- |
| `ConnectionStrings__BmsDatabase` | Пусто в поставке; задать защищённым способом для собственной основной БД |
| `BmsSecurity__WindowsAuthentication__Enabled` | true; не заменять псевдопользователем |
| `BmsSecurity__AllowDevelopmentActorHeaders` | false; не включать для обхода AD |
| `BmsSecurity__Csrf__IssuesToken` / `RequiresToken` | true / true; для защищённых записей |
| `BmsSecurity__ForwardedHeaders__Enabled` | false; прокси требует отдельного проверенного контракта |
| `AllowedHosts` | localhost;127.0.0.1; для рабочей установки настроить свои допустимые hostnames |
| `BmsWeb__LocalFrontendRoot` | Абсолютный локальный каталог сборки UI с index.html и assets |
| `BmsWeb__MaintenanceReadOnly` | true; оставить до приёмки |
| `BMS_REPLICA_DATABASE` | По умолчанию BmsReplica, строгий SQL-идентификатор, тот же SQL Server |
| `BMS_PUBLIC_BASE_URL` | HTTPS-origin без credentials, query и fragment |

Двойное подчёркивание — стандартная запись вложенного ключа через окружение
.NET. Параметры `BMS_*` читаются отдельным контрактом установки, а не являются
произвольным JSON разделом. Используйте точное имя ключа.

## Рабочие функции

| Ключ | Значение в исходной поставке / важная граница |
| --- | --- |
| `Bms__BudgetAdjustments__WriteEnabled` | true, но записи всё равно закрыты maintenance и полномочиями |
| `Bms__BusinessTimeZoneId` | Asia/Qyzylorda |
| `Bms__Reporting__BusinessTimeZoneId` | Asia/Qyzylorda |
| `Bms__CurrencyRates__MaximumAgeDays` | 0; трактовку срока читать в политике курсов, не угадывать |
| `Bms__CurrencyRates__NationalBankImportEnabled` | false |
| `Bms__AccountingPayments__BudgetControlEnabled` | false |
| `Bms__AccountingPayments__ReconciliationEnabled` | false; требует source-aware budget control |
| `Bms__AccountingPayments__ReschedulingEnabled` | false; требует source-aware budget control |
| `Bms__TaskMail__CaptureEnabled` | false |
| `Bms__DefaultBusinessRoles` | Пусто; нет назначений сотрудников прежней компании |
| `Bms__ActiveAccruals__YellowFromDays` / `OrangeFromDays` / `RedFromDays` | 8 / 15 / 31, визуальные зоны, не утверждённый норматив |
| `BmsAccrualEditing__Enabled` | Кодом по умолчанию true, отдельная политика аналитик |
| `Bms__PayrollAdvance__CreationEnabled` | Кодом по умолчанию true, не замена прав пользователя |

`Bms__StaffSubstitution471__Enabled` используется host для включения
дополнительной readiness-проверки схемы замещений. Не трактуйте его как
единственную защиту всех действий замещения: поведение раздела и сервисов
нужно проверить на целевой установке.

## Почта и файлы

`BMS_MAIL_DOMAIN` задаёт разрешённый DNS-домен, `BMS_MAIL_SETTINGS_PATH` —
абсолютный локальный путь к защищённому SMTP JSON. UNC запрещён. Проверяются
ACL, родители, reparse points, TLS и адресаты. Пароль находится только в
защищённом секрете целевого окружения, не в репозитории и не в CLI-аргументах.

Для вложений нужен приватный storage и действующий контракт сканера
`Bms:Attachments:Scanner`. Все конкретные параметры проверить по исходникам
провайдера при настройке; нельзя выдумывать отсутствующее имя env-key
или подставить «сканер всегда чисто». Поведение при недоступности — отказ,
а не разрешение.

## Контроль после изменения настройки

Зафиксировать безопасную копию старой конфигурации, цель и затронутый
процесс → применить в тестовом окружении → проверить запуск и отрицательные
сценарии → при записи проверить SQL/audit/повтор → при источнике проверить
сверку → при почте проверить получателя и журнал → только потом активировать.
Не публиковать содержимое приватного конфига как доказательство.
