using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PayGuardAI.Core.Services;
using PayGuardAI.Data;
using PayGuardAI.Data.Services;
using PayGuardAI.Web;

namespace PayGuardAI.Tests.Integration;

public class PayGuardApiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"test_{Guid.NewGuid()}.db";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Configure test services — replace the SQLite connection with a fresh database file
        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext options registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            // Re-register with a unique SQLite file so schema is always fresh
            services.AddDbContext<ApplicationDbContext>((sp, options) =>
            {
                options.UseSqlite($"DataSource={_dbName}");
            });
        });

        var host = base.CreateHost(builder);

        // Ensure DB is created with the latest schema
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.EnsureCreated();

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        // Clean up the test database file
        if (File.Exists(_dbName))
        {
            try { File.Delete(_dbName); } catch { /* best effort */ }
        }
    }
}
