using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Bms.Infrastructure.Mail;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace Bms.WeeklyPrimary425;

internal sealed record TransportResult425(string State, bool SendStarted);
internal interface ITransport425 { Task<TransportResult425> Send(Prepared425 item, Func<CancellationToken, Task<bool>> immediateGate, CancellationToken token); }
internal sealed class SmtpTransport425(TaskMailDelivery settings) : ITransport425
{
    private delegate bool CertificateValidator(X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors, string pin, DateTimeOffset now);
    internal static void ValidatePolicy()
    {
        var type = typeof(TaskMailDelivery).Assembly.GetType("Bms.Infrastructure.Mail.TaskMailCertificatePolicy", true)!;
        _ = type.GetMethod("Validate", BindingFlags.NonPublic | BindingFlags.Static)!.CreateDelegate<CertificateValidator>();
    }
    public async Task<TransportResult425> Send(Prepared425 item, Func<CancellationToken, Task<bool>> immediateGate, CancellationToken token)
    {
        var started = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var message = Content425.Build(item, settings.From!);
            using var smtp = new SmtpClient { Timeout = 20000, SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13 };
            var type = typeof(TaskMailDelivery).Assembly.GetType("Bms.Infrastructure.Mail.TaskMailCertificatePolicy", true)!;
            var validate = type.GetMethod("Validate", BindingFlags.NonPublic | BindingFlags.Static)!.CreateDelegate<CertificateValidator>();
            if (settings.CertificateSha256 is { } pin) smtp.ServerCertificateValidationCallback = (_, cert, chain, errors) => validate(cert, chain, errors, pin, DateTimeOffset.UtcNow);
            await smtp.ConnectAsync(settings.Host!, settings.Port, settings.Tls == "StartTls" ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect, deadline.Token);
            if (!string.IsNullOrEmpty(settings.UserName)) await smtp.AuthenticateAsync(settings.UserName, settings.Password!, deadline.Token);
            // Fresh owner/closure/AD check after connect/auth and immediately before sending any message data.
            if (!await immediateGate(deadline.Token)) return new("DefinitePreSendFailure", false);
            started = true; await smtp.SendAsync(message, deadline.Token);
            return new("Accepted", true);
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        { return new(started ? "Uncertain" : "DefinitePreSendFailure", started); }
    }
}
internal static class DeliveryEngine425
{
    internal static async Task<string> Deliver(Ledger425 ledger, Prepared425 item, string mode, ITransport425 transport,
        Func<CancellationToken, Task<bool>> gate, CancellationToken token)
    {
        var next = ledger.NextAttempt(item, mode); if (next.Number == 0) return next.State;
        if (!await gate(token)) return "BlockedLiveGate";
        var hash = ledger.StorePrepared(item);
        ledger.ClaimAttempt(item, next.Number, hash, next.AuthorizationSha); // Durable before any SMTP network operation.
        var result = await transport.Send(item, gate, token);
        ledger.Complete(item, next.Number, result.State, result.SendStarted, hash);
        return result.State;
    }
}
