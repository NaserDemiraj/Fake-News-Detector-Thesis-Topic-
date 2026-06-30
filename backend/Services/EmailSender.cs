using System.Net;
using System.Net.Mail;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace FakeNewsDetector.Services
{
    // Pluggable email sender, in priority order:
    //  1. Mailjet HTTP API (Mailjet:ApiKey + Mailjet:SecretKey) — free, no domain
    //     needed, sends to ANY recipient (just verify the sender email).
    //  2. Resend HTTP API (Resend:ApiKey) — instant signup, but test mode only mails
    //     the account owner unless a domain is verified.
    //  3. Brevo HTTP API (Brevo:ApiKey)   — alternative HTTP provider.
    //  4. SMTP (Smtp:Host)                — classic SMTP, for hosts that allow it.
    //  5. Dev fallback                    — logs the message (incl. links) to the console.
    // HTTP providers (1-3) work on hosts that block outbound SMTP ports (Hugging Face
    // Spaces, Render, etc.) because they send over HTTPS/443.
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
            var mailjetKey = _config["Mailjet:ApiKey"];
            var mailjetSecret = _config["Mailjet:SecretKey"];
            var resendKey = _config["Resend:ApiKey"];
            var brevoKey = _config["Brevo:ApiKey"];
            var smtpHost = _config["Smtp:Host"];
            var from = _config["Email:From"] ?? _config["Smtp:From"] ?? _config["Smtp:Username"] ?? "no-reply@verifynews.app";
            var fromName = _config["Email:FromName"] ?? "TruthLens";

            // 1. Mailjet HTTP API (free, no domain, sends to anyone)
            if (!string.IsNullOrWhiteSpace(mailjetKey) && !string.IsNullOrWhiteSpace(mailjetSecret))
            {
                await SendViaMailjetAsync(mailjetKey, mailjetSecret, from, fromName, to, subject, htmlBody);
                return;
            }

            // 2. Resend HTTP API (no phone verification; instant key).
            // Without a verified domain, Resend requires from=onboarding@resend.dev and
            // only delivers to the account owner's email — fine for a demo.
            if (!string.IsNullOrWhiteSpace(resendKey))
            {
                var resendFrom = _config["Resend:From"] ?? $"{fromName} <onboarding@resend.dev>";
                await SendViaResendAsync(resendKey, resendFrom, to, subject, htmlBody);
                return;
            }

            // 2. Brevo HTTP API
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

        private async Task SendViaMailjetAsync(string apiKey, string secretKey, string from, string fromName, string to, string subject, string htmlBody)
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15); // never hang the request

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.mailjet.com/v3.1/send");
            var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{secretKey}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);
            req.Content = JsonContent.Create(new
            {
                Messages = new[]
                {
                    new
                    {
                        From = new { Email = from, Name = fromName },
                        To = new[] { new { Email = to } },
                        Subject = subject,
                        HTMLPart = htmlBody
                    }
                }
            });

            try
            {
                var resp = await client.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    _logger.LogError("Mailjet email to {To} failed ({Status}): {Body}", to, (int)resp.StatusCode, body);
                    throw new Exception($"Mailjet email failed: {resp.StatusCode}");
                }
                _logger.LogInformation("Email sent via Mailjet to {To}: {Subject}", to, subject);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mailjet email send to {To} failed", to);
                throw;
            }
        }

        private async Task SendViaResendAsync(string apiKey, string from, string to, string subject, string htmlBody)
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15); // never hang the request

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Content = JsonContent.Create(new
            {
                from,
                to = new[] { to },
                subject,
                html = htmlBody
            });

            try
            {
                var resp = await client.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync();
                    _logger.LogError("Resend email to {To} failed ({Status}): {Body}", to, (int)resp.StatusCode, body);
                    throw new Exception($"Resend email failed: {resp.StatusCode}");
                }
                _logger.LogInformation("Email sent via Resend to {To}: {Subject}", to, subject);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Resend email send to {To} failed", to);
                throw;
            }
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
