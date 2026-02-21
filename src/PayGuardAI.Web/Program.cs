using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using PayGuardAI.Core.Services;
using PayGuardAI.Data;
using PayGuardAI.Data.Services;
using PayGuardAI.Web;
using PayGuardAI.Web.Components;
using PayGuardAI.Web.Hubs;
using PayGuardAI.Web.Models;
using PayGuardAI.Web.Services;
using Prometheus;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using System.Threading.RateLimiting;

// Bootstrap logger captures fatal startup errors before DI is ready
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog - structured JSON logs (searchable in Railway log viewer)
builder.Host.UseSerilog((context, services, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithThreadId()
    .Enrich.WithProperty("Application", "PayGuardAI")
    .WriteTo.Console(new CompactJsonFormatter()));

// Add MudBlazor services
builder.Services.AddMudServices();

// Add SignalR for real-time updates
builder.Services.AddSignalR();

// Add caching and HTTP context access
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

// Add session for demo authentication state
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Add authentication with feature flag support (OAuth or Demo)
builder.Services.AddPayGuardAuthentication(builder.Configuration);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireReviewer", policy => policy.RequireRole("Reviewer", "Manager", "Admin"));
    options.AddPolicy("RequireManager", policy => policy.RequireRole("Manager", "Admin"));
});

// Add rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var tenantId = context.Items["TenantId"] as string ?? "default";
        return RateLimitPartition.GetFixedWindowLimiter(tenantId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:PermitLimit") ?? 60,
            Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int?>("RateLimiting:WindowSeconds") ?? 60),
            QueueLimit = 0
        });
    });
});

// Add basic monitoring (logging handled by Serilog)
builder.Services.AddHealthChecks();

// Add Entity Framework with feature flag-based database selection
var usePostgres = builder.Configuration.IsPostgresEnabled();

if (usePostgres)
{
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("PostgresConnection") 
            ?? "Host=localhost;Port=5432;Database=payguard_dev;Username=postgres;Password=postgres"));
}
else
{
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") 
            ?? "Data Source=payguardai.db"));
}

// Register application services
builder.Services.AddScoped<IRiskScoringService, RiskScoringService>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<DemoDataSeeder>();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<IAlertingService, AlertingService>();
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<IDatabaseMigrationService, DatabaseMigrationService>();

// Register alerting service — Slack when enabled, plain log fallback otherwise
var slackEnabled = builder.Configuration.GetValue<bool>("FeatureFlags:SlackAlertsEnabled");
if (slackEnabled)
{
    builder.Services.AddHttpClient<IAlertingService, SlackAlertService>();
}
else
{
    builder.Services.AddScoped<IAlertingService, AlertingService>();
}

// Register Prometheus metrics service
builder.Services.AddSingleton<IMetricsService, PrometheusMetricsService>();

// Register MFA service
builder.Services.Configure<MfaSettings>(builder.Configuration.GetSection("Mfa"));
builder.Services.AddScoped<IMfaService, TotpMfaService>();

// Configure OAuth settings
builder.Services.Configure<OAuthSettings>(builder.Configuration.GetSection("OAuth"));

// Register payment providers
builder.Services.AddHttpClient<IAfriexApiService, AfriexApiService>();
builder.Services.AddScoped<AfriexProvider>();
builder.Services.AddHttpClient<FlutterwaveProvider>();
builder.Services.AddScoped<IPaymentProviderFactory, PaymentProviderFactory>();

// Register webhook signature service
builder.Services.AddSingleton<IWebhookSignatureService, WebhookSignatureService>();

// Register Paystack billing service
builder.Services.AddHttpClient<IBillingService, PaystackBillingService>();

// Add controllers for API endpoints (webhooks)
builder.Services.AddControllers();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Ensure database is created and seed demo data
using (var scope = app.Services.CreateScope())
{
    var migrationService = scope.ServiceProvider.GetRequiredService<IDatabaseMigrationService>();
    await migrationService.EnsureDatabaseReadyAsync();
    
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Active database: {DatabaseType}", migrationService.GetActiveDatabaseType());
    
    // Seed demo data in development
    if (app.Environment.IsDevelopment())
    {
        var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
        await seeder.SeedAsync(25);
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Serilog structured request logging (replaces UseHttpLogging)
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "{RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
    options.GetLevel = (httpContext, elapsed, ex) => ex != null
        ? LogEventLevel.Error
        : httpContext.Response.StatusCode >= 500
            ? LogEventLevel.Error
            : elapsed > 2000
                ? LogEventLevel.Warning
                : LogEventLevel.Information;
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? "");
        diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.FirstOrDefault() ?? "");
        diagnosticContext.Set("TenantId", httpContext.Items["TenantId"] as string ?? "unknown");
        diagnosticContext.Set("UserId", httpContext.User?.Identity?.Name ?? "anonymous");
    };
});
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<TenantResolutionMiddleware>();

app.UseSession();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapMetrics("/metrics");  // Prometheus scrape endpoint
app.MapControllers(); // Map API controllers
app.MapHub<TransactionHub>("/hubs/transactions"); // SignalR hub
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
