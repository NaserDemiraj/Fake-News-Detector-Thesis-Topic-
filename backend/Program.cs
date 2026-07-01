using FakeNewsDetector.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Threading.RateLimiting;

// Disable default claim type mapping so "sub" stays as "sub"
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

// Render and most PaaS hosts inject the port to listen on via the PORT env var.
// Locally PORT is unset, so the launch profile / ASPNETCORE_URLS is used instead.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
    builder.WebHost.UseUrls($"http://+:{port}");

builder.WebHost.UseKestrel(o => o.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(4));

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddHttpClient();
builder.Services.AddHealthChecks();

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key must be set in configuration");

if (jwtKey.StartsWith("CHANGE-THIS", StringComparison.OrdinalIgnoreCase) || jwtKey.Length < 32)
    throw new InvalidOperationException(
        "Jwt:Key is the default placeholder or too short (min 32 chars). " +
        "Set a real secret via environment variable JWT__KEY or dotnet user-secrets.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Prevent JsonWebTokenHandler from remapping "sub" → ClaimTypes.NameIdentifier
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "FakeNewsDetector",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "FakeNewsDetector",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            NameClaimType = "sub"
        };
    });

builder.Services.AddAuthorization();

// Rate limiting: 20 requests per minute — per user ID when authenticated, per IP otherwise.
// On PaaS hosts (HF Spaces, Render) the app is behind a reverse proxy, so the real client
// IP is in X-Forwarded-For; RemoteIpAddress would be the proxy (one shared bucket for all).
static string ClientPartitionKey(HttpContext context)
{
    var userId = context.User?.FindFirst("sub")?.Value;
    if (!string.IsNullOrEmpty(userId)) return "user:" + userId;

    var fwd = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    var ip = !string.IsNullOrWhiteSpace(fwd)
        ? fwd.Split(',')[0].Trim()                                   // first hop = original client
        : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return "ip:" + ip;
}

builder.Services.AddRateLimiter(options =>
{
    // Global: generous cap so normal browsing never trips it.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientPartitionKey(context),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 40,
                Window = TimeSpan.FromMinutes(1)
            }));

    // Stricter policy for the expensive analysis endpoint (drains the LLM quota):
    // 10 analyses/minute per client. Applied via [EnableRateLimiting("analyze")].
    options.AddPolicy("analyze", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientPartitionKey(context),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1)
            }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// CORS
var allowedOrigins = builder.Configuration["AllowedOrigins"]?.Split(',', StringSplitOptions.RemoveEmptyEntries)
    ?? new[] { "http://localhost:3000" };

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        // Allow configured web origins plus browser-extension origins.
        // Safe because the API authenticates via Bearer tokens, not cookies.
        policy.SetIsOriginAllowed(origin =>
                  allowedOrigins.Contains(origin)
                  || origin.StartsWith("chrome-extension://")
                  || origin.StartsWith("moz-extension://"))
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Application services
builder.Services.AddSingleton<NeonHttpService>();
builder.Services.AddSingleton<TavilyService>();
builder.Services.AddSingleton<FactCheckService>();
builder.Services.AddSingleton<INewsAnalyzerService, NewsAnalyzerService>();
builder.Services.AddScoped<ISavedAnalysisService, SavedAnalysisService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailSender, EmailSender>();

var app = builder.Build();

// Run DB migrations on startup
await RunMigrationsAsync(app);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

static async Task RunMigrationsAsync(WebApplication app)
{
    try
    {
        var neon = app.Services.GetRequiredService<NeonHttpService>();
        var logger = app.Services.GetRequiredService<ILogger<Program>>();

        // Create Users table
        await neon.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS ""Users"" (
                ""Id""           TEXT PRIMARY KEY,
                ""Email""        TEXT NOT NULL UNIQUE,
                ""PasswordHash"" TEXT NOT NULL,
                ""Name""         TEXT NOT NULL DEFAULT '',
                ""CreatedAt""    TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
            )");

        // Add UserId column to SavedAnalyses (no FK to stay safe with existing data)
        await neon.ExecuteAsync(@"
            ALTER TABLE ""SavedAnalyses""
            ADD COLUMN IF NOT EXISTS ""UserId"" TEXT");

        // Add ContentHash column for dedup caching
        await neon.ExecuteAsync(@"
            ALTER TABLE ""SavedAnalyses""
            ADD COLUMN IF NOT EXISTS ""ContentHash"" TEXT");

        // Add IsPublic column for shareable links
        await neon.ExecuteAsync(@"
            ALTER TABLE ""SavedAnalyses""
            ADD COLUMN IF NOT EXISTS ""IsPublic"" BOOLEAN NOT NULL DEFAULT FALSE");

        // Add EmailVerified column to Users
        await neon.ExecuteAsync(@"
            ALTER TABLE ""Users""
            ADD COLUMN IF NOT EXISTS ""EmailVerified"" BOOLEAN NOT NULL DEFAULT FALSE");

        // Ensure email uniqueness even if table was created before the constraint existed
        await neon.ExecuteAsync(@"
            CREATE UNIQUE INDEX IF NOT EXISTS ""idx_users_email"" ON ""Users"" (""Email"")");

        // Allow Url to be NULL (text analyses have no URL) — idempotent on repeated runs
        await neon.ExecuteAsync(@"
            ALTER TABLE ""SavedAnalyses"" ALTER COLUMN ""Url"" DROP NOT NULL");

        // Refresh tokens (revocable, rotated)
        await neon.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS ""RefreshTokens"" (
                ""Id""        TEXT PRIMARY KEY,
                ""TokenHash"" TEXT NOT NULL,
                ""UserId""    TEXT NOT NULL,
                ""ExpiresAt"" TIMESTAMP WITH TIME ZONE NOT NULL,
                ""RevokedAt"" TIMESTAMP WITH TIME ZONE,
                ""CreatedAt"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
            )");

        // Single-use tokens for password reset & email verification
        await neon.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS ""UserTokens"" (
                ""Id""        TEXT PRIMARY KEY,
                ""TokenHash"" TEXT NOT NULL,
                ""UserId""    TEXT NOT NULL,
                ""Type""      TEXT NOT NULL,
                ""ExpiresAt"" TIMESTAMP WITH TIME ZONE NOT NULL,
                ""UsedAt""    TIMESTAMP WITH TIME ZONE,
                ""CreatedAt"" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW()
            )");

        logger.LogInformation("DB migration complete");
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "DB migration failed — app will continue but auth/history may not work");
    }
}
