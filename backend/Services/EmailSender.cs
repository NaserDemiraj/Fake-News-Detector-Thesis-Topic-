using System.Net;
using System.Net.Mail;

namespace FakeNewsDetector.Services
{
    // Pluggable email sender:
    //  - If SMTP settings are configured (Smtp:Host), sends real email.
    //  - Otherwise logs the message (incl. links) to the console — perfect for local dev.
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(IConfiguration config, ILogger<EmailSender> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendAsync(string to, string subject, string htmlBody)
        {
            var host = _config["Smtp:Host"];

            // Dev fallback — no SMTP configured: log instead of sending
            if (string.IsNullOrWhiteSpace(host))
            {
                _logger.LogWarning(
                    "\n========== EMAIL (dev mode — no SMTP configured) ==========\n" +
                    "To:      {To}\nSubject: {Subject}\n\n{Body}\n" +
                    "===========================================================",
                    to, subject, StripHtml(htmlBody));
                return;
            }

            var port = int.TryParse(_config["Smtp:Port"], out var p) ? p : 587;
            var user = _config["Smtp:Username"];
            var pass = _config["Smtp:Password"];
            var from = _config["Smtp:From"] ?? user ?? "no-reply@verifynews.app";

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = true,
                Credentials = string.IsNullOrEmpty(user) ? null : new NetworkCredential(user, pass)
            };

            using var message = new MailMessage(from, to, subject, htmlBody) { IsBodyHtml = true };

            try
            {
                await client.SendMailAsync(message);
                _logger.LogInformation("Email sent to {To}: {Subject}", to, subject);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {To}", to);
                throw;
            }
        }

        private static string StripHtml(string html) =>
            System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", " ")
                .Replace("&nbsp;", " ").Trim();
    }
}
