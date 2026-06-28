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

        public AuthService(NeonHttpService neon, IConfiguration config, ILogger<AuthService> logger)
        {
            _neon = neon;
            _config = config;
            _logger = logger;
        }

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
                @"INSERT INTO ""Users"" (""Id"",""Email"",""PasswordHash"",""Name"",""CreatedAt"")
                  VALUES ($1,$2,$3,$4,$5)",
                user.Id, user.Email, user.PasswordHash, user.Name,
                user.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));

            _logger.LogInformation("New user registered: {Email}", email);

            return new AuthResponse
            {
                Token = GenerateJwt(user),
                UserId = user.Id,
                Email = user.Email,
                Name = user.Name
            };
        }

        public async Task<AuthResponse?> LoginAsync(LoginRequest request)
        {
            var email = request.Email.Trim().ToLowerInvariant();

            var rows = await _neon.QueryAsync(
                @"SELECT ""Id"",""Email"",""PasswordHash"",""Name"" FROM ""Users"" WHERE ""Email"" = $1", email);

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

            return new AuthResponse
            {
                Token = GenerateJwt(user),
                UserId = user.Id,
                Email = user.Email,
                Name = user.Name
            };
        }

        private string GenerateJwt(User user)
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
                    new System.Security.Claims.Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
                },
                expires: DateTime.UtcNow.AddDays(30),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

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
    }
}
