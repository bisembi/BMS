using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Bms.Infrastructure.Mail;

public static class TaskMailDocumentKinds
{
    public const string ExpenseRequest = "ExpenseRequest";
    public const string BudgetInstance = "BudgetInstance";
    public const string BudgetAdjustment = "BudgetAdjustment";
    public const string StaffSubstitution = "StaffSubstitution";
}

public sealed record TaskMailTask(Guid RequestId, Guid EmployeeId, string Role, string Epoch,
    string ActorExternalId, string EmployeeName, string Number, string Organization, string CostCenter,
    decimal Amount, string Currency, string? Purpose, string? ReturnReason = null, DateTimeOffset? ReturnedAtUtc = null,
    string DocumentKind = TaskMailDocumentKinds.ExpenseRequest)
{
    // Preserve every existing expense message ID: upgrading must not resend it.
    public Guid MailId => new(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{(DocumentKind == TaskMailDocumentKinds.ExpenseRequest ? "bms-task-mail-v1" : DocumentKind == TaskMailDocumentKinds.BudgetAdjustment ? "bms-budget-adjustment-task-mail-v1" : DocumentKind == TaskMailDocumentKinds.StaffSubstitution ? "bms-staff-substitution-mail-v1" : "bms-budget-task-mail-v1")}|{RequestId:N}|{EmployeeId:N}|{Role}|{Epoch}"))[..16]);
}
public sealed record TaskMailEnvelope(Guid RequestId, Guid EmployeeId, string Role, string Epoch,
    string Number, string State, DateTimeOffset? NextAttemptUtc = null,
    string DocumentKind = TaskMailDocumentKinds.ExpenseRequest)
{
    public static TaskMailEnvelope From(TaskMailTask t) => new(t.RequestId,t.EmployeeId,t.Role,t.Epoch,t.Number,"Queued",DocumentKind:t.DocumentKind);
    public bool Matches(TaskMailTask t) => RequestId==t.RequestId && EmployeeId==t.EmployeeId && Role==t.Role && Epoch==t.Epoch && DocumentKind==t.DocumentKind;
}
public sealed record TaskMailDelivery(bool Enabled, string? Host = null, int Port = 587, string Tls = "StartTls",
    string? From = null, string? UserName = null, string? Password = null, string? CertificateSha256 = null)
{
    public static TaskMailDelivery Parse(string json)
    {
        var value=JsonSerializer.Deserialize<TaskMailDelivery>(json,new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow}) ?? throw new InvalidOperationException("Mail configuration is missing.");
        if(!value.Enabled)return value;
        if(string.IsNullOrWhiteSpace(value.Host)||value.Host.Length>253||value.Host.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='.'&&c!='-')
            ||value.Port is <1 or >65535||value.Tls is not("StartTls" or "SslOnConnect")
            ||!ValidAddress(value.From)||string.IsNullOrEmpty(value.UserName)!=string.IsNullOrEmpty(value.Password)
            ||(value.CertificateSha256 is not null && (value.CertificateSha256.Length!=64||!value.CertificateSha256.All(char.IsAsciiHexDigit))))
            throw new InvalidOperationException("Invalid protected SMTP configuration.");
        return value;
    }
    public static bool ValidAddress(string? text) => text is not null && text.Length<=254 && !text.Any(char.IsControl)
        && MailboxAddress.TryParse(text,out var address) && address is not null && string.IsNullOrEmpty(address.Name) && address.Address==text
        && text.EndsWith("@"+BmsInstallationSettings.MailDomain,StringComparison.OrdinalIgnoreCase);
}
public interface ITaskMailRecipientDirectory { Task<string> ResolveMailAsync(string actorExternalId,CancellationToken token); }
public interface ITaskMailTransport { Task SendAsync(TaskMailTask task,string recipient,TaskMailDelivery delivery,CancellationToken token); }
public sealed class TaskMailDeliveryException(bool uncertain) : Exception { public bool Uncertain {get;}=uncertain; }

public static class TaskMailTemplate
{
    public static MimeMessage Build(TaskMailTask task,string recipient,string from)
    {
        if(task.DocumentKind==TaskMailDocumentKinds.StaffSubstitution)
            return BuildStaff471(task,recipient,from);
        if(task.Role is not("NSI_SPECIALIST" or "BUDGET_OWNER" or "FIN_CONTROLLER" or "FIN_DIRECTOR" or "GENERAL_DIRECTOR" or "TREASURER" or "PAYROLL_CALCULATOR" or "REQUEST_AUTHOR") || !TaskMailDelivery.ValidAddress(recipient)
            ||!TaskMailDelivery.ValidAddress(from)||string.IsNullOrWhiteSpace(task.Number)||task.Number.Any(char.IsControl))
            throw new InvalidOperationException("Invalid task mail identity.");
        if(task.DocumentKind==TaskMailDocumentKinds.BudgetAdjustment)
            return BuildAdjustment448(task,recipient,from);
        if(task.DocumentKind==TaskMailDocumentKinds.BudgetInstance)
            return BuildBudget(task,recipient,from);
        if(task.DocumentKind!=TaskMailDocumentKinds.ExpenseRequest)
            throw new InvalidOperationException("Unknown task mail document kind.");
        var action=task.Role switch
        {
            "REQUEST_AUTHOR"=>"доработать заявку",
            "NSI_SPECIALIST"=>"подтвердить НСИ",
            "TREASURER"=>"передать заявку в оплату",
            "GENERAL_DIRECTOR"=>"утвердить заявку",
            _=>"согласовать заявку"
        };
        var url=BmsInstallationSettings.PublicBaseUri.AbsoluteUri+"?expenseRequest="+task.RequestId.ToString("D");
        var returned = task.Role == "REQUEST_AUTHOR";
        var subject=returned ? $"BMS: расходная заявка {task.Number} возвращена на доработку"
            : $"BMS: расходная заявка {task.Number} — необходимо {action}";
        var amount=task.Amount.ToString("N0",CultureInfo.GetCultureInfo("ru-RU"))+" "+task.Currency;
        var reason=returned ? "\nПричина возврата: "+(task.ReturnReason??"Смотрите историю заявки.") : "";
        var introduction=returned ? $"{task.EmployeeName}, расходная заявка {task.Number} возвращена вам на доработку."
            : $"{task.EmployeeName}, на ваш этап поступила расходная заявка {task.Number}.";
        var text=$"{introduction}\nНеобходимо {action}.{reason}\nОрганизация: {task.Organization}\nЦФО: {task.CostCenter}\nСумма: {amount}\nНазначение: {task.Purpose}\nОткрыть заявку: {url}\nВход — под вашей корпоративной учётной записью. Если задача уже выполнена, повторное действие не требуется. На письмо отвечать не нужно.";
        static string H(string? value)=>WebUtility.HtmlEncode(value??"");
        var html=$"<html lang=\"ru\"><body style=\"font-family:Arial,sans-serif;color:#333\"><div style=\"background:#fff0a5;padding:16px\"><strong style=\"color:#a30d20\">BMS — уведомление</strong></div><h2 style=\"color:#830e25\">Расходная заявка {H(task.Number)}</h2><p>{H(task.EmployeeName)}, необходимо {H(action)}.</p><p>Организация: {H(task.Organization)}<br>ЦФО: {H(task.CostCenter)}<br>Сумма: {H(amount)}</p><p>Назначение: {H(task.Purpose)}</p><p><a href=\"{url}\" style=\"display:inline-block;background:#a30d20;color:white;padding:12px 18px;text-decoration:none\">Открыть заявку в BMS</a></p><p>Вход — под вашей корпоративной учётной записью. Если задача уже выполнена, повторное действие не требуется.</p><p>Автоматическое уведомление. На письмо отвечать не нужно.</p></body></html>";
        var message=new MimeMessage {Subject=subject,MessageId=task.MailId.ToString("N")+"@"+BmsInstallationSettings.PublicBaseUri.DnsSafeHost};
        if(returned) html=html.Replace("</h2>","</h2><p>Заявка возвращена вам на доработку.</p><p>Причина возврата: "+H(task.ReturnReason??"Смотрите историю заявки.")+"</p>",StringComparison.Ordinal);
        message.From.Add(new MailboxAddress("BMS — уведомления",from));message.To.Add(MailboxAddress.Parse(recipient));
        message.Body=new BodyBuilder {TextBody=text,HtmlBody=html}.ToMessageBody();return message;
    }

    private static MimeMessage BuildStaff471(TaskMailTask task,string recipient,string from)
    {
        if(task.Role!="SUBSTITUTION_RECIPIENT"||task.RequestId==Guid.Empty||!TaskMailDelivery.ValidAddress(recipient)
            ||!TaskMailDelivery.ValidAddress(from)||string.IsNullOrWhiteSpace(task.Number)||task.Number.Any(char.IsControl))
            throw new InvalidOperationException("Invalid staff mail identity.");
        var url=BmsInstallationSettings.PublicBaseUri.AbsoluteUri+"?staffSubstitution="+task.RequestId.ToString("D");
        static string H(string? text)=>WebUtility.HtmlEncode(text??"");
        var text=$"{task.EmployeeName}, вам направлен документ «Замещение сотрудника» {task.Number}.\n{task.Purpose}\nОткройте документ и нажмите «Подтвердить» или «Отказать». До вашего подтверждения права не передаются.\n{url}\nВход — под вашей корпоративной учётной записью. На письмо отвечать не нужно.";
        var html=$"<html lang=\"ru\"><body style=\"font-family:Arial,sans-serif;color:#333\"><h2 style=\"color:#a30d20\">Замещение сотрудника {H(task.Number)}</h2><p>{H(task.EmployeeName)}, вам направлен документ на подтверждение.</p><p>{H(task.Purpose)}</p><p>Откройте документ и нажмите «Подтвердить» или «Отказать». До подтверждения права не передаются.</p><p><a href=\"{url}\">Открыть документ в BMS</a></p><p>Автоматическое уведомление. На письмо отвечать не нужно.</p></body></html>";
        var result=new MimeMessage{Subject=$"BMS: замещение сотрудника {task.Number} — требуется подтверждение",MessageId=task.MailId.ToString("N")+"@"+BmsInstallationSettings.PublicBaseUri.DnsSafeHost};
        result.From.Add(new MailboxAddress("BMS — уведомления",from));result.To.Add(MailboxAddress.Parse(recipient));
        result.Body=new BodyBuilder{TextBody=text,HtmlBody=html}.ToMessageBody();return result;
    }

    private static MimeMessage BuildAdjustment448(TaskMailTask task,string recipient,string from)
    {
        if(task.Role is not("FIN_DIRECTOR" or "GENERAL_DIRECTOR"))
            throw new InvalidOperationException("Invalid budget adjustment approval role.");
        var action=task.Role=="GENERAL_DIRECTOR"?"утвердить корректировку бюджета":"согласовать корректировку бюджета";
        var url=BmsInstallationSettings.PublicBaseUri.AbsoluteUri+"?budgetAdjustment="+task.RequestId.ToString("D");
        var amount=task.Amount.ToString("N0",CultureInfo.GetCultureInfo("ru-RU"))+" "+task.Currency;
        var text=$"{task.EmployeeName}, на ваш этап поступила корректировка бюджета {task.Number}.\nНеобходимо {action}.\nЦФО: {task.CostCenter}\nИсходный экземпляр и период: {task.Purpose}\nСумма переносов: {amount}\nОткрыть корректировку: {url}\nВход — под вашей корпоративной учётной записью. Если задача уже выполнена, повторное действие не требуется. На письмо отвечать не нужно.";
        static string H(string? value)=>WebUtility.HtmlEncode(value??"");
        var html=$"<html lang=\"ru\"><body style=\"font-family:Arial,sans-serif;color:#333\"><div style=\"background:#fff0a5;padding:16px\"><strong style=\"color:#a30d20\">BMS — уведомление</strong></div><h2 style=\"color:#830e25\">Корректировка бюджета {H(task.Number)}</h2><p>{H(task.EmployeeName)}, необходимо {H(action)}.</p><p>ЦФО: {H(task.CostCenter)}<br>Исходный экземпляр и период: {H(task.Purpose)}<br>Сумма переносов: {H(amount)}</p><p><a href=\"{url}\" style=\"display:inline-block;background:#a30d20;color:white;padding:12px 18px;text-decoration:none\">Открыть корректировку в BMS</a></p><p>Вход — под вашей корпоративной учётной записью. Если задача уже выполнена, повторное действие не требуется.</p><p>Автоматическое уведомление. На письмо отвечать не нужно.</p></body></html>";
        var message=new MimeMessage {Subject=$"BMS: корректировка бюджета {task.CostCenter} № {task.Number} — необходимо {action}",MessageId=task.MailId.ToString("N")+"@"+BmsInstallationSettings.PublicBaseUri.DnsSafeHost};
        message.From.Add(new MailboxAddress("BMS — уведомления",from));message.To.Add(MailboxAddress.Parse(recipient));
        message.Body=new BodyBuilder {TextBody=text,HtmlBody=html}.ToMessageBody();return message;
    }

    private static MimeMessage BuildBudget(TaskMailTask task,string recipient,string from)
    {
        if(task.Role is not("BUDGET_OWNER" or "FIN_CONTROLLER" or "FIN_DIRECTOR" or "GENERAL_DIRECTOR"))
            throw new InvalidOperationException("Invalid budget approval role.");
        var action=task.Role=="GENERAL_DIRECTOR"?"утвердить бюджет":"согласовать бюджет";
        var url=BmsInstallationSettings.PublicBaseUri.AbsoluteUri+"?budgetInstance="+task.RequestId.ToString("D");
        var amount=task.Amount.ToString("N0",CultureInfo.GetCultureInfo("ru-RU"))+" "+task.Currency;
        var text=$"{task.EmployeeName}, на ваш этап поступил экземпляр бюджета {task.Number}.\nНеобходимо {action}.\nЦФО: {task.CostCenter}\nПериод: {task.Purpose}\nПлан расходов: {amount}\nОткрыть бюджет: {url}\nВход — под вашей корпоративной учётной записью. Если задача уже выполнена, повторное действие не требуется. На письмо отвечать не нужно.";
        static string H(string? value)=>WebUtility.HtmlEncode(value??"");
        var html=$"<html lang=\"ru\"><body style=\"font-family:Arial,sans-serif;color:#333\"><div style=\"background:#fff0a5;padding:16px\"><strong style=\"color:#a30d20\">BMS — уведомление</strong></div><h2 style=\"color:#830e25\">Экземпляр бюджета {H(task.Number)}</h2><p>{H(task.EmployeeName)}, необходимо {H(action)}.</p><p>ЦФО: {H(task.CostCenter)}<br>Период: {H(task.Purpose)}<br>План расходов: {H(amount)}</p><p><a href=\"{url}\" style=\"display:inline-block;background:#a30d20;color:white;padding:12px 18px;text-decoration:none\">Открыть бюджет в BMS</a></p><p>Вход — под вашей корпоративной учётной записью. Если задача уже выполнена, повторное действие не требуется.</p><p>Автоматическое уведомление. На письмо отвечать не нужно.</p></body></html>";
        var message=new MimeMessage {Subject=$"BMS: бюджет {task.CostCenter} № {task.Number} — необходимо {action}",MessageId=task.MailId.ToString("N")+"@"+BmsInstallationSettings.PublicBaseUri.DnsSafeHost};
        message.From.Add(new MailboxAddress("BMS — уведомления",from));message.To.Add(MailboxAddress.Parse(recipient));
        message.Body=new BodyBuilder {TextBody=text,HtmlBody=html}.ToMessageBody();return message;
    }
}

public sealed class SmtpTaskMailTransport : ITaskMailTransport
{
    public async Task SendAsync(TaskMailTask task,string recipient,TaskMailDelivery delivery,CancellationToken token)
    {
        if(!delivery.Enabled)throw new InvalidOperationException("SMTP delivery is disabled.");
        // Parse again: callers cannot bypass fail-closed TLS/address checks.
        delivery=TaskMailDelivery.Parse(JsonSerializer.Serialize(delivery));
        var from=delivery.From??throw new InvalidOperationException("SMTP sender is missing.");
        var host=delivery.Host??throw new InvalidOperationException("SMTP host is missing.");
        using var message=TaskMailTemplate.Build(task,recipient,from);
        using var smtp=new SmtpClient {Timeout=20000,SslProtocols=System.Security.Authentication.SslProtocols.Tls12|System.Security.Authentication.SslProtocols.Tls13};var sendStarted=false;
        // The optional pin is acquired through an independently authenticated
        // administrative channel. It replaces trust in this one self-signed
        // endpoint, never hostname, lifetime or arbitrary TLS validation.
        if(delivery.CertificateSha256 is { } pin)
            smtp.ServerCertificateValidationCallback=(_,certificate,chain,errors)=>
                TaskMailCertificatePolicy.Validate(certificate,chain,errors,pin,DateTimeOffset.UtcNow);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await smtp.ConnectAsync(host,delivery.Port,delivery.Tls=="StartTls"?SecureSocketOptions.StartTls:SecureSocketOptions.SslOnConnect,timeout.Token);
            if(!string.IsNullOrEmpty(delivery.UserName))await smtp.AuthenticateAsync(delivery.UserName,delivery.Password??throw new InvalidOperationException("SMTP credential is incomplete."),timeout.Token);
            sendStarted=true;await smtp.SendAsync(message,timeout.Token);
            // A QUIT failure after SMTP acceptance must not create a resend.
        }
        catch(SmtpCommandException){throw new TaskMailDeliveryException(false);}
        catch(Exception error)when(error is not OutOfMemoryException and not StackOverflowException)
        {throw new TaskMailDeliveryException(sendStarted);}
    }
}

internal static class TaskMailCertificatePolicy
{
    internal static bool Validate(X509Certificate? certificate,X509Chain? chain,SslPolicyErrors errors,
        string pin,DateTimeOffset now)
    {
        if(certificate is null||pin.Length!=64||!pin.All(char.IsAsciiHexDigit)
            ||(errors & ~SslPolicyErrors.RemoteCertificateChainErrors)!=SslPolicyErrors.None)return false;
        using var leaf=new X509Certificate2(certificate);
        if(now.UtcDateTime<leaf.NotBefore.ToUniversalTime()||now.UtcDateTime>leaf.NotAfter.ToUniversalTime()
            ||!CryptographicOperations.FixedTimeEquals(leaf.GetCertHash(HashAlgorithmName.SHA256),Convert.FromHexString(pin)))return false;
        if(errors==SslPolicyErrors.None)return true;
        return chain is not null && chain.ChainStatus.Length>0
            && chain.ChainStatus.All(s=>s.Status==X509ChainStatusFlags.UntrustedRoot);
    }
}
