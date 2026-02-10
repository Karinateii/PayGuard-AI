using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using PayGuardAI.Core.Services;
using PayGuardAI.Data;
using PayGuardAI.Data.Services;
using PayGuardAI.Web.Components;
using PayGuardAI.Web.Hubs;
using PayGuardAI.Web.Services;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add MudBlazor services
builder.Services.AddMudServices();

// Add SignalR for real-time updates
builder.Services.AddSignalR();

// Add caching and HTTP context access
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

// Add authentication & authorization (demo scheme)
builder.Services.AddAuthentication("Demo")
    .AddScheme<AuthenticationSchemeOptions, DemoAuthenticationHandler>("Demo", _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireReviewer", policy => policy.RequireRole("Reviewer", "Manager", "Admin"));
    options.AddPolicy("RequireManager", policy => policy.RequireRole("Manager", "Admin"));
});

builder.Services.AddCascadingAuthenticationState();

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

// Add basic monitoring
builder.Services.AddHttpLogging(_ => { });
builder.Services.AddHealthChecks();

// Add Entity Framework with SQLite
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") 
        ?? "Data Source=payguardai.db"));

// Register application services
builder.Services.AddScoped<IRiskScoringService, RiskScoringService>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<DemoDataSeeder>();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<IAlertingService, AlertingService>();
builder.Services.AddScoped<CurrentUserService>();

// Register Afriex API services
builder.Services.AddHttpClient<IAfriexApiService, AfriexApiService>();
builder.Services.AddSingleton<IWebhookSignatureService, WebhookSignatureService>();

// Add controllers for API endpoints (webhooks)
builder.Services.AddControllers();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Ensure database is created and seed demo data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();
    
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

app.UseHttpLogging();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<TenantResolutionMiddleware>();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapControllers(); // Map API controllers
app.MapHub<TransactionHub>("/hubs/transactions"); // SignalR hub
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
