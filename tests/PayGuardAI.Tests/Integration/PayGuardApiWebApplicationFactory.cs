using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PayGuardAI.Data;
using PayGuardAI.Web;

namespace PayGuardAI.Tests.Integration;

public class PayGuardApiWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Configure test services
        builder.ConfigureServices(services =>
        {
            // Add any test-specific service overrides here
            // For example, replace production services with test doubles
        });

        var host = base.CreateHost(builder);

        // Ensure DB is created for integration tests
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.EnsureCreated();

        return host;
    }
}
