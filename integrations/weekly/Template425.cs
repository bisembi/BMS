using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bms.WeeklyPrimary425;

public sealed record OwnerReminder425(
    [property: JsonPropertyName("employeeId")] Guid RecipientOwnerId,
    [property: JsonPropertyName("ownerDisplayName")] string RecipientFullName,
    DateOnly AsOfDate,
    IReadOnlyList<ReminderRow425> Rows);

public sealed record ReminderRow425(
    Guid DocumentId, string DocumentKey, string Number, DateOnly PaymentDate, string Party,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] decimal RequestAmount,
    string Currency, string AmountKind, string ClosureState, Guid InitiatorId, string InitiatorName,
    IReadOnlyList<Guid> CostCenterOwnerIds, IReadOnlyList<CashFlowArticle425> CashFlowArticles,
    VerifiedLink425? VerifiedLink = null);

public sealed record CashFlowArticle425(Guid Id, string Name);
public sealed record VerifiedLink425(bool Verified, string Url, string Evidence);
public sealed record RenderedReminder425(string Subject, string Html, string Text, int RowCount);

// Pure content renderer. No source queries, transport, addresses, scheduling or workflow mutations.
public static class PrimaryDocumentsTemplate425
{
    public const int MaxRows = 200;
    public const int MaxBodyBytes = 128 * 1024;
    public const int MaxSubjectChars = 256;
    public const int MaxSubjectBytes = 1024;
    private const string Title = "Первичные документы по оплатам";
    private const string Subject = "BMS — первичные документы по оплатам";
    private const string ActionText = "Пожалуйста, передайте закрывающие первичные документы в бухгалтерию, указав номер заявки. Если документы уже переданы, попросите бухгалтерию отразить закрытие в BMS.";
    private const string AmountNote = "Сумма заявки — исходная сумма в её валюте, не сумма банковской оплаты и не остаток долга. Значения показаны целыми; разные валюты не суммируются.";

    public static RenderedReminder425? Render(OwnerReminder425 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Require(input.RecipientOwnerId != Guid.Empty, "OWNER_ID_REQUIRED");
        var fullName = Line(input.RecipientFullName, "OWNER_NAME", 200);
        Require(input.AsOfDate != default, "LOCAL_ASOF_DATE_REQUIRED");
        Require(input.Rows is not null && input.Rows.Count <= MaxRows, "OWNER_ROWS_BOUND");
        if (input.Rows.Count == 0) return null;

        var unique = new Dictionary<Guid, (Row Value, string Signature)>();
        foreach (var source in input.Rows)
        {
            var row = Normalize(source, input.RecipientOwnerId, input.AsOfDate);
            var signature = JsonSerializer.Serialize(new
            {
                row.DocumentId, row.Number, row.PaymentDate, row.Party, row.RequestAmount, row.Currency,
                row.ClosureState, row.InitiatorId, row.InitiatorName, row.VerifiedUrl,
                Owners = row.OwnerIds.OrderBy(x => x).ToArray(),
                Articles = row.Articles.OrderBy(x => x.Id).ToArray()
            });
            if (unique.TryGetValue(row.DocumentId, out var prior))
                Require(prior.Signature == signature, "CONFLICTING_DUPLICATE_REQUEST");
            else unique.Add(row.DocumentId, (row, signature));
        }
        var rows = unique.Values.Select(x => x.Value).ToArray();
        var scopeText = rows.Any(x => x.ClosureState == "partially-recorded")
            ? "Оплаченные заявки, полное закрытие которых не отражено в BMS."
            : "Оплаченные заявки, закрытие которых не отражено в BMS.";
        var columns = new (string Label, string Width)[]
        {
            ("Заявка","20%"),("Дата оплаты","12%"),("Дней от оплаты","10%"),
            ("Статья ДДС","20%"),("Получатель","22%"),("Сумма заявки","16%")
        };
        var header = string.Concat(columns.Select(c =>
            $"""<th scope="col" width="{c.Width}" align="left" style="width:{c.Width};padding:11px 10px;border-bottom:1px solid #d8dde5;background:#f3f4f6;color:#4b5563;font-size:11px;line-height:15px;font-weight:600;text-align:left;">{H(c.Label)}</th>"""));
        var body = new StringBuilder();
        var plain = new StringBuilder()
            .AppendLine(Title).AppendLine()
            .Append(fullName).AppendLine(", добрый день.")
            .AppendLine(scopeText).Append("По состоянию на ").AppendLine(FormatDate(input.AsOfDate));

        for (var index = 0; index < rows.Length; index++)
        {
            var row = rows[index];
            var initiator = row.InitiatorId != input.RecipientOwnerId
                ? $"""<br><span style="font-size:11px;line-height:15px;color:#6b7280;">Инициатор: {H(row.InitiatorName)}</span>""" : "";
            var closure = row.ClosureState == "partially-recorded"
                ? """<br><span style="font-size:11px;line-height:15px;color:#6b7280;">Закрытие отражено частично</span>""" : "";
            var link = row.VerifiedUrl is not null
                ? $"""<br><a href="{H(row.VerifiedUrl)}" style="color:#8e1938;text-decoration:underline;font-size:11px;">Открыть в BMS</a>""" : "";
            var daysLabel = "Дней от оплаты: " + row.Days + (row.Days > 10 ? ", более 10 дней" : "");
            var color = row.Days > 10 ? "#c62828" : "#202938";
            var days = $"""<span class="days-value" data-days="{row.Days}" aria-label="{H(daysLabel)}" style="font-weight:600;color:{color};">{row.Days}</span>""";
            var articles = string.Join("<br>", row.Articles.Select(a => $"""<span class="article-name">{H(a.Name)}</span>"""));
            body.Append("""<tr class="data-row">""")
                .Append(Cell("Заявка", $"""<strong style="font-weight:600;">{H(row.Number)}</strong>""" + initiator + closure + link, "20%"))
                .Append(Cell("Дата оплаты", FormatDate(row.PaymentDate), "12%"))
                .Append(Cell("Дней от оплаты", days, "10%"))
                .Append(Cell("Статья ДДС", articles, "20%"))
                .Append(Cell("Получатель", H(row.Party), "22%"))
                .Append(Cell("Сумма заявки", $"""<strong style="font-weight:600;">{H(FormatAmount(row.RequestAmount, row.Currency))}</strong>""", "16%", "right"))
                .Append("</tr>");
            plain.AppendLine().Append(index + 1).Append(". ").Append(row.Number)
                .Append(" · дата оплаты ").AppendLine(FormatDate(row.PaymentDate))
                .Append("Дней от оплаты: ").Append(row.Days).AppendLine(row.Days > 10 ? " (более 10 дней)" : "")
                .Append("Статья ДДС: ").AppendLine(string.Join("; ", row.Articles.Select(a => a.Name)))
                .Append("Получатель: ").AppendLine(row.Party)
                .Append("Сумма заявки: ").AppendLine(FormatAmount(row.RequestAmount, row.Currency));
            if (row.InitiatorId != input.RecipientOwnerId) plain.Append("Инициатор: ").AppendLine(row.InitiatorName);
            if (row.ClosureState == "partially-recorded")
                plain.AppendLine("Закрытие отражено частично; указана полная сумма заявки, не остаток.");
            if (row.VerifiedUrl is not null) plain.Append("Открыть в BMS: ").AppendLine(row.VerifiedUrl);
        }
        plain.AppendLine().AppendLine(ActionText).AppendLine().Append(AmountNote);
        var html = $$"""
<!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{{H(Subject)}}</title><style type="text/css">@media only screen and (max-width:600px){.shell-padding{padding:12px!important}.content-padding{padding:22px 18px!important}.data-table,.data-table tbody,.data-row,.data-cell{display:block!important;width:100%!important;box-sizing:border-box!important}.data-table thead{display:none!important}.data-row{padding:10px 0!important;border-bottom:1px solid #d8dde5!important}.data-cell{padding:5px 0!important;border:0!important;text-align:left!important}.mobile-label{display:block!important;line-height:16px!important}.heading{font-size:23px!important;line-height:29px!important} }</style></head><body style="margin:0;padding:0;background:#f1f3f5;color:#202938;font-family:Arial,Helvetica,sans-serif;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="width:100%;border-collapse:collapse;background:#f1f3f5;"><tr><td class="shell-padding" align="center" style="padding:28px 16px;"><!--[if mso]><table role="presentation" width="880" cellpadding="0" cellspacing="0" border="0"><tr><td><![endif]--><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="width:100%;max-width:880px;border-collapse:collapse;background:#ffffff;border-top:4px solid #8e1938;"><tr><td class="content-padding" style="padding:28px 30px;"><p style="margin:0 0 18px;color:#8e1938;font-size:11px;line-height:17px;font-weight:700;letter-spacing:0.5px;">BMS</p><h1 class="heading" style="margin:0 0 18px;font-size:27px;line-height:34px;font-weight:600;color:#202938;">{{Title}}</h1><p style="margin:0 0 10px;font-size:15px;line-height:23px;">{{H(fullName)}}, добрый день.</p><p style="margin:0 0 18px;font-size:14px;line-height:22px;">{{H(scopeText)}}</p><p style="margin:0 0 14px;color:#6b7280;font-size:12px;line-height:18px;">По состоянию на {{FormatDate(input.AsOfDate)}}</p><table class="data-table" width="100%" cellpadding="0" cellspacing="0" border="0" style="width:100%;border-collapse:collapse;table-layout:fixed;"><thead><tr>{{header}}</tr></thead><tbody>{{body}}</tbody></table><p style="margin:18px 0 0;font-size:14px;line-height:22px;">{{H(ActionText)}}</p><p style="margin:12px 0 0;color:#6b7280;font-size:11px;line-height:16px;">{{H(AmountNote)}}</p></td></tr></table><!--[if mso]></td></tr></table><![endif]--></td></tr></table></body></html>
""";
        var text = plain.ToString().Replace("\r\n", "\n");
        Require(Subject.Length <= MaxSubjectChars && Encoding.UTF8.GetByteCount(Subject) <= MaxSubjectBytes, "SUBJECT_BOUND");
        Require(Encoding.UTF8.GetByteCount(html) <= MaxBodyBytes && Encoding.UTF8.GetByteCount(text) <= MaxBodyBytes, "OWNER_BODY_BOUND");
        return new RenderedReminder425(Subject, html, text, rows.Length);
    }

    public static int CalendarDaysSincePayment(DateOnly paymentDate, DateOnly asOfDate)
    {
        Require(paymentDate != default && asOfDate != default, "CALENDAR_DATE_REQUIRED");
        var result = asOfDate.DayNumber - paymentDate.DayNumber;
        Require(result >= 0, "FUTURE_PAYMENT_DATE");
        return result;
    }
    public static string FormatDate(DateOnly value) => value.ToString("dd.MM.yy", CultureInfo.InvariantCulture);
    public static string FormatAmount(decimal amount, string currency)
    {
        Require(amount >= 0, "NEGATIVE_REQUEST_AMOUNT");
        Require(currency is not null && currency.Length == 3 && currency.All(c => c is >= 'A' and <= 'Z'), "INVALID_CURRENCY");
        return decimal.Round(amount, 0, MidpointRounding.AwayFromZero)
            .ToString("N0", CultureInfo.GetCultureInfo("ru-RU")).Replace('\u00A0', ' ').Replace('\u202F', ' ') + " " + currency;
    }

    private static Row Normalize(ReminderRow425 source, Guid recipient, DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(source);
        Require(source.DocumentId != Guid.Empty && Guid.TryParse(source.DocumentKey, out var key) && key == source.DocumentId, "REQUEST_IDENTITY");
        Require(source.AmountKind == "requestAmount", "ORIGINAL_REQUEST_AMOUNT_REQUIRED");
        Require(source.ClosureState is "not-recorded" or "partially-recorded", "CLOSURE_STATE_REQUIRED");
        Require(source.InitiatorId != Guid.Empty, "INITIATOR_ID_REQUIRED");
        Require(source.CostCenterOwnerIds is not null && source.CostCenterOwnerIds.Count > 0
            && source.CostCenterOwnerIds.All(id => id != Guid.Empty), "AUTHORITATIVE_OWNER_SET_REQUIRED");
        var owners = source.CostCenterOwnerIds.Distinct().ToArray();
        // Legitimate co-owners are allowed. Author membership never grants a recipient scope.
        Require(owners.Contains(recipient), "RECIPIENT_IS_NOT_OWNER");
        Require(source.CashFlowArticles is not null && source.CashFlowArticles.Count is > 0 and <= 100, "ARTICLE_SET_REQUIRED");
        var articles = new Dictionary<Guid, string>();
        foreach (var article in source.CashFlowArticles)
        {
            Require(article is not null && article.Id != Guid.Empty, "ARTICLE_ID_REQUIRED");
            Line(article.Name, "ARTICLE_NAME", 400);
            if (articles.TryGetValue(article.Id, out var prior)) Require(prior == article.Name, "CONFLICTING_ARTICLE_NAME");
            else articles.Add(article.Id, article.Name);
        }
        FormatAmount(source.RequestAmount, source.Currency);
        string? url = null;
        if (source.VerifiedLink?.Verified == true)
        {
            Line(source.VerifiedLink.Evidence, "LINK_EVIDENCE", 300);
            var raw = Line(source.VerifiedLink.Url, "VERIFIED_URL", 400);
            Require(Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme == "https"
                && uri.Host.Equals("bms.directory.example.invalid", StringComparison.OrdinalIgnoreCase)
                && uri.Port == 443 && uri.AbsolutePath == "/" && uri.Fragment.Length == 0
                && uri.UserInfo.Length == 0, "INVALID_DOCUMENT_LINK");
            var query = uri.Query;
            const string prefix = "?expenseRequest=";
            Require(query.StartsWith(prefix, StringComparison.Ordinal) && !query.Contains('&')
                && Guid.TryParse(Uri.UnescapeDataString(query[prefix.Length..]), out var linkId)
                && linkId == source.DocumentId, "LINK_REQUEST_MISMATCH");
            url = uri.AbsoluteUri;
        }
        return new Row(source.DocumentId, Line(source.Number, "NUMBER", 70), source.PaymentDate,
            Line(source.Party, "PARTY", 6400), source.RequestAmount, source.Currency, source.ClosureState,
            source.InitiatorId, Line(source.InitiatorName, "INITIATOR_NAME", 140), owners,
            articles.Select(x => new CashFlowArticle425(x.Key, x.Value)).ToArray(), url,
            CalendarDaysSincePayment(source.PaymentDate, asOf));
    }
    private static string Cell(string label, string value, string width, string align = "left") =>
        $"""<td class="data-cell" width="{width}" valign="top" style="width:{width};padding:13px 10px;border-bottom:1px solid #e5e7eb;vertical-align:top;text-align:{align};font-size:13px;line-height:19px;word-wrap:break-word;word-break:break-word;overflow-wrap:anywhere;"><span class="mobile-label" style="display:none;mso-hide:all;font-size:11px;color:#6b7280;">{H(label)}</span>{value}</td>""";
    private static string H(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static string Line(string? value, string field, int max)
    {
        Require(!string.IsNullOrWhiteSpace(value) && value.Length <= max
            && !value.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029'), field);
        return value.Trim();
    }
    private static void Require([DoesNotReturnIf(false)] bool ok, string code)
    {
        if (!ok) throw new InvalidOperationException("TEMPLATE425_" + code);
    }
    private sealed record Row(Guid DocumentId, string Number, DateOnly PaymentDate, string Party,
        decimal RequestAmount, string Currency, string ClosureState, Guid InitiatorId, string InitiatorName,
        IReadOnlyList<Guid> OwnerIds, IReadOnlyList<CashFlowArticle425> Articles, string? VerifiedUrl, int Days);
}

