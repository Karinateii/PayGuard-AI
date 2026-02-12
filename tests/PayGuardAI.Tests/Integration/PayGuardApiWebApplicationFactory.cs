using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

        return base.CreateHost(builder);
    }
}
