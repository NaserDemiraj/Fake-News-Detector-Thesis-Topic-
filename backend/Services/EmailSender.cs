using System.Net;
using System.Net.Mail;
using System.Net.Http.Json;

namespace FakeNewsDetector.Services
{
    // Pluggable email sender, in priority order:
    //  1. Brevo HTTP API (Brevo:ApiKey)  — works on hosts that block outbound SMTP
    //     ports (Hugging Face Spaces, Render, etc.). Sends over HTTPS.
    //  2. SMTP (Smtp:Host)               — classic SMTP, for hosts that allow it.
    //  3. Dev fallback                   — logs the message (incl. links) to the console.
    public class EmailSender : IEmailSender
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailSender> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public EmailSender(IConfiguration config, ILogger<EmailSender> logger, IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public async Task SendAsync(string to, string subject, string htmlBody)
        {
            var brevoKey = _config["Brevo:ApiKey"];
            var smtpHost = _config["Smtp:Host"];
            var from = _config["Email:From"] ?? _config["Smtp:From"] ?? _config["Smtp:Username"] ?? "no-reply@verifynews.app";
            var fromName = _config["Email:FromName"] ?? "TruthLens";

            // 1. Brevo HTTP API (preferred on SMTP-blocked hosts)
            if (!string.IsNullOrWhiteSpace(brevoKey))
            {
                await SendViaBrevoAsync(brevoKey, from, fromName, to, subject, htmlBody);
                return;
            }

            // 2. SMTP
            if (!string.IsNullOrWhiteSpace(smtpHost))
            {
                await SendViaSmtpAsync(smtpHost, from, to, subject, htmlBody);
                return;
            }

            // 3. Dev fallback — no provider configured: log instead of sending
            _logger.LogWarning(
                "\n========== EMAIL (dev mode — no email provider configured) ==========\n" +
                "To:      {To}\nSubject: {Subject}\n\n{Body}\n" +
                "====================================================================",
                to, subject, StripHtml(htmlBody));
        }

        private async Task SendViaBrevoAsync(string apiKey, string from, string fromName, string to, string subject, string htmlBody)
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15); // never hang the request

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
            req.Headers.Add("api-key", apiKey);
            req.Content = JsonContent.Create(new
            {
                sender = new { email = from, name = fromName },
                to = new[] { new { email = to } },
                subject,
                htmlContent = htmlBody
            });

            try
            {
                var resp = await client.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    _logger.LogError("Brevo email to {To} failed ({Status}): {Body}", to, (int)resp.StatusCode, body);
                    throw new Exception($"Brevo email failed: {resp.StatusCode}");
                }
                _logger.LogInformation("Email sent via Brevo to {To}: {Subject}", to, subject);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Brevo email send to {To} failed", to);
                throw;
            }
        }

        private async Task SendViaSmtpAsync(string host, string from, string to, string subject, string htmlBody)
        {
            var port = int.TryParse(_config["Smtp:Port"], out var p) ? p : 587;
            var user = _config["Smtp:Username"];
            var pass = _config["Smtp:Password"];

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = true,
                Credentials = string.IsNullOrEmpty(user) ? null : new NetworkCredential(user, pass)
            };
            using var message = new MailMessage(from, to, subject, htmlBody) { IsBodyHtml = true };

            try
            {
                await client.SendMailAsync(message);
                _logger.LogInformation("Email sent via SMTP to {To}: {Subject}", to, subject);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email via SMTP to {To}", to);
                throw;
            }
        }

        private static string StripHtml(string html) =>
            System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", " ")
                .Replace("&nbsp;", " ").Trim();
    }
}
