using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using PayGuardAI.Core.Services;
using PayGuardAI.Data;
using PayGuardAI.Data.Services;
using PayGuardAI.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add MudBlazor services
builder.Services.AddMudServices();

// Add Entity Framework with SQLite
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") 
        ?? "Data Source=payguardai.db"));

// Register application services
builder.Services.AddScoped<IRiskScoringService, RiskScoringService>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();

// Add controllers for API endpoints (webhooks)
builder.Services.AddControllers();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Ensure database is created and migrations applied
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();
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

app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers(); // Map API controllers
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
