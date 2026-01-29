using MailKit.Security;
using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PetitShope.Services;

public interface IEmailSender
{
    Task SendEmailAsync(string to, string subject, string htmlBody);
}

public class MailKitEmailSender : IEmailSender
{
    private readonly IConfiguration _cfg;
    private readonly ILogger<MailKitEmailSender> _logger;

    public MailKitEmailSender(IConfiguration cfg, ILogger<MailKitEmailSender> logger)
    {
        _cfg = cfg;
        _logger = logger;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        var rawEnabled = _cfg["Smtp:Enabled"] ?? string.Empty;
        rawEnabled = rawEnabled.Trim().Trim('"', '\'');
        var enabled = bool.TryParse(rawEnabled, out var parsed) ? parsed : false;
        if (!enabled)
        {
            _logger.LogInformation("SMTP disabled — email to {to}: {subject}\n{body}", to, subject, htmlBody);
            return;
        }

        var host = _cfg["Smtp:Host"] ?? "localhost";
        var port = int.TryParse(_cfg["Smtp:Port"], out var p) ? p : 25;
        var user = _cfg["Smtp:User"] ?? "";
        var pass = _cfg["Smtp:Pass"] ?? "";
        var from = _cfg["Smtp:From"] ?? _cfg["Smtp:User"] ?? "no-reply@localhost";
        var rawEnableSsl = _cfg["Smtp:EnableSsl"] ?? string.Empty;
        rawEnableSsl = rawEnableSsl.Trim().Trim('"', '\'');
        var enableSsl = bool.TryParse(rawEnableSsl, out var parsedSsl) ? parsedSsl : false;

        try
        {
            var msg = new MimeMessage();
            msg.From.Add(MailboxAddress.Parse(from));
            msg.To.Add(MailboxAddress.Parse(to));
            msg.Subject = subject;
            var body = new BodyBuilder { HtmlBody = htmlBody };
            msg.Body = body.ToMessageBody();

            using var client = new MailKit.Net.Smtp.SmtpClient();
            var socketOpt = enableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(host, port, socketOpt);
            if (!string.IsNullOrEmpty(user)) await client.AuthenticateAsync(user, pass);
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
            _logger.LogInformation("Sent verification email to {to}", to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {to}", to);
            throw;
        }
    }
}
