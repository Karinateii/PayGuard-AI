using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using PayGuardAI.Core.Entities;
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
using Microsoft.OpenApi.Models;

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

// Configure forwarded headers for Railway reverse proxy (HTTPS termination at edge)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor 
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Add session for demo authentication state
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

// Add authentication with feature flag support (OAuth or Demo)
builder.Services.AddPayGuardAuthentication(builder.Configuration);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireReviewer", policy => policy.RequireRole("Reviewer", "Manager", "Admin", "SuperAdmin"));
    options.AddPolicy("RequireManager", policy => policy.RequireRole("Manager", "Admin", "SuperAdmin"));
    options.AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin", "SuperAdmin"));
    options.AddPolicy("RequireSuperAdmin", policy => policy.RequireRole("SuperAdmin"));

    // Permission-based policies (checked via PermissionAuthorizationHandler)
    options.AddPolicy("CanViewTransactions", policy => policy.Requirements.Add(new PermissionRequirement(Permission.ViewTransactions)));
    options.AddPolicy("CanReviewTransactions", policy => policy.Requirements.Add(new PermissionRequirement(Permission.ReviewTransactions)));
    options.AddPolicy("CanManageRules", policy => policy.Requirements.Add(new PermissionRequirement(Permission.ManageRules)));
    options.AddPolicy("CanViewReports", policy => policy.Requirements.Add(new PermissionRequirement(Permission.ViewReports)));
    options.AddPolicy("CanViewAuditLog", policy => policy.Requirements.Add(new PermissionRequirement(Permission.ViewAuditLog)));
    options.AddPolicy("CanManageTeam", policy => policy.Requirements.Add(new PermissionRequirement(Permission.ManageTeam)));
    options.AddPolicy("CanManageRoles", policy => policy.Requirements.Add(new PermissionRequirement(Permission.ManageRoles)));
    options.AddPolicy("CanManageApiKeys", policy => policy.Requirements.Add(new PermissionRequirement(Permission.ManageApiKeys)));
    options.AddPolicy("CanManageWebhooks", policy => policy.Requirements.Add(new PermissionRequirement(Permission.ManageWebhooks)));
    options.AddPolicy("CanManageSettings", policy => policy.Requirements.Add(new PermissionRequirement(Permission.ManageSettings)));
    options.AddPolicy("CanManageBilling", policy => policy.Requirements.Add(new PermissionRequirement(Permission.ManageBilling)));
    options.AddPolicy("CanManageNotifications", policy => policy.Requirements.Add(new PermissionRequirement(Permission.ManageNotifications)));
});

// Add rate limiting — global per-tenant + per-API-key
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Global rate limit partitioned by tenant
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

    // Per-API-key rate limit policy (stricter, for programmatic access)
    options.AddPolicy("PerApiKey", context =>
    {
        var apiKeyId = context.Items["ApiKeyId"] as Guid?;
        var partitionKey = apiKeyId?.ToString() ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:PerKeyPermitLimit") ?? 120,
            Window = TimeSpan.FromSeconds(builder.Configuration.GetValue<int?>("RateLimiting:PerKeyWindowSeconds") ?? 60),
            QueueLimit = 2
        });
    });
});

// Add basic monitoring (logging handled by Serilog)
builder.Services.AddHealthChecks();

// Add Entity Framework with feature flag-based database selection
var usePostgres = builder.Configuration.IsPostgresEnabled();

if (usePostgres)
{
    var pgConnString = builder.Configuration.GetConnectionString("PostgresConnection")
        ?? throw new InvalidOperationException(
            "PostgreSQL is enabled but no connection string configured. " +
            "Set ConnectionStrings:PostgresConnection or DATABASE_URL environment variable.");
    
    // Railway provides postgresql:// URL format — convert to ADO.NET format for Npgsql
    if (pgConnString.StartsWith("postgresql://") || pgConnString.StartsWith("postgres://"))
    {
        var uri = new Uri(pgConnString);
        var userInfo = uri.UserInfo.Split(':');
        pgConnString = $"Host={uri.Host};Port={uri.Port};Database={uri.AbsolutePath.TrimStart('/')};Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Prefer;Trust Server Certificate=true";
    }
    
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(pgConnString));
    builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
        options.UseNpgsql(pgConnString), ServiceLifetime.Scoped);
}
else
{
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") 
            ?? "Data Source=payguardai.db"));
    builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
        options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") 
            ?? "Data Source=payguardai.db"), ServiceLifetime.Scoped);
}

// Register application services
builder.Services.AddScoped<IRiskScoringService, RiskScoringService>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<DemoDataSeeder>();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<CircuitHandler, TenantCircuitHandler>();
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
builder.Services.AddHttpClient<WiseProvider>();
builder.Services.AddScoped<IPaymentProviderFactory, PaymentProviderFactory>();

// Register webhook signature service
builder.Services.AddSingleton<IWebhookSignatureService, WebhookSignatureService>();

// Register outbound webhook delivery service (POSTs events to customer endpoints)
builder.Services.AddHttpClient<IWebhookDeliveryService, WebhookDeliveryService>();

// Register billing services: Paystack (African) + Flutterwave (international)
builder.Services.AddHttpClient<PaystackBillingService>();
builder.Services.AddKeyedScoped<IBillingService, PaystackBillingService>("paystack",
    (sp, _) => sp.GetRequiredService<PaystackBillingService>());

builder.Services.AddHttpClient<FlutterwaveBillingService>();
builder.Services.AddKeyedScoped<IBillingService, FlutterwaveBillingService>("flutterwave",
    (sp, _) => sp.GetRequiredService<FlutterwaveBillingService>());

// Factory to resolve the correct billing provider based on config/preference
builder.Services.AddScoped<BillingServiceFactory>();

// Default IBillingService registration (for existing code that injects IBillingService directly)
builder.Services.AddScoped<IBillingService>(sp =>
    sp.GetRequiredService<BillingServiceFactory>().GetDefault());

// Register admin dashboard service
builder.Services.AddScoped<IAdminService, AdminService>();

// Register advanced analytics service for custom reports and ROI metrics
builder.Services.AddScoped<IAdvancedAnalyticsService, AdvancedAnalyticsService>();

// Register tenant onboarding service
builder.Services.AddScoped<ITenantOnboardingService, TenantOnboardingService>();

// Register RBAC service for permission-based access control
builder.Services.AddScoped<IRbacService, RbacService>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

// Register magic link (passwordless) authentication service
builder.Services.AddScoped<IMagicLinkService, MagicLinkService>();

// Register email notification service — uses Resend HTTP API, self-disables if no API key
builder.Services.AddScoped<IEmailNotificationService, EmailNotificationService>();

// Register ML scoring and training services — learns from HITL feedback
builder.Services.AddScoped<IMLScoringService, MLScoringService>();
builder.Services.AddScoped<IMLTrainingService, MLTrainingService>();

// Register ML auto-retraining background service — checks hourly for new labeled data
builder.Services.AddHostedService<MLRetrainingBackgroundService>();

// Register Rule Marketplace service — browse, import, and analyze rule templates
builder.Services.AddScoped<IRuleMarketplaceService, RuleMarketplaceService>();

// Add controllers for API endpoints (webhooks)
builder.Services.AddControllers();

// Add Swagger/OpenAPI documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "PayGuard AI API",
        Version = "v1",
        Description = "AI-powered transaction risk monitoring, compliance automation, and fraud detection for cross-border payments.\n\n" +
                    "## Authentication\n" +
                    "Most webhook endpoints are unauthenticated (they verify via HMAC signatures instead). " +
                    "The simulate endpoint requires cookie-based authentication.\n\n" +
                    "## Webhook Signatures\n" +
                    "All inbound webhooks **must** include a valid signature header:\n" +
                    "- **Afriex**: `X-Afriex-Signature` (HMAC-SHA256)\n" +
                    "- **Flutterwave**: `verif-hash` (shared secret)\n" +
                    "- **Wise**: `X-Signature-SHA256` (RSA-SHA256)\n" +
                    "- **Paystack**: `x-paystack-signature` (HMAC-SHA512)\n\n" +
                    "## Rate Limiting\n" +
                    "API endpoints are rate-limited per tenant (60 req/min) and per API key (120 req/min).",
        Contact = new OpenApiContact
        {
            Name = "PayGuard AI Support",
            Email = "support@payguardai.xyz",
            Url = new Uri("https://payguard-ai-production.up.railway.app")
        },
        License = new OpenApiLicense
        {
            Name = "MIT",
            Url = new Uri("https://opensource.org/licenses/MIT")
        }
    });

    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-API-Key",
        Description = "API key for programmatic access. Generate keys from the Admin > API Keys page."
    });

    options.AddSecurityDefinition("WebhookSignature", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-Afriex-Signature",
        Description = "HMAC-SHA256 webhook signature for payload verification."
    });

    // Include XML documentation comments
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);

    // Group endpoints by controller
    options.TagActionsBy(api => new[] { api.GroupName ?? api.ActionDescriptor.RouteValues["controller"] ?? "Default" });
    options.DocInclusionPredicate((_, _) => true);
});

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
        // Set tenant context so seeded data belongs to the demo tenant
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var defaultTenant = app.Configuration["MultiTenancy:DefaultTenantId"] ?? "afriex-demo";
        tenantContext.TenantId = defaultTenant;
        
        // Also set tenant on DbContext so query filters work
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.SetTenantId(defaultTenant);
        
        var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
        await seeder.SeedAsync(25);
    }
}

// Forward headers from Railway's reverse proxy (must be FIRST in pipeline)
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Swagger UI — available in all environments for API documentation
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "PayGuard AI API v1");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "PayGuard AI - API Documentation";
    options.DefaultModelsExpandDepth(1);
    options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
    options.EnableTryItOutByDefault();
});

// Only redirect to HTTPS in development (Railway handles HTTPS termination at edge)
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

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

// Security middleware pipeline (order matters!)
app.UseMiddleware<SecurityHeadersMiddleware>();    // Security headers on every response
app.UseMiddleware<InputValidationMiddleware>();     // Reject oversized / malicious payloads
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ApiKeyAuthenticationMiddleware>(); // Validate X-API-Key for /api/ endpoints

app.UseSession();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();   // Resolve tenant from claims/header (AFTER auth)
app.UseMiddleware<IpWhitelistMiddleware>();          // Enforce per-tenant IP whitelist
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health").RequireAuthorization();
app.MapMetrics("/metrics").RequireAuthorization("RequireAdmin");  // Prometheus scrape endpoint
app.MapControllers(); // Map API controllers
app.MapHub<TransactionHub>("/hubs/transactions"); // SignalR hub
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
