using System.Text;
using FamilyLedger.Api.Health;
using FamilyLedger.Api.Middleware;
using FamilyLedger.Api.Services.FinanceHub;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    options.UseUtcTimestamp = true;
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IStatementOcrService, TesseractStatementOcrService>();
builder.Services.AddScoped<IStatementParser, StatementParser>();
builder.Services.AddScoped<IStatementRetentionService, SupabaseStatementRetentionService>();
builder.Services.AddHostedService<StatementRetentionCleanupService>();
builder.Services.AddSingleton<IFinancialDataProvider, UnconfiguredAccountAggregatorProvider>();
builder.Services.AddHealthChecks()
    .AddCheck(
        "self",
        () => HealthCheckResult.Healthy(),
        tags: new[] { "live" })
    .AddCheck<SupabaseHealthCheck>(
        "supabase-auth",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready" });

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var supabaseUrl = builder.Configuration["Supabase:Url"];
var supabaseKey = builder.Configuration["Supabase:AnonKey"];
builder.Services.AddScoped<Supabase.Client>(_ =>
    new Supabase.Client(supabaseUrl!, supabaseKey, new Supabase.SupabaseOptions { AutoRefreshToken = false, AutoConnectRealtime = false }));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.Authority = $"{supabaseUrl}/auth/v1";
        options.Audience = "authenticated";
        options.MetadataAddress = $"{supabaseUrl}/auth/v1/.well-known/openid-configuration";

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"{supabaseUrl}/auth/v1",
            ValidateAudience = true,
            ValidAudience = "authenticated",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Authentication");
                logger.LogWarning(
                    "JWT authentication failed for correlation ID {CorrelationId}",
                    context.HttpContext.TraceIdentifier);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = HealthResponseWriter.WriteAsync
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
});
app.MapControllers();

app.Run();
