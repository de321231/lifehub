using DESD.LifeHub.Core.Abstractions;
using DESD.LifeHub.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DESD.LifeHub.Infrastructure.Services;

/// <summary>
/// Standardimplementierung des Mailversands.
///
/// Ehrliche Einschränkung: Im MVP ist kein SMTP-Provider angebunden. Diese Klasse
/// versendet daher keine E-Mails, sondern protokolliert nur, dass ein Versand
/// angefordert wurde und kein Provider konfiguriert ist. Es wird bewusst kein
/// erfolgreicher Versand vorgetäuscht.
/// </summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(IOptions<MailOptions> options, ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
        Options = options.Value;
    }

    private MailOptions Options { get; }

    public bool IsConfigured => Options.IsConfigured;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning(
                "E-Mail-Versand angefordert, aber kein SMTP-Provider konfiguriert (Mail:Host/FromAddress fehlen). " +
                "Nachricht an {Recipient} wurde NICHT versendet. Betreff: {Subject}",
                MaskEmail(message.To),
                message.Subject);
            return Task.CompletedTask;
        }

        // Ein konfigurierter SMTP-Versand ist noch nicht implementiert. Auch hier
        // wird kein Erfolg vorgetäuscht.
        _logger.LogWarning(
            "SMTP ist konfiguriert, der Versand ist in dieser MVP-Version jedoch nicht implementiert. " +
            "Nachricht an {Recipient} wurde NICHT versendet.",
            MaskEmail(message.To));

        return Task.CompletedTask;
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1)
        {
            return "***";
        }

        return $"{email[0]}***{email[at..]}";
    }
}
