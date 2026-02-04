using PayGuardAI.Core.Entities;
using PayGuardAI.Core.Services;
using PayGuardAI.Data;

namespace PayGuardAI.Web.Services;

/// <summary>
/// Seeds demo data for hackathon presentation.
/// </summary>
public class DemoDataSeeder
{
    private readonly ApplicationDbContext _context;
    private readonly ITransactionService _transactionService;
    private readonly ILogger<DemoDataSeeder> _logger;
    private readonly Random _random = new(42); // Fixed seed for reproducibility

    private readonly string[] _transactionTypes = { "SEND", "RECEIVE", "DEPOSIT", "WITHDRAW" };
    private readonly string[] _currencies = { "NGN", "USD", "GBP", "KES", "GHS", "ZAR" };
    private readonly string[] _countries = { "NG", "US", "GB", "KE", "GH", "ZA", "CA", "DE" };

    public DemoDataSeeder(
        ApplicationDbContext context,
        ITransactionService transactionService,
        ILogger<DemoDataSeeder> logger)
    {
        _context = context;
        _transactionService = transactionService;
        _logger = logger;
    }

    public async Task SeedAsync(int count = 25)
    {
        if (_context.Transactions.Any())
        {
            _logger.LogInformation("Demo data already exists, skipping seed");
            return;
        }

        _logger.LogInformation("Seeding {Count} demo transactions...", count);

        var scenarios = GetDemoScenarios();

        foreach (var scenario in scenarios)
        {
            try
            {
                var payload = CreateWebhookPayload(scenario);
                await _transactionService.ProcessWebhookAsync(payload);
                _logger.LogDebug("Seeded transaction: {Scenario}", scenario.Description);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to seed scenario: {Scenario}", scenario.Description);
            }
        }

        // Add some random transactions
        for (int i = scenarios.Count; i < count; i++)
        {
            try
            {
                var payload = CreateRandomWebhookPayload();
                await _transactionService.ProcessWebhookAsync(payload);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to seed random transaction {Index}", i);
            }
        }

        _logger.LogInformation("Demo data seeding complete");
    }

    private List<DemoScenario> GetDemoScenarios()
    {
        return new List<DemoScenario>
        {
            // High-risk scenarios
            new()
            {
                Description = "Large transaction from high-risk corridor",
                Amount = 15000m,
                SourceCountry = "NG",
                DestinationCountry = "IR",
                SenderId = "high-risk-user-001"
            },
            new()
            {
                Description = "Round amount potential structuring",
                Amount = 10000m,
                SourceCountry = "US",
                DestinationCountry = "NG",
                SenderId = "suspicious-user-002"
            },
            new()
            {
                Description = "New customer large transaction",
                Amount = 8500m,
                SourceCountry = "GB",
                DestinationCountry = "KE",
                SenderId = "new-user-003"
            },
            
            // Medium-risk scenarios
            new()
            {
                Description = "Moderate amount standard corridor",
                Amount = 3500m,
                SourceCountry = "US",
                DestinationCountry = "GH",
                SenderId = "regular-user-004"
            },
            new()
            {
                Description = "Multiple transactions same day",
                Amount = 2000m,
                SourceCountry = "CA",
                DestinationCountry = "NG",
                SenderId = "frequent-user-005"
            },
            
            // Low-risk scenarios
            new()
            {
                Description = "Small routine remittance",
                Amount = 250m,
                SourceCountry = "US",
                DestinationCountry = "NG",
                SenderId = "trusted-user-006"
            },
            new()
            {
                Description = "Regular recurring transfer",
                Amount = 500m,
                SourceCountry = "GB",
                DestinationCountry = "KE",
                SenderId = "verified-user-007"
            },
            new()
            {
                Description = "Family support payment",
                Amount = 350m,
                SourceCountry = "DE",
                DestinationCountry = "GH",
                SenderId = "family-sender-008"
            },
            
            // Critical scenarios for demo
            new()
            {
                Description = "CRITICAL: Sanctioned country transaction",
                Amount = 5000m,
                SourceCountry = "NG",
                DestinationCountry = "KP",
                SenderId = "blocked-user-009"
            },
            new()
            {
                Description = "CRITICAL: High velocity + large amount",
                Amount = 25000m,
                SourceCountry = "ZA",
                DestinationCountry = "SY",
                SenderId = "velocity-user-010"
            }
        };
    }

    private string CreateWebhookPayload(DemoScenario scenario)
    {
        var id = Guid.NewGuid().ToString();
        var createdAt = DateTime.UtcNow.AddMinutes(-_random.Next(1, 120));
        var type = _transactionTypes[_random.Next(_transactionTypes.Length)];

        return $$"""
        {
            "event": "transaction.completed",
            "data": {
                "id": "{{id}}",
                "type": "{{type}}",
                "status": "COMPLETED",
                "amount": {{scenario.Amount}},
                "sourceCurrency": "USD",
                "destinationCurrency": "{{GetCurrencyForCountry(scenario.DestinationCountry)}}",
                "senderId": "{{scenario.SenderId}}",
                "receiverId": "receiver-{{Guid.NewGuid().ToString()[..8]}}",
                "sourceCountry": "{{scenario.SourceCountry}}",
                "destinationCountry": "{{scenario.DestinationCountry}}",
                "createdAt": "{{createdAt:O}}"
            }
        }
        """;
    }

    private string CreateRandomWebhookPayload()
    {
        var id = Guid.NewGuid().ToString();
        var amount = _random.Next(100, 5000) + _random.Next(0, 100) / 100m;
        var srcCountry = _countries[_random.Next(_countries.Length)];
        var destCountry = _countries[_random.Next(_countries.Length)];
        var type = _transactionTypes[_random.Next(_transactionTypes.Length)];
        var createdAt = DateTime.UtcNow.AddMinutes(-_random.Next(1, 480));

        return $$"""
        {
            "event": "transaction.completed",
            "data": {
                "id": "{{id}}",
                "type": "{{type}}",
                "status": "COMPLETED",
                "amount": {{amount}},
                "sourceCurrency": "{{GetCurrencyForCountry(srcCountry)}}",
                "destinationCurrency": "{{GetCurrencyForCountry(destCountry)}}",
                "senderId": "user-{{Guid.NewGuid().ToString()[..8]}}",
                "receiverId": "receiver-{{Guid.NewGuid().ToString()[..8]}}",
                "sourceCountry": "{{srcCountry}}",
                "destinationCountry": "{{destCountry}}",
                "createdAt": "{{createdAt:O}}"
            }
        }
        """;
    }

    private static string GetCurrencyForCountry(string country) => country switch
    {
        "NG" => "NGN",
        "US" => "USD",
        "GB" => "GBP",
        "KE" => "KES",
        "GH" => "GHS",
        "ZA" => "ZAR",
        "CA" => "CAD",
        "DE" => "EUR",
        _ => "USD"
    };

    private class DemoScenario
    {
        public string Description { get; set; } = "";
        public decimal Amount { get; set; }
        public string SourceCountry { get; set; } = "US";
        public string DestinationCountry { get; set; } = "NG";
        public string SenderId { get; set; } = "";
    }
}
