using System.Net;
using System.Net.Mail;
using GNAS.Core;
using Microsoft.Extensions.Logging;

namespace GNAS.Observability.Alerts.Notifiers;

/// <summary>Email alert notifier based on SMTP.</summary>
public sealed class EmailNotifier : INotifier
{
    private readonly IGnasConfiguration _configuration;
    private readonly ILogger<EmailNotifier>? _logger;

    /// <summary>Initialize email notifier.</summary>
    public EmailNotifier(IGnasConfiguration configuration, ILogger<EmailNotifier>? logger = null)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyAsync(ActiveAlert alert, AlertRule rule, CancellationToken ct)
    {
        var host = _configuration.GetValue("alerts:smtp:host");
        var from = _configuration.GetValue("alerts:smtp:from");
        var recipients = _configuration.GetArray("alerts:smtp:to");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from) || recipients.Length == 0)
        {
            _logger?.LogWarning("SMTP not configured, skipping email alert.");
            return;
        }

        using var message = new MailMessage { From = new MailAddress(from), Subject = $"[{alert.Severity}] {rule.Name}", Body = alert.Message };
        foreach (var recipient in recipients) message.To.Add(recipient);
        using var client = new SmtpClient(host, int.TryParse(_configuration.GetValue("alerts:smtp:port"), out var port) ? port : 587)
        {
            EnableSsl = bool.TryParse(_configuration.GetValue("alerts:smtp:ssl"), out var ssl) ? ssl : true
        };
        var user = _configuration.GetValue("alerts:smtp:user");
        var pass = _configuration.GetValue("alerts:smtp:pass");
        if (!string.IsNullOrWhiteSpace(user)) client.Credentials = new NetworkCredential(user, pass);
        await client.SendMailAsync(message, ct).ConfigureAwait(false);
    }
}
