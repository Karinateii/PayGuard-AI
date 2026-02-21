namespace PayGuardAI.Tests.Integration;

/// <summary>
/// Defines a shared collection for integration tests that use the same WebApplicationFactory.
/// This avoids Serilog "logger is already frozen" errors from multiple factory instances.
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationTestCollection : ICollectionFixture<PayGuardApiWebApplicationFactory>
{
}
