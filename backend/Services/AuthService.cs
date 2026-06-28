using FakeNewsDetector.Models;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace FakeNewsDetector.Services
{
    public class AuthService : IAuthService
    {
        private readonly NeonHttpService _neon;
        private readonly IConfiguration _config;
        private readonly ILogger<AuthService> _logger;
        private readonly IEmailSender _email;

        private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);
        private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);
        private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromHours(1);
        private static readonly TimeSpan VerifyTokenLifetime = TimeSpan.FromDays(3);

        public AuthService(NeonHttpService neon, IConfiguration config, ILogger<AuthService> logger, IEmailSender email)
        {
            _neon = neon;
            _config = config;
            _logger = logger;
            _email = email;
        }

        private string AppBaseUrl => _config["AppBaseUrl"]?.TrimEnd('/') ?? "http://localhost:3000";

        public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
        {
            var email = request.Email.Trim().ToLowerInvariant();

            var existing = await _neon.QueryAsync(
                @"SELECT ""Id"" FROM ""Users"" WHERE ""Email"" = $1", email);

            if (existing.Count > 0)
                throw new InvalidOperationException("EMAIL_TAKEN");

            var user = new User
            {
                Id = Guid.NewGuid().ToString(),
                Email = email,
                Name = request.Name.Trim(),
                PasswordHash = HashPassword(request.Password),
                CreatedAt = DateTime.UtcNow
            };

            await _neon.ExecuteAsync(
                @"INSERT INTO ""Users"" (""Id"",""Email"",""PasswordHash"",""Name"",""CreatedAt"",""EmailVerified"")
                  VALUES ($1,$2,$3,$4,$5,$6)",
                user.Id, user.Email, user.PasswordHash, user.Name,
                user.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), false);

            _logger.LogInformation("New user registered: {Email}", email);

            // Fire off a verification email (non-blocking failure)
            try { await SendVerificationEmailAsync(user.Id, user.Email, user.Name); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not send verification email to {Email}", email); }

            var (access, refresh) = await IssueTokensAsync(user, emailVerified: false);
            return new AuthResponse
            {
                Token = access,
                RefreshToken = refresh,
                UserId = user.Id,
                Email = user.Email,
                Name = user.Name,
                EmailVerified = false
            };
        }

        public async Task<AuthResponse?> LoginAsync(LoginRequest request)
        {
            var email = request.Email.Trim().ToLowerInvariant();

            var rows = await _neon.QueryAsync(
                @"SELECT ""Id"",""Email"",""PasswordHash"",""Name"",""EmailVerified"" FROM ""Users"" WHERE ""Email"" = $1", email);

            if (rows.Count == 0) return null;

            var row = rows[0] as JsonObject;
            if (row == null) return null;

            var storedHash = row["PasswordHash"]?.GetValue<string>() ?? "";
            if (!VerifyPassword(request.Password, storedHash)) return null;

            var user = new User
            {
                Id = row["Id"]?.GetValue<string>() ?? "",
                Email = row["Email"]?.GetValue<string>() ?? "",
                Name = row["Name"]?.GetValue<string>() ?? ""
            };
            var emailVerified = BoolVal(row, "EmailVerified");

            var (access, refresh) = await IssueTokensAsync(user, emailVerified);
            return new AuthResponse
            {
                Token = access,
                RefreshToken = refresh,
                UserId = user.Id,
                Email = user.Email,
                Name = user.Name,
                EmailVerified = emailVerified
            };
        }

        public async Task<AuthResponse?> RefreshAsync(string refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken)) return null;
            var hash = HashToken(refreshToken);

            var rows = await _neon.QueryAsync(
                @"SELECT ""Id"",""UserId"",""ExpiresAt"",""RevokedAt"" FROM ""RefreshTokens"" WHERE ""TokenHash"" = $1", hash);
            if (rows.Count == 0) return null;

            var row = rows[0] as JsonObject;
            if (row == null) return null;

            if (row["RevokedAt"] != null) return null; // already revoked
            if (!DateTime.TryParse(row["ExpiresAt"]?.ToString(), out var expires) || expires < DateTime.UtcNow)
                return null;

            var userId = row["UserId"]?.GetValue<string>() ?? "";
            var userRows = await _neon.QueryAsync(
                @"SELECT ""Id"",""Email"",""Name"",""EmailVerified"" FROM ""Users"" WHERE ""Id"" = $1", userId);
            if (userRows.Count == 0) return null;
            var u = userRows[0] as JsonObject;
            if (u == null) return null;

            var user = new User
            {
                Id = u["Id"]?.GetValue<string>() ?? "",
                Email = u["Email"]?.GetValue<string>() ?? "",
                Name = u["Name"]?.GetValue<string>() ?? ""
            };
            var emailVerified = BoolVal(u, "EmailVerified");

            // Rotate: revoke the old token, issue a fresh pair
            await _neon.ExecuteAsync(
                @"UPDATE ""RefreshTokens"" SET ""RevokedAt"" = $1 WHERE ""TokenHash"" = $2",
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), hash);

            var (access, refresh) = await IssueTokensAsync(user, emailVerified);
            return new AuthResponse
            {
                Token = access,
                RefreshToken = refresh,
                UserId = user.Id,
                Email = user.Email,
                Name = user.Name,
                EmailVerified = emailVerified
            };
        }

        public async Task LogoutAsync(string refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken)) return;
            await _neon.ExecuteAsync(
                @"UPDATE ""RefreshTokens"" SET ""RevokedAt"" = $1 WHERE ""TokenHash"" = $2 AND ""RevokedAt"" IS NULL",
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), HashToken(refreshToken));
        }

        public async Task ForgotPasswordAsync(string email)
        {
            email = email.Trim().ToLowerInvariant();
            var rows = await _neon.QueryAsync(
                @"SELECT ""Id"",""Name"" FROM ""Users"" WHERE ""Email"" = $1", email);

            // Always return silently (don't leak which emails exist)
            if (rows.Count == 0)
            {
                _logger.LogInformation("Password reset requested for non-existent email: {Email}", email);
                return;
            }

            var row = rows[0] as JsonObject;
            var userId = row?["Id"]?.GetValue<string>() ?? "";
            var name = row?["Name"]?.GetValue<string>() ?? "";

            var rawToken = GenerateRawToken();
            await StoreUserTokenAsync(userId, rawToken, "reset", DateTime.UtcNow.Add(ResetTokenLifetime));

            var link = $"{AppBaseUrl}/reset-password.html?token={rawToken}";
            await _email.SendAsync(email, "Reset your VerifyNews password",
                $@"<p>Hi {WebUtil(name)},</p>
                   <p>We received a request to reset your password. Click the link below (valid for 1 hour):</p>
                   <p><a href=""{link}"">Reset my password</a></p>
                   <p>If you didn't request this, you can safely ignore this email.</p>");
        }

        public async Task<bool> ResetPasswordAsync(string token, string newPassword)
        {
            if (string.IsNullOrEmpty(token) || newPassword.Length < 8) return false;

            var userId = await ConsumeUserTokenAsync(token, "reset");
            if (userId == null) return false;

            await _neon.ExecuteAsync(
                @"UPDATE ""Users"" SET ""PasswordHash"" = $1 WHERE ""Id"" = $2",
                HashPassword(newPassword), userId);

            // Security: revoke all refresh tokens after a password reset
            await _neon.ExecuteAsync(
                @"UPDATE ""RefreshTokens"" SET ""RevokedAt"" = $1 WHERE ""UserId"" = $2 AND ""RevokedAt"" IS NULL",
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), userId);

            _logger.LogInformation("Password reset for user {UserId}", userId);
            return true;
        }

        public async Task<bool> VerifyEmailAsync(string token)
        {
            var userId = await ConsumeUserTokenAsync(token, "verify");
            if (userId == null) return false;

            await _neon.ExecuteAsync(
                @"UPDATE ""Users"" SET ""EmailVerified"" = true WHERE ""Id"" = $1", userId);
            _logger.LogInformation("Email verified for user {UserId}", userId);
            return true;
        }

        // ---- helpers ----

        private async Task SendVerificationEmailAsync(string userId, string email, string name)
        {
            var rawToken = GenerateRawToken();
            await StoreUserTokenAsync(userId, rawToken, "verify", DateTime.UtcNow.Add(VerifyTokenLifetime));

            var link = $"{AppBaseUrl}/verify-email.html?token={rawToken}";
            await _email.SendAsync(email, "Verify your VerifyNews email",
                $@"<p>Hi {WebUtil(name)},</p>
                   <p>Welcome to VerifyNews! Please confirm your email address:</p>
                   <p><a href=""{link}"">Verify my email</a></p>
                   <p>This link is valid for 3 days.</p>");
        }

        private async Task<(string access, string refresh)> IssueTokensAsync(User user, bool emailVerified)
        {
            var access = GenerateJwt(user, emailVerified);
            var refresh = GenerateRawToken();

            await _neon.ExecuteAsync(
                @"INSERT INTO ""RefreshTokens"" (""Id"",""TokenHash"",""UserId"",""ExpiresAt"",""CreatedAt"")
                  VALUES ($1,$2,$3,$4,$5)",
                Guid.NewGuid().ToString(), HashToken(refresh), user.Id,
                DateTime.UtcNow.Add(RefreshTokenLifetime).ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));

            return (access, refresh);
        }

        private async Task StoreUserTokenAsync(string userId, string rawToken, string type, DateTime expiresAt)
        {
            await _neon.ExecuteAsync(
                @"INSERT INTO ""UserTokens"" (""Id"",""TokenHash"",""UserId"",""Type"",""ExpiresAt"",""CreatedAt"")
                  VALUES ($1,$2,$3,$4,$5,$6)",
                Guid.NewGuid().ToString(), HashToken(rawToken), userId, type,
                expiresAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        }

        // Returns userId if the token is valid+unused, marks it used, else null
        private async Task<string?> ConsumeUserTokenAsync(string rawToken, string type)
        {
            if (string.IsNullOrEmpty(rawToken)) return null;
            var hash = HashToken(rawToken);

            var rows = await _neon.QueryAsync(
                @"SELECT ""UserId"",""ExpiresAt"",""UsedAt"" FROM ""UserTokens"" WHERE ""TokenHash"" = $1 AND ""Type"" = $2",
                hash, type);
            if (rows.Count == 0) return null;

            var row = rows[0] as JsonObject;
            if (row == null) return null;
            if (row["UsedAt"] != null) return null;
            if (!DateTime.TryParse(row["ExpiresAt"]?.ToString(), out var expires) || expires < DateTime.UtcNow)
                return null;

            await _neon.ExecuteAsync(
                @"UPDATE ""UserTokens"" SET ""UsedAt"" = $1 WHERE ""TokenHash"" = $2",
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), hash);

            return row["UserId"]?.GetValue<string>();
        }

        private string GenerateJwt(User user, bool emailVerified)
        {
            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_config["Jwt:Key"]
                    ?? throw new InvalidOperationException("Jwt:Key is not configured")));

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"] ?? "FakeNewsDetector",
                audience: _config["Jwt:Audience"] ?? "FakeNewsDetector",
                claims: new[]
                {
                    new System.Security.Claims.Claim("sub", user.Id),
                    new System.Security.Claims.Claim("email", user.Email),
                    new System.Security.Claims.Claim("name", user.Name),
                    new System.Security.Claims.Claim("email_verified", emailVerified ? "true" : "false"),
                    new System.Security.Claims.Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
                },
                expires: DateTime.UtcNow.Add(AccessTokenLifetime),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static string GenerateRawToken() =>
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
                .Replace("+", "-").Replace("/", "_").Replace("=", "");

        private static string HashToken(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

        private static string HashPassword(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt,
                iterations: 100_000, HashAlgorithmName.SHA256, 32);
            return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
        }

        private static bool VerifyPassword(string password, string stored)
        {
            var parts = stored.Split(':');
            if (parts.Length != 2) return false;
            try
            {
                var salt = Convert.FromBase64String(parts[0]);
                var expected = Convert.FromBase64String(parts[1]);
                var actual = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(password), salt,
                    iterations: 100_000, HashAlgorithmName.SHA256, 32);
                return CryptographicOperations.FixedTimeEquals(expected, actual);
            }
            catch { return false; }
        }

        private static bool BoolVal(JsonObject o, string key)
        {
            var v = o[key];
            if (v == null) return false;
            if (v is JsonValue jv && jv.TryGetValue<bool>(out var b)) return b;
            return v.ToString() is "true" or "True" or "1";
        }

        private static string WebUtil(string s) => System.Net.WebUtility.HtmlEncode(s);
    }
}
