using FakeNewsDetector.Services;
using FakeNewsDetector.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseKestrel(o => o.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(4));
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddHttpClient();
builder.Services.AddHealthChecks();

// Rate limiting: 20 requests per minute per IP
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// CORS - allow only the configured frontend origin(s)
var allowedOrigins = builder.Configuration["AllowedOrigins"]?.Split(',', StringSplitOptions.RemoveEmptyEntries)
    ?? new[] { "http://localhost:3000" };

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Database - connection string must be provided via env var in production
var connectionString = builder.Configuration.GetConnectionString("NeonDb");
if (string.IsNullOrEmpty(connectionString))
{
    if (builder.Environment.IsDevelopment())
    {
        connectionString = "Host=localhost;Database=fakenews;Username=postgres;Password=postgres;";
        Console.WriteLine("WARNING: NeonDb connection string not configured. Falling back to localhost. Set ConnectionStrings__NeonDb env var.");
    }
    else
    {
        throw new InvalidOperationException(
            "NeonDb connection string not configured. Set the ConnectionStrings__NeonDb environment variable.");
    }
}

builder.Services.AddDbContext<FakeNewsDetectorDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddSingleton<INewsAnalyzerService, NewsAnalyzerService>();
builder.Services.AddScoped<ISavedAnalysisService, SavedAnalysisService>();

var app = builder.Build();

// Run pending migrations on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<FakeNewsDetectorDbContext>();
    try
    {
        dbContext.Database.Migrate();
        app.Logger.LogInformation("Database migrated successfully");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning("Database migration failed (may already exist): {Message}", ex.Message);
    }
}

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
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
